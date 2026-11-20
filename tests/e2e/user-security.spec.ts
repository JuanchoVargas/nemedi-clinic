// Seguridad de las cuentas de usuario, por API (proyecto api de pnpm test).
//
//   · Escalada de privilegios: nadie asigna un rol mayor que el suyo ni se cambia el propio; recepción
//     (Admin) no edita, desactiva, cambia el correo ni borra a un SuperAdmin. Editar los propios datos
//     sigue funcionando.
//   · Un usuario desactivado o eliminado no renueva la sesión: /auth/refresh responde 401 y su refresh
//     token queda revocado (reactivarlo no revive el viejo).
//   · El JWT ya emitido no sobrevive a la cuenta: desactivada, eliminada o con otro rol, la siguiente
//     petición responde 401 (antes un desactivado se reactivaba y un SuperAdmin degradado borraba al dueño).
//   · Un correo identifica una sola cuenta en todo el sistema (clínicas y plataforma): repetirlo se
//     rechaza al crear y al editar, y el login no se bloquea por un repetido que ya exista en la base.
//   · La sede de un usuario es de su misma clínica.
//   · auth/register no es anónimo: sin sesión de dueño o recepción no se crea nadie.
//
// Crea sus propias dos clínicas por el camino real (plataforma → bootstrap-admin → cambio de clave) para no
// tocar al dueño de la demo, y las borra físicamente al final.
//
// Requisitos: los del gate de aislamiento (API en 5055, PlatformAdmin de desarrollo, sqlcmd).
import { randomBytes } from "node:crypto";
import { test, expect, type APIRequestContext } from "@playwright/test";
import { startApi } from "./api-process";
import { PLATFORM_EMAIL, PLATFORM_PASSWORD } from "./platform-credentials";
import { hardDeleteTenants, lit, sql } from "./sql";

/** Segunda instancia del API con límites de intentos bajos (los contadores son de cada proceso). */
const THROTTLE_PORT = 5059;
const THROTTLE_BASE = `http://localhost:${THROTTLE_PORT}`;
const startThrottledApi = (env: Record<string, string>) =>
  startApi(
    { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__RetardoBaseMs: "0",
      ForwardedHeaders__KnownProxies__0: "127.0.0.1", ForwardedHeaders__KnownProxies__1: "::1", ...env },
    `${THROTTLE_BASE}/api/health`, {}, ["--urls", THROTTLE_BASE]);
const deviceCookieOf = (res: { headersArray(): { name: string; value: string }[] }) =>
  res.headersArray().filter((h) => h.name.toLowerCase() === "set-cookie" && h.value.startsWith("nemedi_device="))
    .map((h) => /^nemedi_device=([^;]*)/.exec(h.value)?.[1])[0];

// Sin claves literales: la del admin de plataforma sale de platform-credentials; las demás las
// inventa cada corrida.
const NEMEDI_CHANNEL_ID = "11111111-1111-1111-1111-111111111111";
const RUN = Date.now().toString(36);
/** Cumple PasswordPolicy (letra + dígito, 8+) y cambia en cada corrida. */
const newPassword = () => `Pw${randomBytes(9).toString("base64url")}7`;
const OWNER_PASSWORD = newPassword();
const STAFF_PASSWORD = newPassword();

type Session = { token: string; refreshToken: string; mustChangePassword: boolean; userInfo: { id: string; rol: string; tenantId: string; branchId: string | null } };
type Usuario = { id: string; email: string };

const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function login(api: APIRequestContext, email: string, password: string): Promise<Session> {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email} → ${await res.text()}`).toBe(200);
  return (await res.json()) as Session;
}

/** Primer ingreso de una cuenta con clave temporal: la cambia y devuelve la sesión ya utilizable. */
async function firstLogin(api: APIRequestContext, email: string, temporal: string, nueva: string): Promise<Session> {
  const primer = await login(api, email, temporal);
  const res = await api.post("/api/v1/auth/change-password", {
    ...auth(primer.token), data: { currentPassword: temporal, newPassword: nueva },
  });
  expect(res.status(), `change-password ${email} → ${await res.text()}`).toBe(200);
  return (await res.json()) as Session;
}

async function createClinic(api: APIRequestContext, platformToken: string, label: string) {
  const res = await api.post("/api/v1/platform/tenants", {
    ...auth(platformToken),
    data: {
      nombre: `E2E Usuarios ${label} ${RUN}`, nit: `E2E-U${label}-${RUN}`, telefono: "6010000000",
      email: `clinica-${label}-${RUN}@e2e.local`, channelId: NEMEDI_CHANNEL_ID, plan: "Basico",
    },
  });
  expect(res.status(), `crear clínica → ${await res.text()}`).toBeLessThan(300);
  const tenantId = ((await res.json()) as { id: string }).id;

  const email = `dueno-${label}-${RUN}@e2e.local`;
  const boot = await api.post(`/api/v1/platform/tenants/${tenantId}/bootstrap-admin`, {
    ...auth(platformToken), data: { adminNombre: "Dueño", adminApellido: label, adminEmail: email },
  });
  expect(boot.status(), `bootstrap-admin → ${await boot.text()}`).toBe(200);
  const { passwordTemporal } = (await boot.json()) as { passwordTemporal: string };
  const session = await firstLogin(api, email, passwordTemporal, OWNER_PASSWORD);
  return { tenantId, email, session };
}

async function register(api: APIRequestContext, token: string, rol: string, nombre: string): Promise<Usuario> {
  const email = `${nombre.toLowerCase()}-${RUN}@e2e.local`;
  const res = await api.post("/api/v1/auth/register", {
    ...auth(token), data: { nombre, apellido: `E2E ${RUN}`, email, password: STAFF_PASSWORD, rol },
  });
  expect(res.status(), `register ${rol} → ${await res.text()}`).toBe(201);
  return { id: ((await res.json()) as { id: string }).id, email };
}

async function getUser(api: APIRequestContext, token: string, id: string) {
  const res = await api.get(`/api/v1/users/${id}`, auth(token));
  expect(res.status()).toBe(200);
  return (await res.json()) as { nombre: string; email: string; rol: string; isActive: boolean; branchId: string | null };
}

test.describe("Seguridad de usuarios", () => {
  const tenants: string[] = [];
  let platformToken = "";
  let owner = { id: "", email: "", token: "" };
  let recepcion = { id: "", email: "", token: "" };
  let cabina: Usuario = { id: "", email: "" };
  let ownerBranchId = "";
  let otra = { email: "", token: "", branchId: "" };

  test.beforeAll(async ({ request: api }) => {
    platformToken = (await login(api, PLATFORM_EMAIL, PLATFORM_PASSWORD)).token;

    const a = await createClinic(api, platformToken, "A");
    tenants.push(a.tenantId);
    owner = { id: a.session.userInfo.id, email: a.email, token: a.session.token };
    ownerBranchId = a.session.userInfo.branchId ?? "";
    // Otra clínica: para correos repetidos entre clínicas y sedes ajenas
    const b = await createClinic(api, platformToken, "B");
    tenants.push(b.tenantId);
    otra = { email: b.email, token: b.session.token, branchId: b.session.userInfo.branchId ?? "" };
    expect(ownerBranchId && otra.branchId, "bootstrap-admin crea la Sede Principal").toBeTruthy();

    const r = await register(api, owner.token, "Admin", "Recepcion");
    const rs = await firstLogin(api, r.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    recepcion = { id: r.id, email: r.email, token: rs.token };
    cabina = await register(api, owner.token, "Esteticista", "Cabina");
  });

  test.afterAll(() => {
    hardDeleteTenants(tenants);
  });

  test("recepción no puede ponerse SuperAdmin", async ({ request: api }) => {
    const res = await api.put(`/api/v1/users/${recepcion.id}`, {
      ...auth(recepcion.token), data: { rol: "SuperAdmin" },
    });
    expect(res.status(), await res.text()).toBe(403);
    expect((await getUser(api, owner.token, recepcion.id)).rol).toBe("Admin");

    // Tampoco por el número del enum
    const numerico = await api.put(`/api/v1/users/${recepcion.id}`, { ...auth(recepcion.token), data: { rol: 0 } });
    expect(numerico.status()).toBe(403);
  });

  test("recepción no asciende a una esteticista a Admin", async ({ request: api }) => {
    const res = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(recepcion.token), data: { rol: "Admin" } });
    expect(res.status(), await res.text()).toBe(403);
    expect((await getUser(api, owner.token, cabina.id)).rol).toBe("Esteticista");
  });

  test("recepción no edita, desactiva, cambia el correo ni borra al dueño", async ({ request: api }) => {
    const intentos = [
      { isActive: false },
      { email: `robado-${RUN}@e2e.local` },
      { nombre: "Cambiado" },
    ];
    for (const data of intentos) {
      const res = await api.put(`/api/v1/users/${owner.id}`, { ...auth(recepcion.token), data });
      expect(res.status(), `PUT dueño ${JSON.stringify(data)} → ${await res.text()}`).toBe(403);
    }
    const del = await api.delete(`/api/v1/users/${owner.id}`, auth(recepcion.token));
    expect(del.status(), await del.text()).toBe(403);

    const dueno = await getUser(api, owner.token, owner.id);
    expect(dueno).toMatchObject({ nombre: "Dueño", email: owner.email, isActive: true, rol: "SuperAdmin" });
  });

  test("recepción no edita, desactiva, cambia el correo, borra ni restablece la clave de otra recepción", async ({ request: api }) => {
    const par = await register(api, owner.token, "Admin", "Par");
    const intentos = [
      { email: `par-robado-${RUN}@e2e.local` },
      { isActive: false },
      { nombre: "Cambiado" },
    ];
    for (const data of intentos) {
      const res = await api.put(`/api/v1/users/${par.id}`, { ...auth(recepcion.token), data });
      expect(res.status(), `PUT recepción ${JSON.stringify(data)} → ${await res.text()}`).toBe(403);
    }
    const reset = await api.post(`/api/v1/users/${par.id}/reset-password`, auth(recepcion.token));
    expect(reset.status(), "reset-password de otra recepción (sin volcar el cuerpo: traería la clave)").toBe(403);
    const del = await api.delete(`/api/v1/users/${par.id}`, auth(recepcion.token));
    expect(del.status(), await del.text()).toBe(403);
    expect(await getUser(api, owner.token, par.id)).toMatchObject({ nombre: "Par", email: par.email, isActive: true, rol: "Admin" });

    // Una esteticista sí: recepción le restablece la clave
    const esteticista = await register(api, owner.token, "Esteticista", "Reseteada");
    const resetCabina = await api.post(`/api/v1/users/${esteticista.id}/reset-password`, auth(recepcion.token));
    expect(resetCabina.status(), "reset-password de una esteticista").toBe(200);
  });

  test("nadie se cambia el rol, se desactiva ni se borra a sí mismo", async ({ request: api }) => {
    const bajarse = await api.put(`/api/v1/users/${owner.id}`, { ...auth(owner.token), data: { rol: "Esteticista" } });
    expect(bajarse.status(), await bajarse.text()).toBe(403);
    const desactivarse = await api.put(`/api/v1/users/${owner.id}`, { ...auth(owner.token), data: { isActive: false } });
    expect(desactivarse.status(), await desactivarse.text()).toBe(403);
    const borrarse = await api.delete(`/api/v1/users/${recepcion.id}`, auth(recepcion.token));
    expect(borrarse.status(), await borrarse.text()).toBe(403);
    expect(await getUser(api, owner.token, owner.id)).toMatchObject({ rol: "SuperAdmin", isActive: true });
  });

  test("editar los propios datos sigue funcionando (con el rol de siempre en el cuerpo)", async ({ request: api }) => {
    // Así guarda la web: manda todos los campos, aunque el rol y el estado no cambien
    const propio = await api.put(`/api/v1/users/${recepcion.id}`, {
      ...auth(recepcion.token),
      data: { nombre: "Recepción", apellido: `E2E ${RUN}`, email: recepcion.email, rol: "Admin", branchId: null, isActive: true },
    });
    expect(propio.status(), await propio.text()).toBe(204);

    const dueno = await api.put(`/api/v1/users/${owner.id}`, {
      ...auth(owner.token),
      data: { nombre: "Dueño", apellido: "A", email: owner.email, rol: "SuperAdmin", branchId: null, isActive: true },
    });
    expect(dueno.status(), await dueno.text()).toBe(204);
  });

  test("recepción gestiona esteticistas y el dueño cambia roles", async ({ request: api }) => {
    const desactivar = await api.put(`/api/v1/users/${cabina.id}`, {
      ...auth(recepcion.token), data: { nombre: "Cabina", rol: "Esteticista", isActive: false },
    });
    expect(desactivar.status(), await desactivar.text()).toBe(204);
    expect((await getUser(api, owner.token, cabina.id)).isActive).toBe(false);

    const ascender = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { rol: "Admin", isActive: true } });
    expect(ascender.status(), await ascender.text()).toBe(204);
    expect(await getUser(api, owner.token, cabina.id)).toMatchObject({ rol: "Admin", isActive: true });

    const bajar = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { rol: "Esteticista" } });
    expect(bajar.status(), await bajar.text()).toBe(204);
  });

  test("un rol que no existe se rechaza", async ({ request: api }) => {
    const res = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { rol: 7 } });
    expect(res.status(), await res.text()).toBe(400);
  });

  test("un usuario desactivado no renueva la sesión", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Desactivada");
    const s = await firstLogin(api, u.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    const refresh = (r: Session) => api.post("/api/v1/auth/refresh", { data: { token: r.token, refreshToken: r.refreshToken } });

    // Antes de desactivarla renueva sin problema (la sesión renovada es la que se sigue usando)
    const ok = await refresh(s);
    expect(ok.status(), "refresh antes de desactivar").toBe(200);
    const vigente = (await ok.json()) as Session;

    const off = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { isActive: false } });
    expect(off.status(), await off.text()).toBe(204);
    const tras = await refresh(vigente);
    expect(tras.status(), "refresh tras desactivar (sin volcar el cuerpo: traería tokens)").toBe(401);

    // Desactivar revoca el refresh token: al reactivarla, el viejo ya no sirve (tiene que volver a entrar)
    const on = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { isActive: true } });
    expect(on.status(), await on.text()).toBe(204);
    const revivido = await refresh(vigente);
    expect(revivido.status(), "refresh viejo tras reactivar").toBe(401);
    await login(api, u.email, `${STAFF_PASSWORD}x`);
  });

  test("un usuario desactivado no revive la sesión cambiando la clave", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Revivir");
    const s = await firstLogin(api, u.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);

    const off = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { isActive: false } });
    expect(off.status(), await off.text()).toBe(204);
    // Su JWT sigue vigente hasta que vence, pero no le sirve para sacar un refresh token nuevo
    const res = await api.post("/api/v1/auth/change-password", {
      ...auth(s.token), data: { currentPassword: `${STAFF_PASSWORD}x`, newPassword: `${STAFF_PASSWORD}y` },
    });
    expect(res.status(), "change-password de un usuario desactivado").toBe(401);
  });

  test("un usuario eliminado no renueva la sesión", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Eliminada");
    const s = await firstLogin(api, u.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);

    const del = await api.delete(`/api/v1/users/${u.id}`, auth(owner.token));
    expect(del.status(), await del.text()).toBe(204);
    const res = await api.post("/api/v1/auth/refresh", { data: { token: s.token, refreshToken: s.refreshToken } });
    expect(res.status(), "refresh tras eliminar").toBe(401);
  });

  test("con el JWT de antes, un desactivado no se reactiva, no crea usuarios ni restablece claves", async ({ request: api }) => {
    const u = await register(api, owner.token, "Admin", "Apagada");
    const s = await firstLogin(api, u.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    const off = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { isActive: false } });
    expect(off.status(), await off.text()).toBe(204);

    const reactivarse = await api.put(`/api/v1/users/${u.id}`, { ...auth(s.token), data: { isActive: true } });
    expect(reactivarse.status(), await reactivarse.text()).toBe(401);
    const alta = await api.post("/api/v1/auth/register", {
      ...auth(s.token),
      data: { nombre: "Fantasma", apellido: `E2E ${RUN}`, email: `fantasma-${RUN}@e2e.local`, password: STAFF_PASSWORD, rol: "Esteticista" },
    });
    expect(alta.status(), await alta.text()).toBe(401);
    const reset = await api.post(`/api/v1/users/${cabina.id}/reset-password`, auth(s.token));
    expect(reset.status(), "reset-password con el JWT de un desactivado (sin volcar el cuerpo: traería la clave)").toBe(401);
    const leer = await api.get("/api/v1/users", auth(s.token));
    expect(leer.status(), "cualquier lectura").toBe(401);

    expect((await getUser(api, owner.token, u.id)).isActive).toBe(false);
    expect(sql(`SELECT COUNT(*) FROM Users WHERE Email = ${lit(`fantasma-${RUN}@e2e.local`)};`)[0][0]).toBe("0");
  });

  test("con el JWT de antes, un SuperAdmin degradado o eliminado ya no actúa como dueño", async ({ request: api }) => {
    const socio = await register(api, owner.token, "SuperAdmin", "Socio");
    const s = await firstLogin(api, socio.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    const bajar = await api.put(`/api/v1/users/${socio.id}`, { ...auth(owner.token), data: { rol: "Esteticista" } });
    expect(bajar.status(), await bajar.text()).toBe(204);

    const borrarDueno = await api.delete(`/api/v1/users/${owner.id}`, auth(s.token));
    expect(borrarDueno.status(), await borrarDueno.text()).toBe(401);
    const bajarDueno = await api.put(`/api/v1/users/${owner.id}`, { ...auth(s.token), data: { rol: "Esteticista" } });
    expect(bajarDueno.status(), await bajarDueno.text()).toBe(401);
    expect(await getUser(api, owner.token, owner.id)).toMatchObject({ rol: "SuperAdmin", isActive: true });

    // Con el rol nuevo vuelve a entrar como lo que es ahora (refresh lee el rol de la base)
    const renovada = await api.post("/api/v1/auth/refresh", { data: { token: s.token, refreshToken: s.refreshToken } });
    expect(renovada.status(), "refresh tras degradar").toBe(200);
    const nueva = (await renovada.json()) as Session;
    expect(nueva.userInfo.rol).toBe("Esteticista");
    const comoEsteticista = await api.delete(`/api/v1/users/${owner.id}`, auth(nueva.token));
    expect(comoEsteticista.status(), await comoEsteticista.text()).toBe(403);

    const del = await api.delete(`/api/v1/users/${socio.id}`, auth(owner.token));
    expect(del.status(), await del.text()).toBe(204);
    const trasBorrar = await api.get("/api/v1/users", auth(nueva.token));
    expect(trasBorrar.status(), "JWT de un usuario eliminado").toBe(401);
  });

  test("con el JWT de antes, una clínica borrada ya no responde", async ({ request: api }) => {
    const c = await createClinic(api, platformToken, "C");
    tenants.push(c.tenantId);
    const antes = await api.get("/api/v1/users", auth(c.session.token));
    expect(antes.status(), await antes.text()).toBe(200);

    const borrar = await api.delete(`/api/v1/platform/tenants/${c.tenantId}`, auth(platformToken));
    expect(borrar.status(), await borrar.text()).toBeLessThan(300);
    const despues = await api.get("/api/v1/users", auth(c.session.token));
    expect(despues.status(), await despues.text()).toBe(401);
  });

  test("una clínica inactiva (sin borrar) ya no responde, y el dueño no la apaga por API", async ({ request: api }) => {
    const e = await createClinic(api, platformToken, "E");
    tenants.push(e.tenantId);
    const leer = () => api.get("/api/v1/users", auth(e.session.token));

    // Activar o desactivar una clínica es cosa de la plataforma: el dueño se dejaría por fuera sin vuelta atrás
    const apagar = await api.put(`/api/v1/tenants/${e.tenantId}`, { ...auth(e.session.token), data: { isActive: false } });
    expect(apagar.status(), await apagar.text()).toBe(403);
    const mismo = await api.put(`/api/v1/tenants/${e.tenantId}`, { ...auth(e.session.token), data: { isActive: true, telefono: "6010000001" } });
    expect(mismo.status(), "mandar el estado que ya tiene no es un cambio").toBe(204);
    expect((await leer()).status()).toBe(200);

    // La plataforma la desactiva: las sesiones abiertas se cortan y nadie entra; al reactivarla, vuelven a entrar
    const plataforma = (isActive: boolean) =>
      api.put(`/api/v1/platform/tenants/${e.tenantId}`, { ...auth(platformToken), data: { isActive } });
    const off = await plataforma(false);
    expect(off.status(), await off.text()).toBeLessThan(300);
    expect((await leer()).status(), "JWT de una clínica inactiva").toBe(401);
    const entrar = await api.post("/api/v1/auth/login", { data: { email: e.email, password: OWNER_PASSWORD } });
    expect(entrar.status(), "login en una clínica inactiva").toBe(401);

    const on = await plataforma(true);
    expect(on.status(), await on.text()).toBeLessThan(300);
    await login(api, e.email, OWNER_PASSWORD);
  });

  test("un JWT de antes de cambiar la clave no renueva, ni con el refresh token nuevo", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Sello");
    const clave = `${STAFF_PASSWORD}x`;
    const vieja = await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    const cambio = await api.post("/api/v1/auth/change-password", {
      ...auth(vieja.token), data: { currentPassword: clave, newPassword: `${clave}y` },
    });
    expect(cambio.status(), "change-password").toBe(200);
    const nueva = (await cambio.json()) as Session;

    const mezcla = await api.post("/api/v1/auth/refresh", { data: { token: vieja.token, refreshToken: nueva.refreshToken } });
    expect(mezcla.status(), "JWT viejo + refresh token nuevo").toBe(401);
  });

  test("lo mismo para un admin de plataforma (uno propio de la prueba)", async ({ request: api }) => {
    // Un PlatformAdmin propio, para no cambiar la clave del compartido: se crea por SQL con el hash de una
    // cuenta de la prueba cuya clave se conoce, y se borra al final
    const u = await register(api, owner.token, "Esteticista", "Origenplat");
    const clave = `${STAFF_PASSWORD}x`;
    await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    const email = `plataforma-${RUN}@e2e.local`;
    sql(`INSERT INTO PlatformAdmins (Id, Nombre, Email, PasswordHash, IsActive, MustChangePassword, CreatedAt)
         SELECT NEWID(), 'E2E plataforma', ${lit(email)}, PasswordHash, 1, 0, SYSUTCDATETIME() FROM Users WHERE Id = ${lit(u.id)};`);
    try {
      const vieja = await login(api, email, clave);
      expect(vieja.userInfo.rol).toBe("PlatformAdmin");
      const cambio = await api.post("/api/v1/auth/change-password", {
        ...auth(vieja.token), data: { currentPassword: clave, newPassword: `${clave}p` },
      });
      expect(cambio.status(), "change-password del admin de plataforma").toBe(200);
      const nueva = (await cambio.json()) as Session;

      const mezcla = await api.post("/api/v1/auth/refresh", { data: { token: vieja.token, refreshToken: nueva.refreshToken } });
      expect(mezcla.status(), "JWT viejo + refresh token nuevo (plataforma)").toBe(401);
      const buena = await api.post("/api/v1/auth/refresh", { data: { token: nueva.token, refreshToken: nueva.refreshToken } });
      expect(buena.status(), "la sesión nueva sí renueva").toBe(200);
    } finally {
      sql(`DELETE FROM PlatformAdmins WHERE Email = ${lit(email)};`);
    }
  });

  test("renovar y cambiar la clave a la vez no revive la sesión vieja", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Carrera");
    let clave = `${STAFF_PASSWORD}x`;
    let s = await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    for (let ronda = 0; ronda < 8; ronda++) {
      const nueva = `${clave}${ronda}`;
      const [renovar, cambiar] = await Promise.all([
        api.post("/api/v1/auth/refresh", { data: { token: s.token, refreshToken: s.refreshToken } }),
        api.post("/api/v1/auth/change-password", { ...auth(s.token), data: { currentPassword: clave, newPassword: nueva } }),
      ]);
      expect([200, 401], `refresh en la ronda ${ronda}`).toContain(renovar.status());
      expect([200, 409], `change-password en la ronda ${ronda} (sin volcar el cuerpo)`).toContain(cambiar.status());
      if (cambiar.status() === 200) {
        clave = nueva;
        if (renovar.status() === 200) {
          // La renovación ganó la carrera o la perdió: en ningún caso su refresh token sobrevive al cambio
          const vieja = (await renovar.json()) as Session;
          const revivir = await api.post("/api/v1/auth/refresh", { data: { token: vieja.token, refreshToken: vieja.refreshToken } });
          expect(revivir.status(), `refresh de la sesión de antes del cambio, ronda ${ronda}`).toBe(401);
        }
        s = (await cambiar.json()) as Session;
      } else {
        s = renovar.status() === 200 ? ((await renovar.json()) as Session) : await login(api, u.email, clave);
      }
    }
  });

  test("cambiar o restablecer la clave cierra las demás sesiones", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Sesiones");
    const clave = `${STAFF_PASSWORD}x`;
    await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    const leer = (token: string) => api.get("/api/v1/tenants/current", auth(token));

    // Restablecerla (el dueño) cierra la sesión que tenía abierta
    const abierta = await login(api, u.email, clave);
    expect((await leer(abierta.token)).status()).toBe(200);
    const reset = await api.post(`/api/v1/users/${u.id}/reset-password`, auth(owner.token));
    expect(reset.status(), "reset-password").toBe(200);
    const { passwordTemporal } = (await reset.json()) as { passwordTemporal: string };
    expect((await leer(abierta.token)).status(), "JWT de antes del restablecimiento").toBe(401);

    // Cambiarla ella misma cierra las otras sesiones; la que devuelve change-password sigue
    const nueva = await firstLogin(api, u.email, passwordTemporal, `${clave}y`);
    const otra = await login(api, u.email, `${clave}y`);
    const cambio = await api.post("/api/v1/auth/change-password", {
      ...auth(nueva.token), data: { currentPassword: `${clave}y`, newPassword: `${clave}z` },
    });
    expect(cambio.status(), "change-password").toBe(200);
    const vigente = (await cambio.json()) as Session;
    expect((await leer(otra.token)).status(), "otra sesión de antes del cambio").toBe(401);
    expect((await leer(nueva.token)).status(), "la sesión con la que cambió la clave").toBe(401);
    expect((await leer(vigente.token)).status(), "la sesión nueva").toBe(200);
  });

  test("dos dueños que se desactivan a la vez no dejan la clínica sin dueño", async ({ request: api }) => {
    const d = await createClinic(api, platformToken, "D");
    tenants.push(d.tenantId);
    const otroDueno = await register(api, d.session.token, "SuperAdmin", "Codueno");
    const s = await firstLogin(api, otroDueno.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    const duenosActivos = () =>
      Number(sql(`SELECT COUNT(*) FROM Users WHERE TenantId = ${lit(d.tenantId)} AND Rol = 'SuperAdmin' AND IsActive = 1 AND IsDeleted = 0;`)[0][0]);
    expect(duenosActivos()).toBe(2);

    const [a, b] = await Promise.all([
      api.put(`/api/v1/users/${otroDueno.id}`, { ...auth(d.session.token), data: { isActive: false } }),
      api.put(`/api/v1/users/${d.session.userInfo.id}`, { ...auth(s.token), data: { isActive: false } }),
    ]);
    const codigos = [a.status(), b.status()];
    // Uno gana; el otro recibe 409 (sin dueño) o 401 (su cuenta ya quedó desactivada)
    expect(codigos.filter((c) => c === 204).length, `respuestas ${codigos.join(",")}`).toBe(1);
    expect(duenosActivos(), "dueños activos después").toBe(1);
  });

  test("la cookie de dispositivo solo se entrega a una cuenta activa con su clave definitiva", async ({ request: api }) => {
    const deviceCookie = (res: { headersArray(): { name: string; value: string }[] }) =>
      res.headersArray().filter((h) => h.name.toLowerCase() === "set-cookie" && h.value.startsWith("nemedi_device=")).map((h) => h.value)[0];
    const u = await register(api, owner.token, "Esteticista", "Dispositivo");

    const temporal = await api.post("/api/v1/auth/login", { data: { email: u.email, password: STAFF_PASSWORD } });
    expect(temporal.status()).toBe(200);
    // Sin volcar el valor en el mensaje: es una credencial (de una cuenta de prueba, pero igual)
    expect(deviceCookie(temporal) === undefined, "con la clave temporal no hay cookie").toBe(true);

    const clave = `${STAFF_PASSWORD}x`;
    await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    const definitiva = await api.post("/api/v1/auth/login", { data: { email: u.email, password: clave } });
    expect(definitiva.status()).toBe(200);
    const cookie = deviceCookie(definitiva);
    expect(Boolean(cookie), "con la clave definitiva hay cookie").toBe(true);
    const atributos = (cookie ?? "").split(";").slice(1).join(";"); // sin el valor
    expect(atributos).toMatch(/(^|;)\s*secure/i);
    expect(atributos).toMatch(/httponly/i);
    expect(atributos).toMatch(/samesite=strict/i);

    const off = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { isActive: false } });
    expect(off.status(), await off.text()).toBe(204);
    const inactiva = await api.post("/api/v1/auth/login", { data: { email: u.email, password: clave } });
    expect(inactiva.status()).toBe(401);
    expect(deviceCookie(inactiva) === undefined, "a una cuenta inactiva no").toBe(true);
  });

  test("cambiar la clave (también la temporal) entrega una cookie de dispositivo nueva", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Cambiocookie");
    const primer = await login(api, u.email, STAFF_PASSWORD);
    const cambio = await api.post("/api/v1/auth/change-password", {
      ...auth(primer.token), data: { currentPassword: STAFF_PASSWORD, newPassword: `${STAFF_PASSWORD}x` },
    });
    expect(cambio.status(), "change-password desde la clave temporal").toBe(200);
    expect(Boolean(deviceCookieOf(cambio)), "cookie de dispositivo tras cambiar la clave temporal").toBe(true);
  });

  test("change-password con la cookie de dispositivo no cae en el tope por cuenta", async ({ request: api }) => {
    const u = await register(api, owner.token, "Esteticista", "Topecambio");
    const clave = `${STAFF_PASSWORD}x`;
    await firstLogin(api, u.email, STAFF_PASSWORD, clave);
    const instancia = await startThrottledApi({ RateLimiting__Auth__FallosPorCorreoPorHora: "3", RateLimiting__Auth__FallosPorCorreoEIp: "100" });
    try {
      const entrada = await api.post(`${THROTTLE_BASE}/api/v1/auth/login`, {
        data: { email: u.email, password: clave }, headers: { "X-Forwarded-For": "10.100.0.1" },
      });
      expect(entrada.status()).toBe(200);
      const sesion = (await entrada.json()) as Session;
      const cookie = deviceCookieOf(entrada);
      expect(Boolean(cookie), "cookie tras el ingreso").toBe(true);

      for (let i = 1; i <= 3; i++) {
        const fallo = await api.post(`${THROTTLE_BASE}/api/v1/auth/login`, {
          data: { email: u.email, password: `${clave}mal` }, headers: { "X-Forwarded-For": `10.100.1.${i}` },
        });
        expect(fallo.status(), `ataque ${i}`).toBe(401);
      }
      // Clave actual equivocada a propósito: sin cookie el tope responde 429; con cookie se verifica (400)
      const cambiar = (conCookie: boolean) => api.post(`${THROTTLE_BASE}/api/v1/auth/change-password`, {
        data: { currentPassword: `${clave}otra`, newPassword: `${clave}nueva` },
        headers: {
          Authorization: `Bearer ${sesion.token}`, "X-Forwarded-For": "10.100.2.1",
          Cookie: conCookie ? `nemedi_device=${cookie}` : "nemedi_device=",
        },
      });
      expect((await cambiar(false)).status(), "sin cookie, cuenta en su tope").toBe(429);
      expect((await cambiar(true)).status(), "con su cookie se verifica la clave").toBe(400);
    } finally {
      instancia.stop();
    }
  });

  test.describe("correo repetido de antes de la regla", () => {
    /** Dos cuentas con el mismo correo en clínicas distintas (puesto por SQL: el API ya no lo deja). La víctima es la más antigua. */
    async function duplicadas(api: APIRequestContext, sufijo: string) {
      const victima = await register(api, owner.token, "Esteticista", `Victima${sufijo}`);
      const claveVictima = `${STAFF_PASSWORD}v`;
      await firstLogin(api, victima.email, STAFF_PASSWORD, claveVictima);
      const segunda = await register(api, otra.token, "Esteticista", `Otra${sufijo}`);
      const claveOtra = `${STAFF_PASSWORD}o`;
      await firstLogin(api, segunda.email, STAFF_PASSWORD, claveOtra);
      sql(`UPDATE Users SET Email = ${lit(victima.email)} WHERE Id = ${lit(segunda.id)};`);
      return { victima, claveVictima, segunda, claveOtra };
    }
    const loginEn = (api: APIRequestContext, email: string, password: string, ip: string) =>
      api.post(`${THROTTLE_BASE}/api/v1/auth/login`, { data: { email, password }, headers: { "X-Forwarded-For": ip } });

    test("el ingreso correcto de la otra cuenta no limpia los fallos contra la víctima", async ({ request: api }) => {
      const d = await duplicadas(api, "Ing");
      const instancia = await startThrottledApi({ RateLimiting__Auth__FallosPorCorreoEIp: "3" });
      try {
        const ip = "10.110.0.1";
        expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status()).toBe(401);
        expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status()).toBe(401);
        // La otra cuenta entra con el mismo correo desde la misma IP...
        expect((await loginEn(api, d.victima.email, d.claveOtra, ip)).status(), "la otra cuenta entra").toBe(200);
        // ...y eso no le devuelve intentos a nadie: el tercer fallo bloquea el correo desde esa IP
        expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status()).toBe(401);
        expect((await loginEn(api, d.victima.email, d.claveVictima, ip)).status(), "víctima bloqueada desde esa IP").toBe(429);
      } finally {
        instancia.stop();
        sql(`UPDATE Users SET Email = ${lit(d.segunda.email)} WHERE Id = ${lit(d.segunda.id)};`);
      }
    });

    test("el cambio de clave de la otra cuenta no limpia los fallos contra la víctima", async ({ request: api }) => {
      const d = await duplicadas(api, "Cam");
      const instancia = await startThrottledApi({ RateLimiting__Auth__FallosPorCorreoEIp: "3" });
      try {
        const ip = "10.111.0.1";
        for (let i = 0; i < 3; i++)
          expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status()).toBe(401);
        expect((await loginEn(api, d.victima.email, d.claveVictima, ip)).status(), "víctima bloqueada").toBe(429);

        // La otra cuenta entra desde otra IP y cambia su clave
        const entrada = await loginEn(api, d.victima.email, d.claveOtra, "10.111.0.2");
        expect(entrada.status()).toBe(200);
        const sesion = (await entrada.json()) as Session;
        const cambio = await api.post(`${THROTTLE_BASE}/api/v1/auth/change-password`, {
          headers: { Authorization: `Bearer ${sesion.token}`, "X-Forwarded-For": "10.111.0.2" },
          data: { currentPassword: d.claveOtra, newPassword: `${d.claveOtra}2` },
        });
        expect(cambio.status(), "change-password de la otra cuenta").toBe(200);

        expect((await loginEn(api, d.victima.email, d.claveVictima, ip)).status(), "la víctima sigue bloqueada").toBe(429);
      } finally {
        instancia.stop();
        sql(`UPDATE Users SET Email = ${lit(d.segunda.email)} WHERE Id = ${lit(d.segunda.id)};`);
      }
    });

    test("un change-password inválido de la cuenta más antigua no limpia los contadores compartidos", async ({ request: api }) => {
      const d = await duplicadas(api, "Inv");
      const instancia = await startThrottledApi({ RateLimiting__Auth__FallosPorCorreoEIp: "10" });
      try {
        const ip = "10.112.0.1";
        const entrada = await loginEn(api, d.victima.email, d.claveVictima, ip);
        expect(entrada.status()).toBe(200);
        const sesion = (await entrada.json()) as Session;
        for (let i = 1; i <= 9; i++)
          expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status(), `fallo ${i}`).toBe(401);

        // Clave actual correcta pero la nueva no cumple la política: 400, y no toca los contadores
        const invalido = await api.post(`${THROTTLE_BASE}/api/v1/auth/change-password`, {
          headers: { Authorization: `Bearer ${sesion.token}`, "X-Forwarded-For": ip },
          data: { currentPassword: d.claveVictima, newPassword: "corta" },
        });
        expect(invalido.status(), "change-password inválido").toBe(400);

        expect((await loginEn(api, d.victima.email, `${d.claveVictima}mal`, ip)).status(), "fallo 10").toBe(401);
        expect((await loginEn(api, d.victima.email, d.claveVictima, ip)).status(), "bloqueada tras 10 fallos").toBe(429);
      } finally {
        instancia.stop();
        sql(`UPDATE Users SET Email = ${lit(d.segunda.email)} WHERE Id = ${lit(d.segunda.id)};`);
      }
    });

    test("con el correo compartido la cookie de dispositivo ni exime ni se entrega", async ({ request: api }) => {
      // La víctima entra cuando su correo todavía es solo suyo y recibe su cookie
      const victima = await register(api, owner.token, "Esteticista", "Victimacook");
      const clave = `${STAFF_PASSWORD}v`;
      await firstLogin(api, victima.email, STAFF_PASSWORD, clave);
      const segunda = await register(api, otra.token, "Esteticista", "Otracook");
      await firstLogin(api, segunda.email, STAFF_PASSWORD, `${STAFF_PASSWORD}o`);
      const instancia = await startThrottledApi({ RateLimiting__Auth__FallosPorCorreoPorHora: "3", RateLimiting__Auth__FallosPorCorreoEIp: "100" });
      try {
        const primera = await loginEn(api, victima.email, clave, "10.113.0.1");
        expect(primera.status()).toBe(200);
        const cookie = deviceCookieOf(primera);
        expect(Boolean(cookie), "cookie con el correo todavía único").toBe(true);

        sql(`UPDATE Users SET Email = ${lit(victima.email)} WHERE Id = ${lit(segunda.id)};`);
        const compartido = await loginEn(api, victima.email, clave, "10.113.0.2");
        expect(compartido.status()).toBe(200);
        expect(deviceCookieOf(compartido) === undefined, "con el correo compartido no se entrega").toBe(true);

        for (let i = 1; i <= 3; i++)
          expect((await loginEn(api, victima.email, `${clave}mal`, `10.113.1.${i}`)).status(), `ataque ${i}`).toBe(401);
        const conCookie = await api.post(`${THROTTLE_BASE}/api/v1/auth/login`, {
          data: { email: victima.email, password: clave },
          headers: { "X-Forwarded-For": "10.113.2.1", Cookie: `nemedi_device=${cookie}` },
        });
        expect(conCookie.status(), "con el correo compartido la cookie no exime del tope").toBe(429);
      } finally {
        instancia.stop();
        sql(`UPDATE Users SET Email = ${lit(segunda.email)} WHERE Id = ${lit(segunda.id)};`);
      }
    });
  });

  test("no se crea un usuario con el correo del admin de plataforma ni de otra clínica", async ({ request: api }) => {
    const alta = (email: string) => api.post("/api/v1/auth/register", {
      ...auth(owner.token), data: { nombre: "Copia", apellido: `E2E ${RUN}`, email, password: STAFF_PASSWORD, rol: "Esteticista" },
    });
    for (const email of [PLATFORM_EMAIL, PLATFORM_EMAIL.toUpperCase(), otra.email]) {
      const res = await alta(email);
      expect(res.status(), `alta con ${email} → ${await res.text()}`).toBe(409);
    }
    // El admin de plataforma sigue entrando como tal
    expect((await login(api, PLATFORM_EMAIL, PLATFORM_PASSWORD)).userInfo.rol).toBe("PlatformAdmin");
    expect((await login(api, otra.email, OWNER_PASSWORD)).userInfo.rol).toBe("SuperAdmin");
  });

  test("no se le pone a un usuario el correo del admin de plataforma ni de otra clínica", async ({ request: api }) => {
    for (const email of [PLATFORM_EMAIL, otra.email]) {
      const res = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { email } });
      expect(res.status(), `editar a ${email} → ${await res.text()}`).toBe(409);
    }
    expect((await getUser(api, owner.token, cabina.id)).email).toBe(cabina.email);
  });

  test("el login no se bloquea por un correo que ya esté repetido en la base", async ({ request: api }) => {
    // Datos de antes del arreglo: un usuario de otra clínica con el correo del dueño y luego con el
    // del admin de plataforma (puesto por SQL, porque el API ya no lo deja). Cada uno entra con su clave.
    const dup = await register(api, otra.token, "Esteticista", "Duplicada");
    try {
      sql(`UPDATE Users SET Email = ${lit(owner.email)} WHERE Id = ${lit(dup.id)};`);
      expect((await login(api, owner.email, OWNER_PASSWORD)).userInfo.id.toLowerCase()).toBe(owner.id.toLowerCase());
      expect((await login(api, owner.email, STAFF_PASSWORD)).userInfo.id.toLowerCase()).toBe(dup.id.toLowerCase());

      sql(`UPDATE Users SET Email = ${lit(PLATFORM_EMAIL)} WHERE Id = ${lit(dup.id)};`);
      expect((await login(api, PLATFORM_EMAIL, PLATFORM_PASSWORD)).userInfo.rol).toBe("PlatformAdmin");
      expect((await login(api, PLATFORM_EMAIL, STAFF_PASSWORD)).userInfo.id.toLowerCase()).toBe(dup.id.toLowerCase());
    } finally {
      sql(`UPDATE Users SET Email = ${lit(dup.email)} WHERE Id = ${lit(dup.id)};`);
    }
  });

  test("la sede de un usuario tiene que ser de su clínica", async ({ request: api }) => {
    const ajena = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { branchId: otra.branchId } });
    expect(ajena.status(), await ajena.text()).toBe(400);
    expect((await getUser(api, owner.token, cabina.id)).branchId).toBeNull();

    const propia = await api.put(`/api/v1/users/${cabina.id}`, { ...auth(owner.token), data: { branchId: ownerBranchId } });
    expect(propia.status(), await propia.text()).toBe(204);
    expect((await getUser(api, owner.token, cabina.id)).branchId?.toLowerCase()).toBe(ownerBranchId.toLowerCase());
  });

  test("una sede que se borró no impide editar al usuario que la tiene", async ({ request: api }) => {
    const sede = await api.post("/api/v1/branches", { ...auth(owner.token), data: { nombre: `Sede E2E ${RUN}`, direccion: "Calle E2E 1" } });
    expect(sede.status(), await sede.text()).toBeLessThan(300);
    const sedeId = ((await sede.json()) as { id: string }).id;
    const u = await register(api, owner.token, "Esteticista", "Sedeborrada");
    const asignar = await api.put(`/api/v1/users/${u.id}`, { ...auth(owner.token), data: { branchId: sedeId } });
    expect(asignar.status(), await asignar.text()).toBe(204);

    const borrar = await api.delete(`/api/v1/branches/${sedeId}`, auth(owner.token));
    expect(borrar.status(), await borrar.text()).toBeLessThan(300);
    // La web guarda mandando todos los campos, la sede de siempre incluida
    const editar = await api.put(`/api/v1/users/${u.id}`, {
      ...auth(owner.token), data: { nombre: "Renombrada", branchId: sedeId, rol: "Esteticista", isActive: true },
    });
    expect(editar.status(), await editar.text()).toBe(204);
    expect((await getUser(api, owner.token, u.id)).nombre).toBe("Renombrada");
  });

  test("con la clave temporal pendiente solo se puede cambiar la clave", async ({ request: api }) => {
    const temporal = await register(api, owner.token, "Admin", "Temporal");
    const s = await login(api, temporal.email, STAFF_PASSWORD);
    expect(s.mustChangePassword).toBe(true);

    const email = `creada-con-temporal-${RUN}@e2e.local`;
    const alta = await api.post("/api/v1/auth/register", {
      ...auth(s.token), data: { nombre: "Creada", apellido: `E2E ${RUN}`, email, password: STAFF_PASSWORD, rol: "Esteticista" },
    });
    expect(alta.status(), await alta.text()).toBe(403);
    expect(((await alta.json()) as { code?: string }).code).toBe("password_change_required");
    expect(sql(`SELECT COUNT(*) FROM Users WHERE Email = ${lit(email)};`)[0][0]).toBe("0");

    // Lo que sí puede: renovar la sesión y cambiar la clave
    const renovar = await api.post("/api/v1/auth/refresh", { data: { token: s.token, refreshToken: s.refreshToken } });
    expect(renovar.status(), "refresh con la clave temporal").toBe(200);
    const nueva = (await renovar.json()) as Session;
    const cambiar = await api.post("/api/v1/auth/change-password", {
      ...auth(nueva.token), data: { currentPassword: STAFF_PASSWORD, newPassword: `${STAFF_PASSWORD}x` },
    });
    expect(cambiar.status(), "change-password con la clave temporal").toBe(200);
  });

  test("auth/register exige sesión de dueño o recepción", async ({ request: api }) => {
    const data = { nombre: "Anonimo", apellido: `E2E ${RUN}`, email: `anonimo-${RUN}@e2e.local`, password: STAFF_PASSWORD, rol: "SuperAdmin" };
    const anonimo = await api.post("/api/v1/auth/register", { data });
    expect(anonimo.status(), await anonimo.text()).toBe(401);

    const cab = await firstLogin(api, cabina.email, STAFF_PASSWORD, `${STAFF_PASSWORD}x`);
    const esteticista = await api.post("/api/v1/auth/register", { ...auth(cab.token), data: { ...data, rol: "Esteticista" } });
    expect(esteticista.status(), await esteticista.text()).toBe(403);

    const rolRaro = await api.post("/api/v1/auth/register", { ...auth(owner.token), data: { ...data, rol: 7 } });
    expect(rolRaro.status(), await rolRaro.text()).toBe(400);
    expect(sql(`SELECT COUNT(*) FROM Users WHERE Email = ${lit(data.email)};`)[0][0]).toBe("0");
  });
});

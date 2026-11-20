// Reglas de configuración de la clínica, agenda, historia clínica y reportes (proyecto "api" de pnpm test).
//
//   · Configuración: todos los roles la leen, solo el dueño la cambia (recepción → 403).
//   · Horario de la sede y anticipación mínima: una cita fuera de ellos → 409, al crear y al mover.
//   · Cancelar exige motivo; mover una cita respeta el choque de horario y solo aplica a
//     citas agendadas o confirmadas.
//   · Una nota clínica se corrige dentro de 24 h y solo por su autora (o recepción/dueño); una
//     nota más vieja → 409 (la historia clínica no se altera: va una nota aclaratoria).
//   · Reportes: la esteticista no entra; la cartera cuadra con el saldo del dashboard; Excel real.
// Cada bloque crea lo suyo (sede propia) y lo deja como estaba.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, lit } from "./sql";
import { RIPS_PACIENTE } from "./rips";
import { SUPERADMIN, DEMO_EMAIL, demoPassword } from "./credentials";

const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };
const RUN = Date.now().toString().slice(-8);
const pad = (n: number) => String(n).padStart(2, "0");
/** Fecha local a N días (YYYY-MM-DD). */
const enDias = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};
/** Próximo día (desde mañana) que cae en el día de la semana indicado (0 = domingo). */
const proximo = (dia: number) => {
  for (let n = 1; n <= 7; n++) {
    const d = new Date();
    d.setDate(d.getDate() + n);
    if (d.getDay() === dia) return enDias(n);
  }
  throw new Error("imposible");
};
const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function login(api: APIRequestContext, email: string, password: string) {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email}`).toBe(200);
  return (await res.json()) as { token: string; userInfo: { id: string } };
}
async function call<T>(api: APIRequestContext, method: "get" | "post" | "put" | "delete", token: string, url: string, data?: unknown, status?: number): Promise<T> {
  const res = await api[method](url, { ...auth(token), data });
  const body = await res.text();
  if (status !== undefined) expect(res.status(), `${method.toUpperCase()} ${url} → ${body}`).toBe(status);
  else expect(res.status(), `${method.toUpperCase()} ${url} → ${body}`).toBeLessThan(300);
  return (body ? JSON.parse(body) : null) as T;
}

interface Settings { nombre: string; nit: string; telefono: string; email: string; direccion: string | null; ciudad: string | null; anticipacionMinimaHoras: number; exigirConsentimiento: boolean; ivaProductos: number; ivaServicios: number }

test.describe("Configuración, agenda y notas", () => {
  test.describe.configure({ mode: "serial" });

  let owner = "";
  let laura = "";
  let lauraId = "";
  let camilaToken = "";
  let branchId = "";
  let patientId = "";
  let procedureId = "";
  let original: Settings;
  const citas: string[] = [];

  test.beforeAll(async ({ request: api }) => {
    owner = (await login(api, OWNER.email, OWNER.password)).token;
    const l = await login(api, DEMO_EMAIL.laura, demoPassword());
    laura = l.token;
    lauraId = l.userInfo.id;
    camilaToken = (await login(api, DEMO_EMAIL.camila, demoPassword())).token;
    original = await call<Settings>(api, "get", owner, "/api/v1/tenants/current/settings");

    branchId = (await call<{ id: string }>(api, "post", owner, "/api/v1/branches", { nombre: `Sede E2E ${RUN}`, direccion: "Calle de prueba", telefono: "6010000000" })).id;
    patientId = (await call<{ id: string }>(api, "post", owner, "/api/v1/patients", {
      nombre: "Agenda", apellido: `E2E ${RUN}`, cedula: `85${RUN}`, telefono: "3000000085", ...RIPS_PACIENTE,
    })).id;
    const procs = await call<{ items: { id: string; activo: boolean; duracionMinutos: number }[] }>(api, "get", owner, "/api/v1/procedures?page=1&pageSize=50");
    procedureId = procs.items.find((p) => p.activo && p.duracionMinutos <= 60)!.id;
  });

  test.afterAll(async ({ request: api }) => {
    if (original) await api.put("/api/v1/tenants/current/settings", { ...auth(owner), data: original });
    const ids = citas.map(lit).join(",");
    if (ids) sql(`DELETE FROM Appointments WHERE Id IN (${ids});`);
    if (patientId) sql(`DELETE FROM ClinicalNotes WHERE ClinicalRecordId IN (SELECT Id FROM ClinicalRecords WHERE PatientId = ${lit(patientId)}); DELETE FROM ClinicalRecords WHERE PatientId = ${lit(patientId)}; DELETE FROM Patients WHERE Id = ${lit(patientId)};`);
    if (branchId) sql(`DELETE FROM BranchSchedules WHERE BranchId = ${lit(branchId)}; DELETE FROM Branches WHERE Id = ${lit(branchId)};`);
  });

  const nuevaCita = async (api: APIRequestContext, fechaInicio: string, status = 201) => {
    const res = await api.post("/api/v1/appointments", {
      ...auth(owner),
      data: { patientId, esteticistId: lauraId, procedureId, branchId, fechaInicio, notas: "e2e agenda" },
    });
    expect(res.status(), `cita ${fechaInicio} → ${await res.text()}`).toBe(status);
    if (res.status() === 201) {
      const id = ((await res.json()) as { id: string }).id;
      citas.push(id);
      return id;
    }
    return ((await res.json()) as { error: string }).error;
  };

  test("configuración: todos la leen, solo el dueño la cambia", async ({ request: api }) => {
    expect(original.ivaProductos).toBeGreaterThanOrEqual(0);
    await call(api, "get", laura, "/api/v1/tenants/current/settings");
    const res = await api.put("/api/v1/tenants/current/settings", { ...auth(laura), data: original });
    expect(res.status()).toBe(403);

    const cambiada = await call<Settings>(api, "put", owner, "/api/v1/tenants/current/settings",
      { ...original, direccion: "Cra. 15 # 93-60", ciudad: "Bogotá" });
    expect(cambiada).toMatchObject({ direccion: "Cra. 15 # 93-60", ciudad: "Bogotá" });
  });

  test("horario de la sede: fuera de franja o en día cerrado → 409", async ({ request: api }) => {
    // Lunes a viernes 8–12 y 14–18; fines de semana cerrado
    const dias = [1, 2, 3, 4, 5].flatMap((d) => [
      { diaSemana: d, horaApertura: "08:00:00", horaCierre: "12:00:00" },
      { diaSemana: d, horaApertura: "14:00:00", horaCierre: "18:00:00" },
    ]);
    const guardado = await call<unknown[]>(api, "put", owner, `/api/v1/branches/${branchId}/schedule`, dias);
    expect(guardado).toHaveLength(10);

    const cruzadas = await api.put(`/api/v1/branches/${branchId}/schedule`, {
      ...auth(owner), data: [{ diaSemana: 1, horaApertura: "08:00:00", horaCierre: "12:00:00" }, { diaSemana: 1, horaApertura: "11:00:00", horaCierre: "13:00:00" }],
    });
    expect(cruzadas.status()).toBe(400);

    const lunes = proximo(1);
    await nuevaCita(api, `${lunes}T09:00:00`);
    expect(await nuevaCita(api, `${lunes}T12:30:00`, 409)).toContain("Fuera del horario");
    expect(await nuevaCita(api, `${proximo(0)}T10:00:00`, 409)).toContain("no atiende");
  });

  test("mover una cita: respeta horario, choques y estado", async ({ request: api }) => {
    const martes = proximo(2);
    const a = await nuevaCita(api, `${martes}T08:00:00`);
    const b = await nuevaCita(api, `${martes}T10:00:00`);

    await call(api, "put", owner, `/api/v1/appointments/${a}/reschedule`, { fechaInicio: `${martes}T15:00:00` }, 204);
    const movida = await call<{ fechaInicio: string; fechaFin: string }>(api, "get", owner, `/api/v1/appointments/${a}`);
    expect(movida.fechaInicio).toBe(`${martes}T15:00:00`);

    const choque = await api.put(`/api/v1/appointments/${b}/reschedule`, { ...auth(owner), data: { fechaInicio: `${martes}T15:00:00` } });
    expect(choque.status()).toBe(409);

    const fuera = await api.put(`/api/v1/appointments/${b}/reschedule`, { ...auth(owner), data: { fechaInicio: `${martes}T19:00:00` } });
    expect(fuera.status()).toBe(409);

    // Otra esteticista no mueve la cita de Laura
    const ajena = await api.put(`/api/v1/appointments/${b}/reschedule`, { ...auth(camilaToken), data: { fechaInicio: `${martes}T16:00:00` } });
    expect(ajena.status()).toBe(403);
  });

  test("cancelar exige motivo y lo guarda; una cita cancelada no se mueve", async ({ request: api }) => {
    const id = await nuevaCita(api, `${proximo(3)}T09:00:00`);
    const sinMotivo = await api.put(`/api/v1/appointments/${id}/status`, { ...auth(owner), data: { estado: "Cancelada" } });
    expect(sinMotivo.status()).toBe(400);

    await call(api, "put", owner, `/api/v1/appointments/${id}/status`, { estado: "Cancelada", motivo: "La paciente viajó" }, 204);
    const cita = await call<{ estado: string; motivoCancelacion: string }>(api, "get", owner, `/api/v1/appointments/${id}`);
    expect(cita).toMatchObject({ estado: "Cancelada", motivoCancelacion: "La paciente viajó" });

    const mover = await api.put(`/api/v1/appointments/${id}/reschedule`, { ...auth(owner), data: { fechaInicio: `${proximo(3)}T10:00:00` } });
    expect(mover.status()).toBe(409);
  });

  test("anticipación mínima: una cita demasiado pronto → 409", async ({ request: api }) => {
    await call(api, "put", owner, "/api/v1/tenants/current/settings", { ...original, anticipacionMinimaHoras: 48 });
    const error = await nuevaCita(api, `${enDias(1)}T09:00:00`, 409);
    expect(error).toContain("48 h");
    await call(api, "put", owner, "/api/v1/tenants/current/settings", { ...original, anticipacionMinimaHoras: 0 });
  });

  test("nota clínica: se corrige dentro de 24 h y solo por su autora", async ({ request: api }) => {
    const nota = await call<{ nota: { id: string; editable: boolean } }>(api, "post", laura, `/api/v1/patients/${patientId}/clinical-record/notes`,
      { esteticistId: lauraId, procedimiento: "Valoración", observaciones: "Texto con error", productos: [] }, 201);
    const url = `/api/v1/patients/${patientId}/clinical-record/notes/${nota.nota.id}`;

    await call(api, "put", laura, url, { procedimiento: "Valoración", observaciones: "Texto corregido", evaluacionPaciente: 4 }, 204);
    const notas = await call<{ items: { id: string; observaciones: string; fechaEdicion: string | null; editable: boolean }[] }>(
      api, "get", laura, `/api/v1/patients/${patientId}/clinical-record/notes?page=1&pageSize=10`);
    const corregida = notas.items.find((n) => n.id === nota.nota.id)!;
    expect(corregida.observaciones).toBe("Texto corregido");
    expect(corregida.fechaEdicion).toBeTruthy();
    expect(corregida.editable).toBe(true);

    const otra = await api.put(url, { ...auth(camilaToken), data: { procedimiento: "x", observaciones: "x" } });
    expect(otra.status(), "otra esteticista no corrige la nota").toBe(403);

    // Una nota de hace dos días ya no se toca
    sql(`UPDATE ClinicalNotes SET FechaCreacion = DATEADD(day, -2, FechaCreacion) WHERE Id = ${lit(nota.nota.id)};`);
    const vieja = await api.put(url, { ...auth(laura), data: { procedimiento: "Valoración", observaciones: "Otra vez" } });
    expect(vieja.status()).toBe(409);
    expect((await vieja.json()).error).toContain("nota aclaratoria");
  });
});

test.describe("Reportes", () => {
  test("la esteticista no entra y la cartera cuadra con el dashboard", async ({ request: api }) => {
    const owner = (await login(api, OWNER.email, OWNER.password)).token;
    const laura = (await login(api, DEMO_EMAIL.laura, demoPassword())).token;
    expect((await api.get("/api/v1/reports/sales", auth(laura))).status()).toBe(403);

    const cartera = await call<{ total: number; filas: { total: number }[] }>(api, "get", owner, "/api/v1/reports/cartera");
    const dashboard = await call<{ saldoPendiente: number }>(api, "get", owner, "/api/v1/dashboard");
    expect(cartera.total).toBe(dashboard.saldoPendiente);
    expect(cartera.total).toBe(cartera.filas.reduce((s, f) => s + f.total, 0));

    for (const r of ["sales", "procedures", "production", "cartera"]) {
      const json = await api.get(`/api/v1/reports/${r}`, auth(owner));
      expect(json.status(), r).toBe(200);
      const xlsx = await api.get(`/api/v1/reports/${r}?formato=xlsx`, auth(owner));
      expect(xlsx.headers()["content-type"], r).toContain("spreadsheetml");
      // Un .xlsx es un zip: empieza por "PK"
      expect((await xlsx.body()).subarray(0, 2).toString()).toBe("PK");
    }
  });
});

test.describe("Sesión", () => {
  test("expiration dice el vencimiento real del JWT y el refresh rota el token", async ({ request: api }) => {
    const res = await api.post("/api/v1/auth/login", { data: OWNER });
    const login = (await res.json()) as { token: string; refreshToken: string; expiration: string };
    const exp = JSON.parse(Buffer.from(login.token.split(".")[1], "base64url").toString()).exp as number;
    // expiration llega en hora local sin zona: se compara contra el exp del token con 1 min de tolerancia
    const informado = new Date(login.expiration).getTime() / 1000;
    expect(Math.abs(informado - exp)).toBeLessThan(60);

    const r1 = await api.post("/api/v1/auth/refresh", { data: { token: login.token, refreshToken: login.refreshToken } });
    expect(r1.status()).toBe(200);
    const renovado = (await r1.json()) as { token: string; refreshToken: string };
    expect(renovado.refreshToken).not.toBe(login.refreshToken);

    // El refresh token viejo ya no sirve (rotación)
    const r2 = await api.post("/api/v1/auth/refresh", { data: { token: login.token, refreshToken: login.refreshToken } });
    expect(r2.status()).toBe(401);
  });
});

test.describe("Baja de lote", () => {
  test("un lote vencido se da de baja: sale del stock como Baja, no como consumo", async ({ request: api }) => {
    const owner = (await login(api, OWNER.email, OWNER.password)).token;
    const productId = (await call<{ id: string }>(api, "post", owner, "/api/v1/products", {
      nombre: `Toxina E2E ${RUN}`, tipoProducto: "InsumoCabina", unidadMedida: "vial", stockMinimo: 1, tipoRegulatorio: "Medicamento",
      registroSanitarioInvima: "INVIMA 2020M-E2E",
    })).id;
    try {
      await call(api, "post", owner, "/api/v1/inventory/entries",
        { productId, cantidad: 4, motivoEntrada: "Compra", lote: { numeroLote: `VENCIDO-${RUN}`, fechaVencimiento: enDias(-3) } });
      await call(api, "post", owner, "/api/v1/inventory/entries",
        { productId, cantidad: 2, motivoEntrada: "Compra", lote: { numeroLote: `BUENO-${RUN}`, fechaVencimiento: enDias(200) } });

      const lotes = await call<{ id: string; numeroLote: string; estado: string; cantidadDisponible: number }[]>(api, "get", owner, `/api/v1/products/${productId}/lots`);
      const vencido = lotes.find((l) => l.numeroLote.startsWith("VENCIDO"))!;
      expect(vencido.estado).toBe("Vencido");

      const laura = (await login(api, DEMO_EMAIL.laura, demoPassword())).token;
      const sinPermiso = await api.post(`/api/v1/products/${productId}/lots/${vencido.id}/write-off`, { ...auth(laura), data: { motivo: "x" } });
      expect(sinPermiso.status(), "la esteticista no da de baja").toBe(403);

      const despues = await call<{ id: string; cantidadDisponible: number }[]>(api, "post", owner,
        `/api/v1/products/${productId}/lots/${vencido.id}/write-off`, { motivo: "Vencimiento" });
      expect(despues.find((l) => l.id === vencido.id)!.cantidadDisponible).toBe(0);

      const producto = await call<{ stockActual: number }>(api, "get", owner, `/api/v1/products/${productId}`);
      expect(producto.stockActual, "el stock sigue siendo lo utilizable").toBe(2);

      const movs = await call<{ tipoMovimiento: string; cantidad: number }[]>(api, "get", owner, `/api/v1/inventory/movements/product/${productId}`);
      expect(movs.find((m) => m.tipoMovimiento === "Baja")?.cantidad).toBe(4);

      const reporte = await call<{ grupos: { productos: { productId: string; consumoPeriodo: number; bajasPeriodo: number }[] }[] }>(
        api, "get", owner, "/api/v1/products/regulatory-report");
      const fila = reporte.grupos.flatMap((g) => g.productos).find((p) => p.productId === productId)!;
      expect(fila).toMatchObject({ consumoPeriodo: 0, bajasPeriodo: 4 });

      const otraVez = await api.post(`/api/v1/products/${productId}/lots/${vencido.id}/write-off`, { ...auth(owner), data: {} });
      expect(otraVez.status()).toBe(409);
    } finally {
      sql(`DELETE FROM InventoryMovements WHERE ProductId = ${lit(productId)}; DELETE FROM InventoryEntries WHERE ProductId = ${lit(productId)}; DELETE FROM ProductLots WHERE ProductId = ${lit(productId)}; DELETE FROM Products WHERE Id = ${lit(productId)};`);
    }
  });
});

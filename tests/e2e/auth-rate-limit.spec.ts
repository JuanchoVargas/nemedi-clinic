// Límite de intentos en el ingreso (proyecto api de pnpm test).
//
// Levanta una SEGUNDA instancia del API (puerto 5058) con límites bajos por configuración, para no
// depender de los valores de producción ni bloquear al API normal (los contadores viven en memoria
// de cada proceso). Distintas IPs se simulan con X-Forwarded-For, que esa instancia acepta porque
// declara el loopback como proxy de confianza. Comprueba que:
//   · sin proxies declarados, X-Forwarded-For se ignora (antes cualquiera fijaba su IP con él);
//   · login, refresh y change-password comparten un cupo por IP: al pasarlo, 429 con { error } y
//     Retry-After;
//   · los fallos de una cuenta desde una IP la bloquean desde esa IP (429 aun con la clave correcta),
//     pero la clave correcta desde otra IP entra: 10 intentos ya no dejan al dueño ni al admin de
//     plataforma por fuera. La cuenta solo se bloquea en todas partes con muchos fallos por hora, y ni
//     así desde una IP donde ya entró bien;
//   · los contadores van por cuenta: las variantes del correo que SQL Server considera iguales
//     (caracteres invisibles, ancho, mayúsculas) no dan intentos nuevos;
//   · el intento se reserva antes de verificar: N peticiones simultáneas no pasan del cupo;
//   · miles de correos basura no hacen olvidar el bloqueo de una cuenta real, y un correo de más de
//     254 caracteres ni se procesa (400);
//   · change-password también cuenta y su 429 trae Retry-After;
//   · un correo que no existe tarda lo mismo que una clave equivocada (no se puede enumerar por tiempo).
import { test, expect, type APIRequestContext } from "@playwright/test";
import { startApi, type ApiProcess } from "./api-process";
import { PLATFORM_EMAIL, PLATFORM_PASSWORD } from "./platform-credentials";

const PORT = 5058;
const BASE = `http://localhost:${PORT}`;
const RUN = Date.now().toString(36);
/** Clave equivocada inventada (nunca la de nadie). */
const WRONG = `Nope${RUN}9`;
const TRUSTED_LOOPBACK = { ForwardedHeaders__KnownProxies__0: "127.0.0.1", ForwardedHeaders__KnownProxies__1: "::1" };
/** Lo mismo declarado como redes (ForwardedHeaders:KnownNetworks). */
const TRUSTED_LOOPBACK_NETWORKS = { ForwardedHeaders__KnownNetworks__0: "127.0.0.0/8", ForwardedHeaders__KnownNetworks__1: "::1/128" };

test.describe.configure({ mode: "serial" });

const login = (request: APIRequestContext, email: string, password: string, ip?: string) =>
  request.post(`${BASE}/api/v1/auth/login`, {
    data: { email, password },
    headers: ip ? { "X-Forwarded-For": ip } : {},
  });

/** Lleva letras y dígitos a su forma de ancho completo ("a" → "ａ"); "@" y "." quedan, para que siga siendo un correo. */
const fullWidth = (s: string) => s.replace(/[A-Za-z0-9]/g, (c) => String.fromCharCode(c.charCodeAt(0) + 0xfee0));

test.describe("Límite de intentos en el ingreso", () => {
  let api: ApiProcess | undefined;

  test.afterEach(() => api?.stop());

  test("sin proxies de confianza configurados, X-Forwarded-For no cambia la IP", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "2", RateLimiting__Auth__RetardoBaseMs: "0" },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    // Tres IPs inventadas en la cabecera: si se le creyera, cada una tendría su propio cupo
    const codigos: number[] = [];
    for (let i = 1; i <= 3; i++)
      codigos.push((await login(request, `xff-${i}-${RUN}@e2e.local`, WRONG, `10.90.0.${i}`)).status());
    expect(codigos, "la tercera petición desde la misma conexión pasa el cupo de 2").toEqual([401, 401, 429]);
  });

  test("ASPNETCORE_FORWARDEDHEADERS_ENABLED no vuelve a abrir X-Forwarded-For", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "2", RateLimiting__Auth__RetardoBaseMs: "0", ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true" },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const codigos: number[] = [];
    for (let i = 1; i <= 3; i++)
      codigos.push((await login(request, `xff-env-${i}-${RUN}@e2e.local`, WRONG, `10.91.0.${i}`)).status());
    expect(codigos, "sin proxies declarados la variable de entorno no hace confiar en la cabecera").toEqual([401, 401, 429]);
  });

  test("login, refresh y change-password comparten un cupo por IP", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "4", RateLimiting__Auth__FallosPorCorreoEIp: "100", RateLimiting__Auth__RetardoBaseMs: "0" },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const nadie = (n: number) => login(request, `nadie-${n}-${RUN}@e2e.local`, WRONG);
    expect((await nadie(1)).status()).toBe(401);
    expect((await nadie(2)).status()).toBe(401);
    expect((await request.post(`${BASE}/api/v1/auth/refresh`, { data: { token: "x", refreshToken: "y" } })).status()).toBe(401);
    expect((await request.post(`${BASE}/api/v1/auth/change-password`, { data: {} })).status()).toBe(401);

    const bloqueado = await nadie(3);
    expect(bloqueado.status(), "quinto intento de la misma IP en el minuto").toBe(429);
    expect(bloqueado.headers()["retry-after"]).toBeTruthy();
    expect(((await bloqueado.json()) as { error: string }).error).toContain("intentos");
    expect((await request.post(`${BASE}/api/v1/auth/refresh`, { data: { token: "x", refreshToken: "y" } })).status()).toBe(429);

    // El resto del API no entra en ese cupo
    expect((await request.get(`${BASE}/api/health`)).status()).toBe(200);
  });

  test("los fallos bloquean el correo desde esa IP, no al dueño desde otra", async ({ request }) => {
    api = await startApi(
      {
        RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "3",
        RateLimiting__Auth__FallosPorCorreoPorHora: "5", RateLimiting__Auth__RetardoBaseMs: "0", ...TRUSTED_LOOPBACK,
      },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const atacante = "10.20.0.1";
    for (let i = 0; i < 3; i++)
      expect((await login(request, PLATFORM_EMAIL, `${WRONG}${i}`, atacante)).status(), `fallo ${i + 1}`).toBe(401);

    const desdeAtacante = await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, atacante);
    expect(desdeAtacante.status(), "clave correcta desde la IP de los fallos").toBe(429);
    expect(desdeAtacante.headers()["retry-after"]).toBeTruthy();
    expect((await login(request, PLATFORM_EMAIL.toUpperCase(), PLATFORM_PASSWORD, atacante)).status()).toBe(429);

    const dueno = await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, "10.20.0.2");
    expect(dueno.status(), "clave correcta desde otra IP").toBe(200);

    // Otro correo, desde la misma IP, sigue con su cupo
    expect((await login(request, `otro-${RUN}@e2e.local`, WRONG, atacante)).status()).toBe(401);

    // Rotando IPs: el correo se bloquea en todas partes al llegar a FallosPorCorreoPorHora
    const rotado = `rotado-${RUN}@e2e.local`;
    for (let i = 1; i <= 5; i++)
      expect((await login(request, rotado, WRONG, `10.30.0.${i}`)).status(), `fallo rotando ${i}`).toBe(401);
    expect((await login(request, rotado, WRONG, "10.30.0.99")).status(), "sexta IP").toBe(429);
  });

  test("el correo cuenta igual con otro ancho, mayúsculas o espacios", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "3", RateLimiting__Auth__RetardoBaseMs: "0" },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const email = `ancho-${RUN}@e2e.local`;
    expect((await login(request, fullWidth(email), WRONG)).status()).toBe(401);
    expect((await login(request, fullWidth(email.toUpperCase()), WRONG)).status()).toBe(401);
    expect((await login(request, `  ${email.toUpperCase()} `, WRONG)).status()).toBe(401);
    expect((await login(request, email, WRONG)).status(), "cuarto intento del mismo correo").toBe(429);
  });

  test("intentos simultáneos no pasan del cupo", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "10", RateLimiting__Auth__RetardoBaseMs: "0" },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const email = `paralelo-${RUN}@e2e.local`;
    const respuestas = await Promise.all(Array.from({ length: 30 }, () => login(request, email, WRONG)));
    const codigos = respuestas.map((r) => r.status());
    expect(codigos.filter((c) => c === 401).length, `401 de 30 simultáneos (${codigos.join(",")})`).toBe(10);
    expect(codigos.filter((c) => c === 429).length).toBe(20);
  });

  test("un correo que no existe tarda lo mismo que una clave equivocada", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__RetardoBaseMs: "0", ...TRUSTED_LOOPBACK },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const medir = async (email: string, ip: string) => {
      const inicio = performance.now();
      expect((await login(request, email, WRONG, ip)).status()).toBe(401);
      return performance.now() - inicio;
    };
    const mediana = (xs: number[]) => [...xs].sort((a, b) => a - b)[Math.floor(xs.length / 2)];

    await medir(`calentar-${RUN}@e2e.local`, "10.40.0.250"); // la primera petición paga el arranque
    const existe: number[] = [];
    const noExiste: number[] = [];
    for (let i = 1; i <= 5; i++) {
      existe.push(await medir(PLATFORM_EMAIL, `10.40.0.${i}`));
      noExiste.push(await medir(`fantasma-${i}-${RUN}@e2e.local`, `10.40.1.${i}`));
    }
    const conCuenta = mediana(existe);
    const sinCuenta = mediana(noExiste);
    expect(conCuenta, "verificar una clave real cuesta un BCrypt").toBeGreaterThan(20);
    expect(sinCuenta, `sin cuenta ${sinCuenta.toFixed(0)} ms vs con cuenta ${conCuenta.toFixed(0)} ms`).toBeGreaterThan(conCuenta * 0.5);
  });

  test("las variantes del correo que SQL Server iguala no dan intentos nuevos", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "3", RateLimiting__Auth__RetardoBaseMs: "0", ...TRUSTED_LOOPBACK_NETWORKS },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    // U+200D (unión de ancho cero) intercalado: la collation lo ignora y encuentra la misma cuenta
    const [local, dominio] = PLATFORM_EMAIL.split("@");
    const variante = (i: number) => `${local.slice(0, i)}‍${local.slice(i)}@${dominio}`;
    const ip = "10.50.0.1";
    for (let i = 1; i <= 3; i++)
      expect((await login(request, variante(i), WRONG, ip)).status(), `fallo con variante ${i}`).toBe(401);

    expect((await login(request, variante(4), PLATFORM_PASSWORD, ip)).status(), "clave correcta con otra variante").toBe(429);
    expect((await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, ip)).status(), "clave correcta con el correo tal cual").toBe(429);
  });

  test("el tope por cuenta no alcanza al navegador donde el dueño ya entró (cookie de dispositivo)", async ({ request }) => {
    api = await startApi(
      {
        RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "3",
        RateLimiting__Auth__FallosPorCorreoPorHora: "5", RateLimiting__Auth__FallosPorDispositivo: "2",
        RateLimiting__Auth__RetardoBaseMs: "0", ...TRUSTED_LOOPBACK,
      },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    // El dueño entra: el API le deja la cookie de dispositivo conocido (firmada, solo para /api/v1/auth)
    const entrada = await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, "10.60.0.1");
    expect(entrada.status()).toBe(200);
    const setCookie = entrada.headersArray().filter((h) => h.name.toLowerCase() === "set-cookie").map((h) => h.value).join("\n");
    const valor = /nemedi_device=([^;]+)/.exec(setCookie)?.[1];
    // Las aserciones nunca vuelcan el valor (es una credencial)
    expect(Boolean(valor), "cookie de dispositivo tras un ingreso correcto").toBe(true);
    const atributos = setCookie.replace(/nemedi_device=[^;]*/g, "nemedi_device=…");
    expect(atributos).toMatch(/httponly/i);
    expect(atributos).toMatch(/secure/i);
    expect(atributos).toMatch(/samesite=strict/i);
    expect(atributos).toMatch(/path=\/api\/v1\/auth/i);
    // La cookie se manda a mano en cada petición (así el jar del contexto no decide por la prueba)
    const conCookie = (password: string, ip: string, cookie = valor!) =>
      request.post(`${BASE}/api/v1/auth/login`, {
        data: { email: PLATFORM_EMAIL, password },
        headers: { "X-Forwarded-For": ip, Cookie: `nemedi_device=${cookie}` },
      });
    const sinCookie = (password: string, ip: string) =>
      request.post(`${BASE}/api/v1/auth/login`, {
        data: { email: PLATFORM_EMAIL, password },
        headers: { "X-Forwarded-For": ip, Cookie: "nemedi_device=" },
      });

    // Ataque desde muchas IPs: la cuenta pasa su tope por hora
    for (let i = 1; i <= 5; i++)
      expect((await sinCookie(WRONG, `10.61.0.${i}`)).status(), `ataque desde la IP ${i}`).toBe(401);
    expect((await sinCookie(PLATFORM_PASSWORD, "10.62.0.1")).status(), "sin la cookie, desde una IP nueva").toBe(429);
    expect((await conCookie(PLATFORM_PASSWORD, "10.62.0.2")).status(), "con su cookie, aunque la IP sea nueva").toBe(200);

    // Una cookie alterada no sirve
    expect((await conCookie(PLATFORM_PASSWORD, "10.62.0.3", `${valor}x`)).status(), "cookie alterada").toBe(429);

    // La cookie tiene su propio bloqueo corto (FallosPorDispositivo = 2)
    expect((await conCookie(WRONG, "10.63.0.1")).status()).toBe(401);
    expect((await conCookie(WRONG, "10.63.0.2")).status()).toBe(401);
    expect((await conCookie(PLATFORM_PASSWORD, "10.63.0.3")).status(), "dispositivo bloqueado").toBe(429);
  });

  test("correos basura no hacen olvidar un bloqueo y un correo larguísimo no se procesa", async ({ request }) => {
    api = await startApi(
      {
        RateLimiting__Auth__PorIpPorMinuto: "100000", RateLimiting__Auth__FallosPorCorreoEIp: "3",
        RateLimiting__Auth__RetardoBaseMs: "0", RateLimiting__Auth__MaxEntradas: "50", ...TRUSTED_LOOPBACK,
      },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const victimaIp = "10.70.0.1";
    for (let i = 0; i < 3; i++)
      expect((await login(request, PLATFORM_EMAIL, WRONG, victimaIp)).status()).toBe(401);
    expect((await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, victimaIp)).status(), "bloqueada").toBe(429);

    for (let lote = 0; lote < 5; lote++)
      await Promise.all(Array.from({ length: 20 }, (_, i) =>
        login(request, `basura-${lote}-${i}-${RUN}@e2e.local`, WRONG, "10.71.0.1")));

    expect((await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, victimaIp)).status(), "sigue bloqueada tras 100 correos basura").toBe(429);

    const largo = `${"x".repeat(300)}@e2e.local`;
    expect((await login(request, largo, WRONG, "10.72.0.1")).status(), "correo de más de 254 caracteres").toBe(400);
  });

  test("change-password cuenta los fallos y su 429 trae Retry-After", async ({ request }) => {
    api = await startApi(
      { RateLimiting__Auth__PorIpPorMinuto: "1000", RateLimiting__Auth__FallosPorCorreoEIp: "3", RateLimiting__Auth__RetardoBaseMs: "0", ...TRUSTED_LOOPBACK },
      `${BASE}/api/health`, {}, ["--urls", BASE]);

    const ip = "10.80.0.1";
    const sesion = await login(request, PLATFORM_EMAIL, PLATFORM_PASSWORD, ip);
    expect(sesion.status()).toBe(200);
    const { token } = (await sesion.json()) as { token: string };
    const cambiar = () => request.post(`${BASE}/api/v1/auth/change-password`, {
      headers: { Authorization: `Bearer ${token}`, "X-Forwarded-For": ip },
      data: { currentPassword: WRONG, newPassword: `${WRONG}x` },
    });
    for (let i = 1; i <= 3; i++)
      expect((await cambiar()).status(), `clave actual equivocada ${i}`).toBe(400);
    const bloqueado = await cambiar();
    expect(bloqueado.status()).toBe(429);
    expect(bloqueado.headers()["retry-after"], "Retry-After en el 429 de change-password").toBeTruthy();
  });
});

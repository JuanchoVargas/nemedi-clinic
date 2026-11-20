// El API como aplicación de IIS en /api (clinic.example.com/api) — proyecto "api" de pnpm test.
//
// Sin IIS en la máquina, se emula lo que hace el módulo de ASP.NET Core: arranca el API con
// ASPNETCORE_PORT, ASPNETCORE_APPL_PATH=/api y ASPNETCORE_TOKEN (la integración con IIS se activa
// sola: exige la cabecera MS-ASPNETCORE-TOKEN y entrega PathBase=/api, Path=/v1/…, igual que IIS
// in-process). Comprueba que las rutas públicas siguen siendo /api/v1/… y que funcionan:
//   · health, login y un endpoint autenticado;
//   · las URLs firmadas de archivos salen con /api y se descargan sin JWT;
//   · el branding se resuelve por el Host;
//   · /api/api/… no existe (PathBase no se duplica) y sin el token del módulo → 400.
import { test, expect } from "@playwright/test";
import http from "node:http";
import { startApi, type ApiProcess } from "./api-process";
import { SUPERADMIN } from "./credentials";

const PORT = 5057;
const BASE = `http://127.0.0.1:${PORT}`;
const TOKEN = "token-ficticio-de-iis";
const MODULE = { "MS-ASPNETCORE-TOKEN": TOKEN };
const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };

/** GET con un Host arbitrario (fetch no deja cambiarlo). */
function getWithHost(pathAndQuery: string, host: string): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: "127.0.0.1", port: PORT, path: pathAndQuery, method: "GET", headers: { ...MODULE, Host: host } }, (res) => {
      let body = "";
      res.on("data", (c) => { body += c; });
      res.on("end", () => resolve({ status: res.statusCode ?? 0, body }));
    });
    req.on("error", reject);
    req.end();
  });
}

test.describe.configure({ mode: "serial" });

test.describe("API como aplicación de IIS en /api", () => {
  let api: ApiProcess | undefined;
  let token = "";

  test.beforeAll(async () => {
    api = await startApi(
      { ASPNETCORE_PORT: String(PORT), ASPNETCORE_APPL_PATH: "/api", ASPNETCORE_TOKEN: TOKEN },
      `${BASE}/api/health`, MODULE);
  });

  test.afterAll(() => api?.stop());

  test("las rutas públicas siguen siendo /api/…: health y login", async ({ request }) => {
    const health = await request.get(`${BASE}/api/health`, { headers: MODULE });
    expect(health.status()).toBe(200);
    expect(await health.text()).toContain("healthy");

    const login = await request.post(`${BASE}/api/v1/auth/login`, { headers: MODULE, data: OWNER });
    expect(login.status(), await login.text()).toBe(200);
    token = ((await login.json()) as { token: string }).token;

    const settings = await request.get(`${BASE}/api/v1/tenants/current/settings`, { headers: { ...MODULE, Authorization: `Bearer ${token}` } });
    expect(settings.status()).toBe(200);
  });

  test("PathBase no se duplica y el módulo exige su token", async ({ request }) => {
    expect((await request.get(`${BASE}/api/api/v1/branding`, { headers: MODULE })).status()).toBe(404);
    expect((await request.get(`${BASE}/api/v1/no-existe`, { headers: MODULE })).status()).toBe(404);
    // Sin la cabecera del módulo el API no atiende: prueba de que la emulación está activa
    expect((await request.get(`${BASE}/api/health`)).status()).toBe(400);
  });

  test("las URLs firmadas de archivos salen con /api y se descargan sin JWT", async ({ request }) => {
    const auth = { ...MODULE, Authorization: `Bearer ${token}` };
    const procs = (await (await request.get(`${BASE}/api/v1/procedures?page=1&pageSize=50`, { headers: auth })).json()) as { items: { imagenId?: string | null }[] };
    const imagenId = procs.items.find((p) => p.imagenId)?.imagenId;
    expect(imagenId, "el seed demo pone imagen a los procedimientos").toBeTruthy();

    const signed = (await (await request.get(`${BASE}/api/v1/files/${imagenId}/url`, { headers: auth })).json()) as { url: string; thumbUrl: string };
    expect(signed.url).toMatch(/^\/api\/v1\/files\/[0-9a-f-]+\?t=/);
    const file = await request.get(`${BASE}${signed.url}`, { headers: MODULE });
    expect(file.status()).toBe(200);
    expect(file.headers()["content-type"]).toMatch(/^image\//);
    expect((await request.get(`${BASE}${signed.thumbUrl}`, { headers: MODULE })).status()).toBe(200);
  });

  test("el branding se resuelve por el Host del sitio", async () => {
    const canalDemo = await getWithHost("/api/v1/branding", "canal-demo.example.com");
    expect(canalDemo.status).toBe(200);
    expect(JSON.parse(canalDemo.body).canal).toBe("canal-demo");
    const otro = await getWithHost("/api/v1/branding", "clinic.example.com");
    expect(JSON.parse(otro.body).canal).toBe("nemedi"); // dominio sin canal → Nemedi
  });
});

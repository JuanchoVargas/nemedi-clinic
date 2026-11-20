// Carpeta de archivos sin permiso de escritura (proyecto "api" de pnpm test).
//
// En producción (IIS) la identidad del pool de /api necesita escribir en Storage__UploadsPath; sin ese
// permiso, subir una foto respondía un 500 genérico. Aquí se levanta una SEGUNDA instancia del API
// (puerto 5057) cuya carpeta de archivos es en realidad un archivo: no se puede crear ni escribir, igual
// que sin permisos. Se comprueba que:
//   · /api/health lo reporta (archivos.escribible = false) con el motivo y sin la ruta del servidor;
//   · subir un archivo responde 503 con un mensaje claro (code storage_unavailable), no un 500;
//   · el API arranca igual (lo demás funciona) y el log lo dice al arrancar (CRIT).
// Y contra el API normal (:5055): la carpeta es escribible, subir y borrar funciona.
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { test, expect, type APIRequestContext } from "@playwright/test";
import { startApi, type ApiProcess } from "./api-process";
import { SUPERADMIN } from "./credentials";

const PORT = 5057;
const BROKEN_API = `http://localhost:${PORT}`;
const OWNER = {
  email: SUPERADMIN.email,
  password: SUPERADMIN.password,
};
// PNG de 1×1: lo mínimo que el API acepta como imagen
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");

const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function login(api: APIRequestContext, base = "") {
  const res = await api.post(`${base}/api/v1/auth/login`, { data: OWNER });
  expect(res.status()).toBe(200);
  return ((await res.json()) as { token: string }).token;
}

function upload(api: APIRequestContext, token: string, base = "") {
  return api.post(`${base}/api/v1/files`, {
    ...auth(token),
    multipart: {
      file: { name: "prueba.png", mimeType: "image/png", buffer: PNG },
      entityType: "Patient",
      kind: "Perfil",
    },
  });
}

test.describe("Carpeta de archivos", () => {
  let server: ApiProcess | undefined;
  let tempDir = "";
  let blocked = "";

  test.beforeAll(async () => {
    tempDir = mkdtempSync(path.join(tmpdir(), "nemedi-storage-"));
    blocked = path.join(tempDir, "no-es-carpeta");
    writeFileSync(blocked, "un archivo donde debería haber una carpeta");
    server = await startApi({ Storage__UploadsPath: blocked }, `${BROKEN_API}/api/health`, {}, ["--urls", BROKEN_API]);
  });

  test.afterAll(() => {
    server?.stop();
    if (tempDir) rmSync(tempDir, { recursive: true, force: true });
  });

  test("sin permiso de escritura: /api/health lo dice, sin la ruta del servidor", async ({ request: api }) => {
    const res = await api.get(`${BROKEN_API}/api/health`);
    expect(res.status()).toBe(200); // el API sigue vivo: solo lo que guarda archivos falla
    const health = (await res.json()) as { archivos: { escribible: boolean; error: string | null } };
    expect(health.archivos.escribible).toBe(false);
    expect(health.archivos.error).toBeTruthy();
    expect(health.archivos.error).not.toContain(tempDir);
    expect(server!.log()).toContain("no puede escribir en la carpeta de archivos");
  });

  test("sin permiso de escritura: subir responde 503 con un mensaje claro", async ({ request: api }) => {
    const token = await login(api, BROKEN_API);
    const res = await upload(api, token, BROKEN_API);
    const body = (await res.json()) as { error: string; code: string };
    expect(res.status(), JSON.stringify(body)).toBe(503);
    expect(body.code).toBe("storage_unavailable");
    expect(body.error).toContain("carpeta de archivos no permite escribir");
    expect(body.error).toContain("administrador");
    expect(body.error).not.toContain(tempDir);
  });

  test("con permiso: el health lo confirma y subir y borrar funciona", async ({ request: api }) => {
    const health = (await (await api.get("/api/health")).json()) as { archivos: { escribible: boolean; centinela: string | null } };
    expect(health.archivos.escribible).toBe(true);
    // El centinela: un archivo que existe en la carpeta, para que verify.ps1 lo pida por HTTP y exija 404
    expect(health.archivos.centinela).toBe("nemedi-privado.txt");
    const sentinel = path.resolve("apps/api/src/NemediClinic.Api/App_Data/uploads", health.archivos.centinela!);
    expect(existsSync(sentinel), sentinel).toBe(true);
    expect(readFileSync(sentinel, "utf8")).toContain("no tiene datos");

    const token = await login(api);
    const res = await upload(api, token);
    expect(res.status()).toBe(201);
    const { id } = (await res.json()) as { id: string };
    expect((await api.delete(`/api/v1/files/${id}`, auth(token))).status()).toBe(204);
  });
});

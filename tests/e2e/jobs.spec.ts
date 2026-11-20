// Jobs recurrentes por endpoint (proyecto "api" de pnpm test).
//
//   · POST /api/v1/jobs/{nombre} exige la cabecera X-Jobs-Key (401 sin ella o equivocada; 404 si el
//     job no existe, pero solo después de validar la clave).
//   · Con la clave corre el job y deja la ejecución registrada (JobRuns) con su resultado.
//   · Candado: con una ejecución EnCurso del mismo job, otra responde 409; una EnCurso colgada (más
//     de 30 min, el proceso se reinició) se cierra como Abandonada y no bloquea.
//   · /platform/jobs: el PlatformAdmin ve la última corrida de cada job y puede ejecutarlo; una
//     clínica recibe 403.
// La clave es Jobs__ApiKey, la misma que recibe el API (o E2E_JOBS_KEY); ver credentials.ts.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, lit } from "./sql";
import { SUPERADMIN, PLATFORM, jobsKey } from "./credentials";

const JOBS_KEY = jobsKey();
const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };
const key = (k = JOBS_KEY) => ({ headers: { "X-Jobs-Key": k } });

interface JobRun { id: string; nombre: string; estado: string; resultado: string | null; origen: string; duracionMs: number | null }

async function login(api: APIRequestContext, email: string, password: string) {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email}`).toBe(200);
  return ((await res.json()) as { token: string }).token;
}

/** Corre un job con la clave; si otra corrida (Hangfire en desarrollo) lo tiene tomado, reintenta. */
async function runJob(api: APIRequestContext, nombre: string) {
  for (let intento = 0; ; intento++) {
    const res = await api.post(`/api/v1/jobs/${nombre}`, key());
    if (res.status() !== 409 || intento === 5) return res;
    await new Promise((r) => setTimeout(r, 2000));
  }
}

test.describe.configure({ mode: "serial" });

test.describe("Jobs por endpoint", () => {
  const creados: string[] = [];

  test.afterAll(() => {
    if (creados.length) sql(`DELETE FROM JobRuns WHERE Id IN (${creados.map(lit).join(",")})`);
  });

  test("sin clave o con otra clave → 401; job inexistente → 404", async ({ request: api }) => {
    expect((await api.post("/api/v1/jobs/lotes-alertas")).status()).toBe(401);
    expect((await api.post("/api/v1/jobs/lotes-alertas", key("otra-clave"))).status()).toBe(401);
    // La clave se valida antes que el nombre: sin clave no se sabe qué jobs existen
    expect((await api.post("/api/v1/jobs/no-existe")).status()).toBe(401);
    expect((await api.post("/api/v1/jobs/no-existe", key())).status()).toBe(404);
  });

  test("con la clave corre el job y deja la ejecución registrada", async ({ request: api }) => {
    const res = await runJob(api, "lotes-alertas");
    expect(res.status(), await res.text()).toBe(200);
    const run = (await res.json()) as JobRun;
    creados.push(run.id);
    expect(run).toMatchObject({ nombre: "lotes-alertas", estado: "Exitosa", origen: "Endpoint" });
    expect(run.resultado).toContain("Stock recalculado");
    expect(run.duracionMs).not.toBeNull();

    const fila = sql(`SELECT Estado, Origen FROM JobRuns WHERE Id = ${lit(run.id)}`);
    expect(fila).toEqual([["Exitosa", "Endpoint"]]);

    // Idempotente: otra corrida inmediata tampoco cambia nada que ya estaba bien
    const otra = await runJob(api, "lotes-alertas");
    expect(otra.status()).toBe(200);
    const segunda = (await otra.json()) as JobRun;
    creados.push(segunda.id);
    expect(segunda.resultado).toMatch(/^Stock recalculado en 0 de/);
  });

  test("candado: con una ejecución en curso, otra responde 409", async ({ request: api }) => {
    const id = crypto.randomUUID();
    sql(`INSERT INTO JobRuns (Id, Nombre, Inicio, Estado, Origen) VALUES (${lit(id)}, 'paquetes-vencimiento', GETDATE(), 'EnCurso', 'Test')`);
    creados.push(id);
    const res = await api.post("/api/v1/jobs/paquetes-vencimiento", key());
    expect(res.status()).toBe(409);
    expect(((await res.json()) as { error: string }).error).toContain("ya se está ejecutando");
    sql(`DELETE FROM JobRuns WHERE Id = ${lit(id)}`);
  });

  test("una ejecución colgada (el proceso se reinició) se cierra como Abandonada y no bloquea", async ({ request: api }) => {
    const id = crypto.randomUUID();
    sql(`INSERT INTO JobRuns (Id, Nombre, Inicio, Estado, Origen) VALUES (${lit(id)}, 'paquetes-vencimiento', DATEADD(HOUR, -2, GETDATE()), 'EnCurso', 'Test')`);
    creados.push(id);
    const res = await runJob(api, "paquetes-vencimiento");
    expect(res.status(), await res.text()).toBe(200);
    creados.push(((await res.json()) as JobRun).id);
    expect(sql(`SELECT Estado FROM JobRuns WHERE Id = ${lit(id)}`)).toEqual([["Abandonada"]]);
  });

  test("dos disparos simultáneos no corren a la vez: 200 y, si se cruzan, 409", async ({ request: api }) => {
    const [a, b] = await Promise.all([api.post("/api/v1/jobs/adjuntos-purga", key()), api.post("/api/v1/jobs/adjuntos-purga", key())]);
    const statuses = [a.status(), b.status()].sort();
    expect(statuses.every((s) => s === 200 || s === 409), `statuses ${statuses}`).toBe(true);
    for (const r of [a, b]) if (r.status() === 200) creados.push(((await r.json()) as JobRun).id);
    expect(sql("SELECT COUNT(*) FROM JobRuns WHERE Nombre = 'adjuntos-purga' AND Estado = 'EnCurso'")).toEqual([["0"]]);
  });

  test("/platform/jobs: el PlatformAdmin ve la última corrida y ejecuta; una clínica no entra", async ({ request: api }) => {
    const platform = await login(api, PLATFORM.email, PLATFORM.password);
    const auth = { headers: { Authorization: `Bearer ${platform}` } };
    const res = await api.get("/api/v1/platform/jobs", auth);
    expect(res.status()).toBe(200);
    const overview = (await res.json()) as {
      endpointHabilitado: boolean;
      jobs: { nombre: string; ultimaEjecucion: JobRun | null }[];
      historial: JobRun[];
    };
    expect(overview.endpointHabilitado).toBe(true);
    expect(overview.jobs.map((j) => j.nombre).sort()).toEqual(["adjuntos-purga", "facturas-reintento", "lotes-alertas", "paquetes-vencimiento"]);
    expect(overview.jobs.find((j) => j.nombre === "lotes-alertas")?.ultimaEjecucion?.estado).toBe("Exitosa");
    expect(overview.historial.length).toBeGreaterThan(0);

    const run = await api.post("/api/v1/platform/jobs/lotes-alertas/run", auth);
    expect(run.status(), await run.text()).toBe(200);
    const hecho = (await run.json()) as JobRun;
    creados.push(hecho.id);
    expect(hecho.origen).toBe("Plataforma");

    const owner = await login(api, OWNER.email, OWNER.password);
    expect((await api.get("/api/v1/platform/jobs", { headers: { Authorization: `Bearer ${owner}` } })).status()).toBe(403);
  });
});

// Reloj independiente de la zona del servidor (proyecto "api" de pnpm test).
//
// Levanta una SEGUNDA instancia del API (puerto 5056) que cree estar en US Eastern, como el IIS de
// producción (Clock__SimulateServerTimeZone, solo Development), contra la misma base de desarrollo.
//   · /api/health: servidor en Eastern, clínica en Bogotá, y la hora de la clínica = UTC−5.
//   · Una cita creada a las 09:00 se devuelve a las 09:00, desde esa instancia y desde la normal,
//     y queda 09:00 en la base.
//   · Lo que el sistema estampa con "ahora" (una corrida de job) sale en hora de Bogotá, no de Eastern.
//   · Horario de verano de Eastern: la 01:30 que Eastern repite el 1 de noviembre y la 02:30 que
//     Eastern se salta el 14 de marzo se guardan y se devuelven tal cual; un valor con "Z" u offset
//     entra convertido a Bogotá.
// Las pruebas unitarias (apps/api/tests, ZonedClockTests) cubren el reloj en el instante del cambio.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, lit } from "./sql";
import { startApi, type ApiProcess } from "./api-process";
import { SUPERADMIN, jobsKey } from "./credentials";

const PORT = 5056;
const EASTERN_API = `http://localhost:${PORT}`;
const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };
const JOBS_KEY = jobsKey();
const RUN = Date.now().toString().slice(-8);
const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

const pad = (n: number) => String(n).padStart(2, "0");
/** "yyyy-MM-ddTHH:mm:ss" de un instante visto en un offset fijo de horas. */
const wallClock = (ms: number, offsetHours: number) => {
  const d = new Date(ms + offsetHours * 3_600_000);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`;
};
/** Offset (horas) de US Eastern en este instante: −4 con horario de verano, −5 sin él. */
const easternOffset = (ms: number) => {
  const name = new Intl.DateTimeFormat("en-US", { timeZone: "America/New_York", timeZoneName: "shortOffset" })
    .formatToParts(new Date(ms)).find((p) => p.type === "timeZoneName")!.value; // "GMT-4"
  return Number(name.replace("GMT", "")) || 0;
};
const asMs = (wall: string) => Date.parse(`${wall}Z`); // para restar dos horas de pared

test.describe.configure({ mode: "serial" });

test.describe("Reloj de Bogotá con el servidor en US Eastern", () => {
  let server: ApiProcess | undefined;
  let owner = "";
  let branchId = "";
  let patientId = "";
  let procedureId = "";
  let lauraId = "";
  let camilaId = "";
  const citas: string[] = [];
  const corridas: string[] = [];

  test.beforeAll(async ({ request: api }) => {
    server = await startApi({ Clock__SimulateServerTimeZone: "America/New_York" }, `${EASTERN_API}/api/health`, {}, ["--urls", EASTERN_API]);

    owner = ((await (await api.post("/api/v1/auth/login", { data: OWNER })).json()) as { token: string }).token;
    branchId = ((await (await api.post("/api/v1/branches", { ...auth(owner), data: { nombre: `Sede reloj ${RUN}`, direccion: "Calle del reloj", telefono: "6010000056" } })).json()) as { id: string }).id;
    const pacientes = (await (await api.get("/api/v1/patients?search=1000000001&page=1&pageSize=5", auth(owner))).json()) as { items: { id: string }[] };
    patientId = pacientes.items[0].id;
    const procs = (await (await api.get("/api/v1/procedures?page=1&pageSize=50", auth(owner))).json()) as { items: { id: string; activo: boolean; duracionMinutos: number }[] };
    procedureId = procs.items.find((p) => p.activo && p.duracionMinutos <= 60)!.id;
    const users = (await (await api.get("/api/v1/users?page=1&pageSize=50", auth(owner))).json()) as { items: { id: string; email: string }[] };
    lauraId = users.items.find((u) => u.email === "laura.perez@nemedi.demo")!.id;
    camilaId = users.items.find((u) => u.email === "camila.ruiz@nemedi.demo")!.id;
  });

  test.afterAll(() => {
    server?.stop();
    if (citas.length) sql(`DELETE FROM Appointments WHERE Id IN (${citas.map(lit).join(",")})`);
    if (corridas.length) sql(`DELETE FROM JobRuns WHERE Id IN (${corridas.map(lit).join(",")})`);
    if (branchId) sql(`DELETE FROM BranchSchedules WHERE BranchId = ${lit(branchId)}; DELETE FROM Branches WHERE Id = ${lit(branchId)};`);
  });

  /** Crea la cita en la instancia "en Eastern" y devuelve lo que respondió. */
  async function cita(api: APIRequestContext, fechaInicio: string, esteticistId = lauraId) {
    const res = await api.post(`${EASTERN_API}/api/v1/appointments`, {
      ...auth(owner),
      data: { patientId, esteticistId, procedureId, branchId, fechaInicio, notas: `e2e reloj ${RUN}` },
    });
    expect(res.status(), `cita ${fechaInicio} → ${await res.text()}`).toBe(201);
    const { id } = (await res.json()) as { id: string };
    citas.push(id);
    // El POST responde solo el id: se lee de vuelta de la misma instancia "en Eastern"
    const leida = await api.get(`${EASTERN_API}/api/v1/appointments/${id}`, auth(owner));
    expect(leida.status()).toBe(200);
    return { id, fechaInicio: ((await leida.json()) as { fechaInicio: string }).fechaInicio };
  }

  test("/api/health: el servidor en Eastern, la clínica en Bogotá y la hora de la clínica = UTC−5", async ({ request: api }) => {
    const antes = Date.now();
    const h = (await (await api.get(`${EASTERN_API}/api/health`)).json()) as {
      utc: string; horaServidor: string; zonaServidor: string; horaClinica: string; zonaConfigurada: string; utcOffsetClinica: string;
    };
    expect(h.zonaServidor).toMatch(/Eastern|New_York/);
    expect(h.zonaConfigurada).toMatch(/Bogota|SA Pacific/);
    expect(h.utcOffsetClinica).toBe("-05:00");
    expect(h.horaClinica.endsWith("-05:00")).toBe(true);
    const offset = easternOffset(antes);
    expect(h.horaServidor.endsWith(`-0${Math.abs(offset)}:00`), `servidor ${h.horaServidor}, Eastern hoy es UTC${offset}`).toBe(true);
    // Las tres describen el mismo instante
    const utc = Date.parse(h.utc);
    expect(Math.abs(Date.parse(h.horaClinica) - utc)).toBeLessThan(1000);
    expect(Math.abs(Date.parse(h.horaServidor) - utc)).toBeLessThan(1000);
    expect(Math.abs(utc - antes)).toBeLessThan(60_000);
  });

  test("una cita creada a las 09:00 se devuelve a las 09:00 (y queda 09:00 en la base)", async ({ request: api }) => {
    const creada = await cita(api, "2026-12-10T09:00:00");
    expect(creada.fechaInicio).toBe("2026-12-10T09:00:00");

    const desdeEastern = (await (await api.get(`${EASTERN_API}/api/v1/appointments/${creada.id}`, auth(owner))).json()) as { fechaInicio: string };
    const desdeNormal = (await (await api.get(`/api/v1/appointments/${creada.id}`, auth(owner))).json()) as { fechaInicio: string };
    expect(desdeEastern.fechaInicio).toBe("2026-12-10T09:00:00");
    expect(desdeNormal.fechaInicio).toBe("2026-12-10T09:00:00");
    expect(sql(`SELECT CONVERT(varchar(19), FechaInicio, 126) FROM Appointments WHERE Id = ${lit(creada.id)}`)).toEqual([["2026-12-10T09:00:00"]]);
  });

  test("lo que el sistema estampa con 'ahora' sale en hora de Bogotá, no de Eastern", async ({ request: api }) => {
    const antes = Date.now();
    const res = await api.post(`${EASTERN_API}/api/v1/jobs/lotes-alertas`, { headers: { "X-Jobs-Key": JOBS_KEY } });
    expect(res.status(), await res.text()).toBe(200);
    const run = (await res.json()) as { id: string; inicio: string };
    corridas.push(run.id);

    const bogota = wallClock(antes, -5);
    expect(Math.abs(asMs(run.inicio) - asMs(bogota)), `inicio ${run.inicio}, Bogotá ${bogota}`).toBeLessThan(120_000);
    const offset = easternOffset(antes);
    if (offset !== -5) {
      // Con horario de verano Eastern va una hora adelante: si el sistema usara la hora del servidor, se vería aquí
      const eastern = wallClock(antes, offset);
      expect(Math.abs(asMs(run.inicio) - asMs(eastern))).toBeGreaterThan(50 * 60_000);
    }
  });

  test("horario de verano de Eastern: la hora repetida y la hora inexistente se guardan tal cual", async ({ request: api }) => {
    // 1 de noviembre de 2026: Eastern repite la 01:xx (fin del horario de verano)
    expect((await cita(api, "2026-11-01T01:30:00")).fechaInicio).toBe("2026-11-01T01:30:00");
    // 14 de marzo de 2027: en Eastern no existe la 02:30 (salta de 02:00 a 03:00)
    expect((await cita(api, "2027-03-14T02:30:00")).fechaInicio).toBe("2027-03-14T02:30:00");
  });

  test("un valor con Z u offset entra convertido a la hora de Bogotá, no a la del servidor", async ({ request: api }) => {
    // 15 de octubre (Eastern en horario de verano, UTC−4): 14:00Z = 09:00 en Bogotá (en Eastern serían las 10:00)
    expect((await cita(api, "2026-10-15T14:00:00Z")).fechaInicio).toBe("2026-10-15T09:00:00");
    expect((await cita(api, "2026-10-16T10:00:00-04:00", camilaId)).fechaInicio).toBe("2026-10-16T09:00:00");
  });
});

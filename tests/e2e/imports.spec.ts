// Importador de pacientes desde Excel/CSV (proyecto "api" de pnpm test).
//
//   · Archivo limpio (CSV con ;): mapeo automático, vista previa sin errores, importación, historial.
//   · Encabezado en la fila 3, columnas en desorden y fechas en varios formatos (.xlsx).
//   · Duplicados contra lo existente: omitir y actualizar; repetidos dentro del archivo: error.
//   · Filas con error: no se importan, el Excel de errores trae el motivo.
//   · Vacío → 400, no es Excel → 415, más de 5.000 filas → 400, mapeo inválido → 400.
//   · El tenant sale del JWT: una columna TenantId del archivo no cambia nada.
//   · La esteticista no importa (403). Nada se guarda antes de "execute".
// La clínica demo es IPS: los archivos traen los datos RIPS. Borra por SQL lo que crea.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, scalar, lit } from "./sql";
import { xlsx, XLSX_TYPE, type Cell } from "./xlsx";
import { SUPERADMIN, DEMO_EMAIL, demoPassword } from "./credentials";

const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };
const RUN = Date.now().toString().slice(-7);
const BASE = "/api/v1/imports/patients";
/** Documento único de esta corrida: 6 + RUN + n (10–11 dígitos). */
const doc = (n: number) => `6${RUN}${String(n).padStart(2, "0")}`;
const FILE_TAG = `e2e-import-${RUN}`;

interface Analysis {
  archivoNombre: string;
  formato: string;
  filaEncabezados: number;
  filasDeDatos: number;
  columnas: { index: number; letter: string; header: string; samples: string[] }[];
  campos: { key: string; label: string; required: boolean; group?: string | null }[];
  mapeoSugerido: Record<string, number>;
  formatosDeFecha: string[];
}
interface PreviewRow { fila: number; estado: string; valores: Record<string, string>; errores: string[]; avisos: string[]; existente?: string | null }
interface Preview { total: number; validas: number; conError: number; duplicadas: number; conAvisos: number; filas: PreviewRow[] }
interface Result { id: string; total: number; creadas: number; actualizadas: number; omitidas: number; conError: number }

type Upload = { name: string; mimeType: string; buffer: Buffer };
const csvFile = (name: string, text: string): Upload => ({ name: `${FILE_TAG}-${name}.csv`, mimeType: "text/csv", buffer: Buffer.from(text, "utf8") });
const xlsxFile = (name: string, rows: Cell[][]): Upload => ({ name: `${FILE_TAG}-${name}.xlsx`, mimeType: XLSX_TYPE, buffer: xlsx(rows) });

const auth = (token: string) => ({ Authorization: `Bearer ${token}` });

async function login(api: APIRequestContext, email: string, password: string) {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email}`).toBe(200);
  return ((await res.json()) as { token: string }).token;
}

async function analyze(api: APIRequestContext, token: string, file: Upload, status = 200, filaEncabezados?: number) {
  const res = await api.post(`${BASE}/analyze`, {
    headers: auth(token),
    multipart: { file, ...(filaEncabezados ? { filaEncabezados: String(filaEncabezados) } : {}) },
  });
  const body = await res.text();
  expect(res.status(), `analyze ${file.name} → ${body}`).toBe(status);
  return JSON.parse(body) as Analysis & { error?: string };
}

async function step<T>(api: APIRequestContext, token: string, path: "preview" | "execute" | "errors", file: Upload, a: { filaEncabezados: number; mapeo: Record<string, number> },
  extra: Record<string, string> = {}, status = 200) {
  const res = await api.post(`${BASE}/${path}`, {
    headers: auth(token),
    multipart: { file, filaEncabezados: String(a.filaEncabezados), mapeo: JSON.stringify(a.mapeo), ...extra },
  });
  if (path === "errors" && status === 200) {
    expect(res.status()).toBe(200);
    return (await res.body()) as unknown as T;
  }
  const body = await res.text();
  expect(res.status(), `${path} ${file.name} → ${body}`).toBe(status);
  return JSON.parse(body) as T;
}

const patientCount = (cedulaLike: string) => Number(scalar(`SELECT COUNT(*) FROM Patients WHERE Cedula LIKE ${lit(cedulaLike)} AND IsDeleted = 0`));

// RIPS de la clínica demo (IPS): CC, sexo, nacimiento, Colombia, Bogotá, urbana, particular
const HEADER_CLEAN = "Nombres;Apellidos;Tipo de documento;Cédula;Fecha nac;Sexo;Celular;Correo;Dirección;Ciudad;País;Código DANE;Zona;Régimen";
const cleanRow = (n: number, nombre: string, apellido: string, fecha: string, sexo: string, cel: string) =>
  `${nombre};${apellido};CC;${doc(n).replace(/(\d)(?=(\d{3})+$)/g, "$1.")};${fecha};${sexo};${cel};${nombre.toLowerCase().replace(/\s/g, "")}${RUN}@example.com;Calle ${n} # 10-20;bogotá;170;11001;U;12`;

test.describe("Importador de pacientes", () => {
  test.describe.configure({ mode: "serial" });

  let owner = "";
  let tenantId = "";

  test.beforeAll(async ({ request: api }) => {
    owner = await login(api, OWNER.email, OWNER.password);
    const t = await api.get("/api/v1/tenants/current", { headers: auth(owner) });
    tenantId = ((await t.json()) as { id: string }).id;
  });

  test.afterAll(() => {
    const like = lit(`6${RUN}%`);
    sql(`DELETE FROM ClinicalRecords WHERE PatientId IN (SELECT Id FROM Patients WHERE Cedula LIKE ${like});
         DELETE FROM Patients WHERE Cedula LIKE ${like};
         DELETE FROM ImportRuns WHERE ArchivoNombre LIKE ${lit(`${FILE_TAG}%`)};`);
  });

  test("archivo limpio (CSV con ;): mapeo automático, vista previa e importación", async ({ request: api }) => {
    const file = csvFile("limpio", [
      HEADER_CLEAN,
      cleanRow(1, "MARÍA JOSÉ", "pérez  de la hoz", "15/03/1990", "F", "+57 310 555 1234"),
      cleanRow(2, "carlos andrés", "GÓMEZ", "1985-12-25", "M", "(311) 222-3344"),
      cleanRow(3, "Ana", "Ruiz", "5 de julio de 1978", "F", "3001112233"),
    ].join("\r\n"));

    const a = await analyze(api, owner, file);
    expect(a.formato).toBe("csv");
    expect(a.filaEncabezados).toBe(1);
    expect(a.filasDeDatos).toBe(3);
    const header = (field: string) => a.columnas.find((c) => c.index === a.mapeoSugerido[field])?.header;
    expect(header("documento")).toBe("Cédula");
    expect(header("telefono")).toBe("Celular");
    expect(header("fechaNacimiento")).toBe("Fecha nac");
    expect(header("nombre")).toBe("Nombres");
    expect(header("apellido")).toBe("Apellidos");
    expect(header("email")).toBe("Correo");
    expect(header("tipoUsuario")).toBe("Régimen");
    expect(header("municipioResidencia")).toBe("Código DANE");
    expect(Object.keys(a.mapeoSugerido)).toHaveLength(14);
    expect(a.formatosDeFecha.length).toBeGreaterThanOrEqual(10);

    const p = await step<Preview>(api, owner, "preview", file, { filaEncabezados: a.filaEncabezados, mapeo: a.mapeoSugerido });
    expect(p).toMatchObject({ total: 3, validas: 3, conError: 0, duplicadas: 0 });
    const maria = p.filas[0].valores;
    expect(maria).toMatchObject({ nombre: "María José", apellido: "Pérez de la Hoz", documento: doc(1), telefono: "3105551234", fechaNacimiento: "15/03/1990", sexo: "Mujer", ciudad: "Bogotá" });
    // Vista previa = nada guardado
    expect(patientCount(`6${RUN}%`)).toBe(0);

    const r = await step<Result>(api, owner, "execute", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(r).toMatchObject({ total: 3, creadas: 3, actualizadas: 0, omitidas: 0, conError: 0 });
    const rows = sql(`SELECT Nombre, Apellido, Telefono, CONVERT(varchar(10), FechaNacimiento, 23), Sexo, Direccion, Ciudad, TenantId,
                      (SELECT COUNT(*) FROM ClinicalRecords c WHERE c.PatientId = p.Id) FROM Patients p WHERE Cedula = ${lit(doc(2))}`);
    expect(rows[0]).toEqual(["Carlos Andrés", "Gómez", "3112223344", "1985-12-25", "Hombre", "Calle 2 # 10-20", "Bogotá", tenantId.toUpperCase(), "1"]);

    // Historial: quién, cuándo, archivo y resultado
    const h = await api.get("/api/v1/imports?page=1&pageSize=5", { headers: auth(owner) });
    const run = ((await h.json()) as { items: { archivoNombre: string; creadas: number; usuario: string; entidad: string }[] }).items.find((i) => i.archivoNombre === file.name);
    expect(run).toMatchObject({ creadas: 3, entidad: "patients" });
    expect(run!.usuario.length).toBeGreaterThan(0);
  });

  test("encabezado en la fila 3, columnas en desorden y fechas en varios formatos (.xlsx)", async ({ request: api }) => {
    const file = xlsxFile("desorden", [
      ["Clínica Demo — base de pacientes"],
      ["Exportado el 30/09/2026", null, null, "Hoja 1"],
      ["Régimen", "Zona", "Código DANE", "País", "Género", "Teléfono celular", "F. nacimiento", "Identificación", "Tipo doc", "Primer apellido", "Primer nombre", "Observaciones"],
      ["12", "Urbana", 11001, "Colombia", "Femenino", 3104445566, "25-12-1985", doc(10), "C.C.", "LÓPEZ", "laura", "alérgica al látex"],
      ["12", "U", "11001", "170", "Masculino", "3104445567", 32947, doc(11), "CC", "Mora", "Luis", ""],
      ["Particular", "Rural", "05001", "170", "F", "3104445568", "15 mar 1990", doc(12), "cedula", "Díaz", "Sara", ""],
      ["12", "U", "11001", "170", "F", "3104445569", "19900315", doc(13), "CC", "Vega", "Eva", ""],
      ["12", "U", "11001", "170", "F", "3104445570", "15.03.90", doc(14), "CC", "Paz", "Lina", ""],
    ]);
    const a = await analyze(api, owner, file);
    expect(a.formato).toBe("xlsx");
    expect(a.filaEncabezados).toBe(3);
    expect(a.filasDeDatos).toBe(5);
    const header = (field: string) => a.columnas.find((c) => c.index === a.mapeoSugerido[field])?.header;
    expect(header("documento")).toBe("Identificación");
    expect(header("nombre")).toBe("Primer nombre");
    expect(header("apellido")).toBe("Primer apellido");
    expect(header("fechaNacimiento")).toBe("F. nacimiento");
    expect(header("telefono")).toBe("Teléfono celular");
    expect(header("sexo")).toBe("Género");
    expect(header("tipoDocumento")).toBe("Tipo doc");
    expect(a.mapeoSugerido).not.toHaveProperty("observaciones");

    const p = await step<Preview>(api, owner, "preview", file, { filaEncabezados: a.filaEncabezados, mapeo: a.mapeoSugerido });
    expect(p.conError, JSON.stringify(p.filas.filter((f) => f.errores.length))).toBe(0);
    expect(p.filas.map((f) => f.fila)).toEqual([4, 5, 6, 7, 8]);
    expect(p.filas.map((f) => f.valores.fechaNacimiento)).toEqual(["25/12/1985", "15/03/1990", "15/03/1990", "15/03/1990", "15/03/1990"]);
    expect(p.filas[0].valores).toMatchObject({ nombre: "Laura", apellido: "López", telefono: "3104445566", municipioResidencia: "11001", tipoUsuario: "Particular" });
    expect(p.filas[2].valores.municipioResidencia).toBe("05001");
    expect(p.filas[4].avisos.join(" ")).toContain("dos dígitos");

    // La persona corrige la fila de títulos: con otra fila el mapeo cambia (y vuelve con la correcta)
    const wrong = await analyze(api, owner, file, 200, 1);
    expect(wrong.filaEncabezados).toBe(1);
    expect(wrong.mapeoSugerido).not.toHaveProperty("documento");

    const r = await step<Result>(api, owner, "execute", file, { filaEncabezados: 3, mapeo: a.mapeoSugerido });
    expect(r.creadas).toBe(5);
  });

  test("duplicados: omitir deja como está, actualizar cambia solo lo que trae el archivo", async ({ request: api }) => {
    // doc(1) y doc(2) existen (primer test); doc(20) es nuevo
    const file = csvFile("duplicados", [
      "Cédula,Nombre,Apellido,Celular,Fecha de nacimiento,Sexo,Tipo de documento,País,Municipio de residencia,Zona,Régimen",
      `${doc(1)},María José,Pérez de la Hoz,3209998877,,F,CC,170,11001,U,12`,
      `${doc(2)},Carlos Andrés,Gómez,3209998866,,M,CC,170,11001,U,12`,
      `${doc(20)},Nueva,Paciente,3209998855,01/02/2000,F,CC,170,11001,U,12`,
    ].join("\n"));
    const a = await analyze(api, owner, file);
    const p = await step<Preview>(api, owner, "preview", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(p).toMatchObject({ total: 3, validas: 1, duplicadas: 2, conError: 0 });
    expect(p.filas[0].existente).toBe("María José Pérez de la Hoz");

    const omit = await step<Result>(api, owner, "execute", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido }, { duplicados: "Omitir" });
    expect(omit).toMatchObject({ creadas: 1, actualizadas: 0, omitidas: 2 });
    expect(scalar(`SELECT Telefono FROM Patients WHERE Cedula = ${lit(doc(1))}`)).toBe("3105551234");

    // Ahora doc(20) también existe: todo es duplicado
    const upd = await step<Result>(api, owner, "execute", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido }, { duplicados: "Actualizar" });
    expect(upd).toMatchObject({ creadas: 0, actualizadas: 3, omitidas: 0 });
    // El teléfono cambió; la fecha vacía en el archivo no borró la que tenía
    expect(sql(`SELECT Telefono, CONVERT(varchar(10), FechaNacimiento, 23) FROM Patients WHERE Cedula = ${lit(doc(1))}`)[0]).toEqual(["3209998877", "1990-03-15"]);
    expect(patientCount(`6${RUN}%`)).toBe(3 + 5 + 1);
  });

  test("filas con error: no se importan y el Excel de errores trae el motivo", async ({ request: api }) => {
    const file = csvFile("errores", [
      "Documento;Nombre completo;Fecha de nacimiento;Correo;Sexo;Tipo documento;País;DANE;Zona;Régimen",
      `${doc(30)};Valentina Ríos Mejía;03/04/1995;vale@example.com;F;CC;170;11001;U;12`,
      `${doc(31)};Pedro;10/10/1990;pedro@example.com;M;CC;170;11001;U;12`,
      `${doc(32)};Sofía Castro;15/03/2031;sofia@example.com;F;CC;170;11001;U;12`,
      `${doc(33)};Julián Torres;31/02/1990;no-es-correo;M;CC;170;11001;U;12`,
      `${doc(30)};Valentina Repetida;03/04/1995;v2@example.com;F;CC;170;11001;U;12`,
      `AB123;Mario Lema;01/01/1980;;M;CC;170;11001;U;12`,
      `${doc(34)};Clara Ortiz;01/01/1980;;F;CC;170;11001;U;`,
    ].join("\n"));
    const a = await analyze(api, owner, file);
    expect(a.mapeoSugerido).toHaveProperty("nombreCompleto");
    const p = await step<Preview>(api, owner, "preview", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(p).toMatchObject({ total: 7, validas: 1, conError: 6 });
    const errores = (fila: number) => p.filas.find((f) => f.fila === fila)!.errores.join(" | ");
    expect(p.filas[0].valores).toMatchObject({ nombre: "Valentina", apellido: "Ríos Mejía" });
    // 03/04 no es ambigua: la columna trae 15/03 y 31/02, así que se lee día/mes sin aviso
    expect(p.filas[0].valores.fechaNacimiento).toBe("03/04/1995");
    expect(p.filas[0].avisos.join(" ")).not.toContain("ambigua");
    expect(p.filas[0].avisos.join(" ")).toContain("Nombre completo");
    expect(errores(3)).toContain("una sola palabra");
    expect(errores(4)).toContain("futuro");
    expect(errores(5)).toContain("inexistente");
    expect(errores(5)).toContain("correo");
    expect(errores(6)).toContain("se repite en el archivo");
    expect(errores(7)).toContain("solo dígitos");
    expect(errores(8)).toContain("RIPS");

    // soloProblemas: la muestra trae solo las filas con algo que revisar
    const solo = await step<Preview>(api, owner, "preview", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido }, { soloProblemas: "true" });
    expect(solo.filas.every((f) => f.estado !== "Valida" || f.avisos.length > 0)).toBe(true);
    expect(solo.total).toBe(7);

    const excel = await step<Buffer>(api, owner, "errors", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(excel.subarray(0, 2).toString()).toBe("PK");
    // El Excel de errores se puede volver a subir: el importador lo lee y trae la columna del motivo
    const back = await analyze(api, owner, { name: `${FILE_TAG}-errores-de-vuelta.xlsx`, mimeType: XLSX_TYPE, buffer: excel });
    expect(back.filasDeDatos).toBe(6);
    expect(back.columnas.map((c) => c.header)).toContain("Motivo del error");

    const r = await step<Result>(api, owner, "execute", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(r).toMatchObject({ creadas: 1, conError: 6 });
    expect(patientCount(doc(31))).toBe(0);
  });

  test("el tenant sale del JWT: una columna TenantId del archivo no cambia nada", async ({ request: api }) => {
    const otro = "11111111-2222-3333-4444-555555555555";
    const file = csvFile("tenant", [
      "TenantId;Cédula;Nombre;Apellido;Sexo;Fecha nacimiento;Tipo documento;País;DANE;Zona;Régimen",
      `${otro};${doc(40)};Tenant;Ajeno;F;01/01/1990;CC;170;11001;U;12`,
    ].join("\n"));
    const a = await analyze(api, owner, file);
    expect(a.columnas[0].header).toBe("TenantId");
    expect(Object.values(a.mapeoSugerido)).not.toContain(0);
    // Ni forzándolo: "tenantId" no es un campo del importador
    await step(api, owner, "preview", file, { filaEncabezados: 1, mapeo: { ...a.mapeoSugerido, tenantId: 0 } }, {}, 400);
    await step<Result>(api, owner, "execute", file, { filaEncabezados: 1, mapeo: a.mapeoSugerido });
    expect(scalar(`SELECT TenantId FROM Patients WHERE Cedula = ${lit(doc(40))}`)).toBe(tenantId.toUpperCase());
  });

  test("archivos inválidos y mapeos inválidos", async ({ request: api }) => {
    expect((await analyze(api, owner, csvFile("vacio", ""), 400)).error).toContain("vacío");
    // Solo títulos, sin datos
    expect((await analyze(api, owner, csvFile("solo-titulos", "Cédula;Nombre;Apellido\n"), 400)).error).toContain("no tiene filas");
    const png: Upload = { name: `${FILE_TAG}-foto.xlsx`, mimeType: XLSX_TYPE, buffer: Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13]) };
    expect((await analyze(api, owner, png, 415)).error).toContain("no es un Excel");
    const pdf: Upload = { name: `${FILE_TAG}-doc.csv`, mimeType: "text/csv", buffer: Buffer.from("%PDF-1.7\n1 0 obj") };
    await analyze(api, owner, pdf, 415);

    const big = ["Cédula;Nombre;Apellido", ...Array.from({ length: 5001 }, (_, i) => `${7000000 + i};A;B`)].join("\n");
    expect((await analyze(api, owner, csvFile("grande", big), 400)).error).toContain("5.000");

    const ok = csvFile("mapeo", `Cédula;Nombre;Apellido\n${doc(50)};A;B`);
    await step(api, owner, "preview", ok, { filaEncabezados: 1, mapeo: { documento: 0, nombre: 0, apellido: 2 } }, {}, 400); // columna repetida
    await step(api, owner, "preview", ok, { filaEncabezados: 1, mapeo: { documento: 9 } }, {}, 400); // columna inexistente
    await step(api, owner, "preview", ok, { filaEncabezados: 7, mapeo: { documento: 0 } }, {}, 400); // fila inexistente
    const unknown = await api.post("/api/v1/imports/facturas/analyze", { headers: auth(owner), multipart: { file: ok } });
    expect(unknown.status()).toBe(404);
    expect(patientCount(doc(50))).toBe(0);
  });

  test("la esteticista no importa ni ve el historial", async ({ request: api }) => {
    const esteticista = await login(api, DEMO_EMAIL.laura, demoPassword());
    const file = csvFile("esteticista", `Cédula;Nombre;Apellido\n${doc(60)};A;B`);
    expect((await api.post(`${BASE}/analyze`, { headers: auth(esteticista), multipart: { file } })).status()).toBe(403);
    expect((await api.post(`${BASE}/execute`, { headers: auth(esteticista), multipart: { file, filaEncabezados: "1", mapeo: '{"documento":0,"nombre":1,"apellido":2}' } })).status()).toBe(403);
    expect((await api.get("/api/v1/imports", { headers: auth(esteticista) })).status()).toBe(403);
    expect(patientCount(doc(60))).toBe(0);
  });
});

// Importador de catálogo: productos (con lotes), procedimientos y paquetes (proyecto "api" de pnpm test).
//
// Para cada entidad: archivo limpio, columnas en desorden, duplicados (omitir / actualizar), obligatorios
// vacíos, y su caso propio: varias filas del mismo producto = un producto con varios lotes; un paquete con
// un procedimiento que no existe = error en esa fila (y el paquete no se crea a medias).
// Todo lleva la marca E2E-<RUN> y se borra por SQL al final.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, scalar, lit } from "./sql";
import { xlsx, XLSX_TYPE, type Cell } from "./xlsx";
import { SUPERADMIN } from "./credentials";

const OWNER = { email: SUPERADMIN.email, password: SUPERADMIN.password };
const RUN = Date.now().toString().slice(-7);
const TAG = `E2E-${RUN}`;
const FILE_TAG = `e2e-catalogo-${RUN}`;

interface Analysis { filaEncabezados: number; columnas: { index: number; header: string }[]; mapeoSugerido: Record<string, number> }
interface Row { fila: number; estado: string; valores: Record<string, string>; errores: string[]; avisos: string[]; existente?: string | null }
interface Preview { total: number; validas: number; conError: number; duplicadas: number; nuevos: number; existentes: number; detalle?: string | null; avisosGenerales: string[]; filas: Row[] }
interface Result { creadas: number; actualizadas: number; omitidas: number; conError: number; detalle?: string | null }
type Upload = { name: string; mimeType: string; buffer: Buffer };

const csv = (name: string, lines: string[]): Upload => ({ name: `${FILE_TAG}-${name}.csv`, mimeType: "text/csv", buffer: Buffer.from(lines.join("\r\n"), "utf8") });
const book = (name: string, rows: Cell[][]): Upload => ({ name: `${FILE_TAG}-${name}.xlsx`, mimeType: XLSX_TYPE, buffer: xlsx(rows) });
const auth = (token: string) => ({ Authorization: `Bearer ${token}` });

let owner = "";

async function analyze(api: APIRequestContext, entity: string, file: Upload) {
  const res = await api.post(`/api/v1/imports/${entity}/analyze`, { headers: auth(owner), multipart: { file } });
  const body = await res.text();
  expect(res.status(), `analyze ${file.name} → ${body}`).toBe(200);
  return JSON.parse(body) as Analysis;
}

async function step<T>(api: APIRequestContext, entity: string, path: "preview" | "execute", file: Upload, a: Analysis, duplicados = "Omitir") {
  const res = await api.post(`/api/v1/imports/${entity}/${path}`, {
    headers: auth(owner),
    multipart: { file, filaEncabezados: String(a.filaEncabezados), mapeo: JSON.stringify(a.mapeoSugerido), duplicados, soloProblemas: "false" },
  });
  const body = await res.text();
  expect(res.status(), `${path} ${file.name} → ${body}`).toBe(200);
  return JSON.parse(body) as T;
}

/** Primera fila de una consulta, con los decimales sin ceros de relleno ("15.0000" → "15"). */
const first = (query: string) => sql(query)[0].map((v) => (/^-?\d*\.\d+$/.test(v) ? String(Number(v)) : v));
const one = (query: string) => first(query)[0];

const header = (a: Analysis, field: string) => a.columnas.find((c) => c.index === a.mapeoSugerido[field])?.header;
const row = (p: Preview, fila: number) => p.filas.find((r) => r.fila === fila)!;

test.describe("Importador de catálogo", () => {
  test.describe.configure({ mode: "serial" });

  test.beforeAll(async ({ request: api }) => {
    const res = await api.post("/api/v1/auth/login", { data: OWNER });
    owner = ((await res.json()) as { token: string }).token;
  });

  test.afterAll(() => {
    const like = lit(`%${TAG}%`);
    sql(`DELETE FROM InventoryMovements WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
         DELETE FROM InventoryEntries WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
         DELETE FROM ProductLots WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
         DELETE FROM Products WHERE Nombre LIKE ${like};
         DELETE FROM PackageProcedures WHERE PackageId IN (SELECT Id FROM Packages WHERE Nombre LIKE ${like});
         DELETE FROM Packages WHERE Nombre LIKE ${like};
         DELETE FROM Procedures WHERE Nombre LIKE ${like};
         DELETE FROM ImportRuns WHERE ArchivoNombre LIKE ${lit(`${FILE_TAG}%`)};`);
  });

  // ── Productos ──────────────────────────────────────────────

  const SKU = (n: number) => `E2E${RUN}-${n}`;
  const products = book("productos", [
    ["Inventario de bodega"],
    ["Ref", "Producto", "Valor", "IVA", "Clasificación", "Registro INVIMA", "Lote", "Cant", "Vence", "Costo", "Categoría"],
    [SKU(1), `Toxina A ${TAG}`, "$ 850.000", "19%", "Medicamento", "INVIMA 2020M-0001", "L-100", 10, "03/2028", 500000, `Inyectables ${TAG}`],
    [SKU(1), `Toxina A ${TAG}`, "", "", "", "", "L-101", 5, "15/09/2027", "520.000", ""],
    [SKU(2), `Gasas ${TAG}`, "", "", "Insumo", "", "", 200, "", "", `insumos ${TAG}`],
    ["", `Sérum ${TAG}`, "120.000", "exento", "Cosmético", "", "", "", "", "", `INSUMOS ${TAG}`],
    [SKU(4), `Ácido hialurónico ${TAG}`, "650000", "19", "Dispositivo médico", "INVIMA 2021DM-0002", "", 3, "", "", ""],
  ]);

  test("productos: archivo limpio con lotes agrupados", async ({ request: api }) => {
    const a = await analyze(api, "products", products);
    expect(a.filaEncabezados).toBe(2);
    expect(header(a, "referencia")).toBe("Ref");
    expect(header(a, "precioVenta")).toBe("Valor");
    expect(header(a, "cantidad")).toBe("Cant");
    expect(header(a, "vencimiento")).toBe("Vence");
    expect(header(a, "numeroLote")).toBe("Lote");
    expect(header(a, "costo")).toBe("Costo");
    expect(header(a, "categoria")).toBe("Categoría");

    const p = await step<Preview>(api, "products", "preview", products, a);
    expect(p).toMatchObject({ total: 5, validas: 5, conError: 0, nuevos: 4, existentes: 0 });
    expect(p.detalle).toContain("3 lotes con 215 unidades");
    expect(row(p, 3).valores).toMatchObject({ precioVenta: "850.000", iva: "19", tipoRegulatorio: "Medicamento", vencimiento: "31/03/2028" });
    // Dispositivo médico sin lote ni vencimiento: se crea sin existencias, con aviso
    expect(row(p, 7).avisos.join(" ")).toContain("necesita número de lote y vencimiento");

    const r = await step<Result>(api, "products", "execute", products, a);
    expect(r).toMatchObject({ creadas: 4, actualizadas: 0, conError: 0 });
    expect(r.detalle).toBe("3 lotes con 215 unidades");
    // Toxina: un producto, dos lotes, stock = 15, registro copiado al lote
    expect(first(`SELECT p.StockActual, COUNT(l.Id), MIN(l.RegistroSanitario) FROM Products p JOIN ProductLots l ON l.ProductId = p.Id
                WHERE p.Referencia = ${lit(SKU(1))} GROUP BY p.StockActual`)).toEqual(["15", "2", "INVIMA 2020M-0001"]);
    expect(scalar(`SELECT COUNT(*) FROM Products WHERE Nombre = ${lit(`Toxina A ${TAG}`)}`)).toBe("1");
    expect(scalar(`SELECT COUNT(*) FROM InventoryEntries e JOIN Products p ON p.Id = e.ProductId WHERE p.Referencia = ${lit(SKU(1))}`)).toBe("2");
    // Gasas: lote sin número; Sérum sin cantidad: sin lotes ni stock; venta / insumo según el precio
    expect(first(`SELECT StockActual, TipoProducto, UnidadMedida FROM Products WHERE Referencia = ${lit(SKU(2))}`)).toEqual(["200", "InsumoCabina", "unidad"]);
    expect(first(`SELECT StockActual, IvaPorcentaje, TipoProducto, (SELECT COUNT(*) FROM ProductLots l WHERE l.ProductId = p.Id)
                FROM Products p WHERE Nombre = ${lit(`Sérum ${TAG}`)}`)).toEqual(["0", "0", "Venta", "0"]);
    expect(one(`SELECT StockActual FROM Products WHERE Referencia = ${lit(SKU(4))}`)).toBe("0");
    expect(scalar(`SELECT Detalle FROM ImportRuns WHERE ArchivoNombre = ${lit(products.name)}`)).toBe("3 lotes con 215 unidades");
    // Costo en cada lote; categoría en el producto, sin variantes ("insumos" e "INSUMOS" son una)
    expect(sql(`SELECT l.NumeroLote, l.CostoUnitario FROM ProductLots l JOIN Products p ON p.Id = l.ProductId
                WHERE p.Referencia = ${lit(SKU(1))} ORDER BY l.NumeroLote`).map((r) => [r[0], String(Number(r[1]))]))
      .toEqual([["L-100", "500000"], ["L-101", "520000"]]);
    expect(sql(`SELECT DISTINCT Categoria FROM Products WHERE Nombre LIKE ${lit(`%${TAG}%`)} AND Categoria IS NOT NULL ORDER BY Categoria`).map((r) => r[0]))
      .toEqual([`Insumos ${TAG}`, `Inyectables ${TAG}`]);
    // El producto muestra el costo del lote más reciente que lo tiene
    const toxina = await api.get(`/api/v1/products?Search=${SKU(1)}&Page=1&PageSize=5`, { headers: auth(owner) });
    const item = ((await toxina.json()) as { items: { ultimoCosto: number | null; categoria: string | null }[] }).items[0];
    expect(item.categoria).toBe(`Inyectables ${TAG}`);
    expect([500000, 520000]).toContain(item.ultimoCosto); // mismo ingreso: cualquiera de los dos es "el último"
  });

  test("productos: columnas en desorden y sin cantidades", async ({ request: api }) => {
    const file = csv("productos-desorden", [
      "Vencimiento;Proveedor;Precio de venta;Referencia;Nombre del producto;Tipo regulatorio",
      `;Dermo SAS;45.000;${SKU(10)};Crema ${TAG};Cosmético`,
    ]);
    const a = await analyze(api, "products", file);
    expect(header(a, "referencia")).toBe("Referencia");
    expect(header(a, "nombre")).toBe("Nombre del producto");
    expect(header(a, "precioVenta")).toBe("Precio de venta");
    const p = await step<Preview>(api, "products", "preview", file, a);
    expect(p.avisosGenerales.join(" ")).toContain("no trae cantidades");
    expect(p.nuevos).toBe(1);
  });

  test("productos: duplicados por referencia (omitir y actualizar sin sumar dos veces un lote)", async ({ request: api }) => {
    const again = book("productos-otra-vez", [
      ["Ref", "Producto", "Valor", "Lote", "Cant", "Vence"],
      [SKU(1), `Toxina A ${TAG}`, "900.000", "L-100", 10, "03/2028"], // lote que ya tiene: no se suma
      [SKU(1), `Toxina A ${TAG}`, "", "L-102", 4, "12/2028"],       // lote nuevo
      ["", `sérum ${TAG}`, "130.000", "", "", ""],                   // por nombre, sin tildes ni mayúsculas
    ]);
    const a = await analyze(api, "products", again);
    const p = await step<Preview>(api, "products", "preview", again, a);
    expect(p).toMatchObject({ duplicadas: 3, existentes: 2, nuevos: 0 });
    expect(row(p, 2).avisos.join(" ")).toContain("ya tiene el lote L-100");

    const omit = await step<Result>(api, "products", "execute", again, a, "Omitir");
    expect(omit).toMatchObject({ creadas: 0, actualizadas: 0, omitidas: 2 });
    expect(scalar(`SELECT COUNT(*) FROM ProductLots l JOIN Products p ON p.Id = l.ProductId WHERE p.Referencia = ${lit(SKU(1))}`)).toBe("2");

    const upd = await step<Result>(api, "products", "execute", again, a, "Actualizar");
    expect(upd).toMatchObject({ creadas: 0, actualizadas: 2, omitidas: 0, detalle: "1 lote con 4 unidades" });
    expect(first(`SELECT PrecioVenta, StockActual FROM Products WHERE Referencia = ${lit(SKU(1))}`)).toEqual(["900000", "19"]);
    expect(one(`SELECT PrecioVenta FROM Products WHERE Nombre = ${lit(`Sérum ${TAG}`)}`)).toBe("130000");
  });

  test("productos: obligatorios vacíos y datos inválidos", async ({ request: api }) => {
    const file = csv("productos-errores", [
      "Referencia,Nombre,IVA,Lote,Cantidad,Vencimiento",
      ",,19,,5,",                                   // ni nombre ni referencia
      `${SKU(20)},,19,,5,`,                         // nuevo con referencia sola
      `${SKU(21)},Jeringa ${TAG},16,,5,`,           // IVA que no existe
      `${SKU(22)},Guantes ${TAG},19,L-9,,31/12/2027`, // lote sin cantidad
      `${SKU(23)},Alcohol ${TAG},19,L-1,5,`,
      `${SKU(23)},Alcohol ${TAG},19,L-1,8,`,        // mismo lote repetido
    ]);
    const a = await analyze(api, "products", file);
    const p = await step<Preview>(api, "products", "preview", file, a);
    expect(row(p, 2).errores.join(" ")).toContain("Falta el nombre o la referencia");
    expect(row(p, 3).errores.join(" ")).toContain("Falta el nombre del producto");
    expect(row(p, 4).errores.join(" ")).toContain("0, 5 o 19");
    expect(row(p, 5).errores.join(" ")).toContain("no trae cantidad");
    expect(row(p, 7).errores.join(" ")).toContain("se repite");
    expect(row(p, 6).estado).toBe("Valida");
    const r = await step<Result>(api, "products", "execute", file, a);
    expect(r).toMatchObject({ creadas: 1, conError: 5 });
  });

  // ── Procedimientos ─────────────────────────────────────────

  test("procedimientos: archivo limpio con alias colombianos", async ({ request: api }) => {
    const file = csv("procedimientos", [
      "Servicio;Dura;Valor;IVA;Zona;Descripción",
      `Limpieza facial ${TAG};1 h;$ 150.000;0;Rostro;Limpieza profunda`,
      `Peeling ${TAG};45 min;180.000;;Rostro;`,
      `Drenaje ${TAG};1:30;95000;19%;Cuerpo;`,
    ]);
    const a = await analyze(api, "procedures", file);
    expect(header(a, "nombre")).toBe("Servicio");
    expect(header(a, "duracion")).toBe("Dura");
    expect(header(a, "precio")).toBe("Valor");
    const p = await step<Preview>(api, "procedures", "preview", file, a);
    expect(p).toMatchObject({ validas: 3, conError: 0, nuevos: 3 });
    const r = await step<Result>(api, "procedures", "execute", file, a);
    expect(r.creadas).toBe(3);
    expect(first(`SELECT DuracionMinutos, PrecioBase, IvaPorcentaje, AreaCorporal FROM Procedures WHERE Nombre = ${lit(`Drenaje ${TAG}`)}`))
      .toEqual(["90", "95000", "19", "Cuerpo"]);
    expect(scalar(`SELECT ISNULL(CONVERT(varchar, IvaPorcentaje), 'clinica') FROM Procedures WHERE Nombre = ${lit(`Peeling ${TAG}`)}`)).toBe("clinica");
  });

  test("procedimientos: columnas en desorden, duplicados y obligatorios", async ({ request: api }) => {
    const file = book("procedimientos-2", [
      ["Tiempo", "Precio de venta", "Procedimiento"],
      [60, 160000, `limpieza FACIAL ${TAG}`],       // existe (sin importar mayúsculas)
      [30, 70000, `Mesoterapia ${TAG}`],
      [30, 70000, `MESOTERAPIA ${TAG}`],             // repetido en el archivo
      ["", 50000, `Sin duración ${TAG}`],
      [40, "", `Sin precio ${TAG}`],
      ["9 h", 50000, `Muy largo ${TAG}`],
    ]);
    const a = await analyze(api, "procedures", file);
    expect(header(a, "duracion")).toBe("Tiempo");
    expect(header(a, "precio")).toBe("Precio de venta");
    expect(header(a, "nombre")).toBe("Procedimiento");
    const p = await step<Preview>(api, "procedures", "preview", file, a);
    expect(row(p, 2)).toMatchObject({ estado: "Duplicada", existente: `Limpieza facial ${TAG}` });
    expect(row(p, 4).errores.join(" ")).toContain("se repite");
    expect(row(p, 5).errores.join(" ")).toContain("Duración (minutos) está vacío");
    expect(row(p, 6).errores.join(" ")).toContain("Precio está vacío");
    expect(row(p, 7).errores.join(" ")).toContain("máximo");
    const r = await step<Result>(api, "procedures", "execute", file, a, "Actualizar");
    expect(r).toMatchObject({ creadas: 1, actualizadas: 1, conError: 4 });
    expect(one(`SELECT PrecioBase FROM Procedures WHERE Nombre = ${lit(`Limpieza facial ${TAG}`)}`)).toBe("160000");
  });

  test("procedimientos: RIPS en una IPS", async ({ request: api }) => {
    const file = csv("procedimientos-rips", [
      "Nombre,Duración,Precio,CUPS,Finalidad,Tipo de servicio",
      `Radiofrecuencia ${TAG},60,200000,869610,16,Procedimiento`,
      `Láser ${TAG},60,200000,12,16,2`,
      `Hidrofacial ${TAG},60,200000,869611,,`,
    ]);
    const a = await analyze(api, "procedures", file);
    expect(header(a, "codigoCups")).toBe("CUPS");
    const p = await step<Preview>(api, "procedures", "preview", file, a);
    expect(row(p, 2).estado).toBe("Valida");
    expect(row(p, 3).errores.join(" ")).toContain("CUPS");
    expect(row(p, 4).errores.join(" ")).toContain("finalidad");
    await step<Result>(api, "procedures", "execute", file, a);
    expect(first(`SELECT RequiereRips, CodigoCups, FinalidadConsulta, TipoServicio FROM Procedures WHERE Nombre = ${lit(`Radiofrecuencia ${TAG}`)}`))
      .toEqual(["1", "869610", "Tratamiento", "Procedimiento"]);
  });

  // ── Paquetes ───────────────────────────────────────────────

  test("paquetes: archivo limpio, varias filas por paquete", async ({ request: api }) => {
    const file = csv("paquetes", [
      "Paquete;Valor;Vigencia;Procedimiento;Sesiones;Descripción",
      `Piel luminosa ${TAG};$ 900.000;6 meses;Limpieza facial ${TAG};4;Rutina completa`,
      `Piel luminosa ${TAG};;;Peeling ${TAG};2;`,
      `Cuerpo firme ${TAG};1.200.000;180;Drenaje ${TAG};8 sesiones;`,
    ]);
    const a = await analyze(api, "packages", file);
    expect(header(a, "paquete")).toBe("Paquete");
    expect(header(a, "precio")).toBe("Valor");
    const p = await step<Preview>(api, "packages", "preview", file, a);
    expect(p).toMatchObject({ validas: 3, conError: 0, nuevos: 2, detalle: "14 sesiones en 2 paquetes nuevos." });
    const r = await step<Result>(api, "packages", "execute", file, a);
    expect(r).toMatchObject({ creadas: 2, detalle: "14 sesiones en total" });
    expect(first(`SELECT PrecioTotal, VigenciaDias, SesionesTotales, DiasAlertaVencimiento,
                (SELECT COUNT(*) FROM PackageProcedures pp WHERE pp.PackageId = p.Id AND pp.IsDeleted = 0)
                FROM Packages p WHERE Nombre = ${lit(`Piel luminosa ${TAG}`)}`)).toEqual(["900000", "180", "6", "15", "2"]);
  });

  test("paquetes: procedimiento inexistente y obligatorios vacíos", async ({ request: api }) => {
    const file = csv("paquetes-errores", [
      "Procedimiento,Sesiones,Paquete,Precio,Vigencia", // columnas en desorden
      `Limpieza facial ${TAG},2,Express ${TAG},300000,90`,
      `Criolipólisis ${TAG},3,Express ${TAG},,`,
      `Peeling ${TAG},,Sin sesiones ${TAG},100000,30`,
      `Peeling ${TAG},2,Sin vigencia ${TAG},100000,`,
      `Drenaje ${TAG},4,Bien ${TAG},400000,120`,
    ]);
    const a = await analyze(api, "packages", file);
    expect(header(a, "procedimiento")).toBe("Procedimiento");
    expect(header(a, "paquete")).toBe("Paquete");
    const p = await step<Preview>(api, "packages", "preview", file, a);
    expect(row(p, 3).errores.join(" ")).toContain(`No existe el procedimiento «Criolipólisis ${TAG}»`);
    // El paquete no se crea a medias: la fila buena del mismo paquete también queda con error
    expect(row(p, 2).errores.join(" ")).toContain("no se importa porque la fila 3 tiene error");
    expect(row(p, 4).errores.join(" ")).toContain("Sesiones está vacío");
    expect(row(p, 5).errores.join(" ")).toContain("Falta la vigencia");
    expect(row(p, 6).estado).toBe("Valida");
    const r = await step<Result>(api, "packages", "execute", file, a);
    expect(r).toMatchObject({ creadas: 1, conError: 4 });
    expect(scalar(`SELECT COUNT(*) FROM Packages WHERE Nombre = ${lit(`Express ${TAG}`)}`)).toBe("0");
  });

  test("paquetes: duplicados por nombre (actualizar reemplaza las sesiones si no se ha vendido)", async ({ request: api }) => {
    const file = csv("paquetes-otra-vez", [
      "Paquete;Precio;Vigencia;Procedimiento;Sesiones",
      `PIEL LUMINOSA ${TAG};950.000;;Limpieza facial ${TAG};6`,
    ]);
    const a = await analyze(api, "packages", file);
    const p = await step<Preview>(api, "packages", "preview", file, a);
    expect(p).toMatchObject({ duplicadas: 1, existentes: 1, nuevos: 0 });
    const omit = await step<Result>(api, "packages", "execute", file, a, "Omitir");
    expect(omit.omitidas).toBe(1);
    const upd = await step<Result>(api, "packages", "execute", file, a, "Actualizar");
    expect(upd.actualizadas).toBe(1);
    expect(first(`SELECT PrecioTotal, VigenciaDias, SesionesTotales, (SELECT COUNT(*) FROM PackageProcedures pp WHERE pp.PackageId = p.Id AND pp.IsDeleted = 0)
                FROM Packages p WHERE Nombre = ${lit(`Piel luminosa ${TAG}`)}`)).toEqual(["950000", "180", "6", "1"]);
  });

  test("el historial dice de qué entidad fue cada importación", async ({ request: api }) => {
    const res = await api.get("/api/v1/imports?page=1&pageSize=50", { headers: auth(owner) });
    const items = ((await res.json()) as { items: { archivoNombre: string; entidad: string; entidadNombre: string }[] }).items
      .filter((i) => i.archivoNombre.startsWith(FILE_TAG));
    const byFile = Object.fromEntries(items.map((i) => [i.archivoNombre.replace(`${FILE_TAG}-`, ""), i.entidadNombre]));
    expect(byFile["productos.xlsx"]).toBe("Productos");
    expect(byFile["procedimientos.csv"]).toBe("Procedimientos");
    expect(byFile["paquetes.csv"]).toBe("Paquetes");
  });
});

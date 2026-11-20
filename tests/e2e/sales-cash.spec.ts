// Reglas de ventas y caja (proyecto "api" de pnpm test). Todo contra el tenant demo, con un cajero
// y un producto propios del test que se borran al final (las ventas no se borran por API: se anulan;
// la limpieza física va por SQL porque esto corre sobre la base de desarrollo que se usa en demos).
//
//   · IVA: productos 19 % y servicios 0 % por defecto; un ítem con IVA propio manda. La web no calcula:
//     pide una cotización.
//   · Confirmar descuenta por FEFO (un movimiento por lote, ligado a la venta), asigna el paquete al
//     paciente y reparte el pago: lo que cae en el paquete se ve en la ficha del paciente.
//   · Los pagos nunca superan el total ni el saldo (422).
//   · Anular devuelve el stock al mismo lote, retira el paquete y anula los pagos; es idempotente.
//     Con sesiones realizadas del paquete no se puede (409).
//   · Caja: una abierta por usuario; egreso tope el efectivo; el cierre congela la diferencia; el
//     efectivo de una venta cobrada en una caja ya cerrada vuelve como egreso de la caja abierta.
//   · La esteticista no entra a ventas ni a caja (403). Emitir factura sin proveedor → 501.
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, lit } from "./sql";
import { RIPS_PACIENTE } from "./rips";
import { SUPERADMIN, DEMO_EMAIL, demoPassword, tempPassword } from "./credentials";

const ADMIN_EMAIL = SUPERADMIN.email;
const ADMIN_PASSWORD = SUPERADMIN.password;
const RUN = Date.now().toString().slice(-8);
const CAJERO_EMAIL = `cajero.e2e.${RUN}@nemedi.test`;
const CAJERO_PASSWORD = tempPassword();
const CAJERO_TEMPORAL = tempPassword();

const pad = (n: number) => String(n).padStart(2, "0");
const enDias = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};
const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function login(api: APIRequestContext, email: string, password: string) {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email}`).toBe(200);
  return (await res.json()) as { token: string; userInfo: { id: string } };
}

async function call<T>(api: APIRequestContext, method: "get" | "post" | "put", token: string, url: string, data?: unknown, status = 200): Promise<T> {
  const res = await api[method](url, { ...auth(token), data });
  const body = await res.text();
  expect(res.status(), `${method.toUpperCase()} ${url} → ${body}`).toBe(status);
  return (body ? JSON.parse(body) : null) as T;
}

interface Linea { tipo: string; descripcion: string; subtotal: number; iva: number; total: number; ivaPorcentaje: number; patientPackageId: string | null }
interface Venta {
  id: string; numero: number; estado: string; subtotal: number; impuestos: number; total: number;
  totalPagado: number; saldo: number; estadoPago: string; lineas: Linea[];
  pagos: { monto: number; metodoPago: string; patientPackageId: string | null; anulado: boolean }[];
}
interface Caja {
  id: string; estado: string; diferencia: number | null;
  resumen: { montoApertura: number; totalEfectivo: number; totalTarjeta: number; totalEgresos: number; efectivoEsperado: number };
  movimientos: { tipo: string; concepto: string; monto: number; anulado: boolean }[];
}
interface Lote { id: string; numeroLote: string | null; cantidadDisponible: number }

test.describe("Ventas y caja", () => {
  // Un solo flujo: la caja que abre un test la usan los siguientes. Si uno falla, el resto no corre
  // (Playwright reiniciaría el worker con otro cajero sin caja y los fallos serían en cascada).
  test.describe.configure({ mode: "serial" });

  let owner = "";
  let token = "";
  let cajeroId = "";
  let patientId = "";
  let productId = "";
  let procedureId = "";
  let packageId = "";
  let esteticistaToken = "";

  test.beforeAll(async ({ request: api }) => {
    owner = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;

    // Cajero propio: el SuperAdmin del demo puede tener su caja abierta por el seed
    const branches = await call<{ items: { id: string }[] }>(api, "get", owner, "/api/v1/branches");
    const reg = await call<{ id: string }>(api, "post", owner, "/api/v1/auth/register", {
      nombre: "Cajero", apellido: `E2E ${RUN}`, email: CAJERO_EMAIL, password: CAJERO_TEMPORAL, rol: "Admin", branchId: branches.items[0].id,
    }, 201);
    cajeroId = reg.id;
    const temp = await login(api, CAJERO_EMAIL, CAJERO_TEMPORAL);
    token = (await call<{ token: string }>(api, "post", temp.token, "/api/v1/auth/change-password",
      { currentPassword: CAJERO_TEMPORAL, newPassword: CAJERO_PASSWORD })).token;

    patientId = (await call<{ id: string }>(api, "post", owner, "/api/v1/patients", {
      nombre: "Ventas", apellido: `E2E ${RUN}`, cedula: `83${RUN}`, telefono: "3000000083", ...RIPS_PACIENTE,
    }, 201)).id;

    // Producto de venta con dos lotes: el que vence antes debe salir primero (FEFO)
    productId = (await call<{ id: string }>(api, "post", owner, "/api/v1/products", {
      nombre: `Protector solar E2E ${RUN}`, tipoProducto: "Venta", unidadMedida: "unidad", stockMinimo: 1,
      tipoRegulatorio: "Cosmetico", precioVenta: 50_000,
    }, 201)).id;
    await call(api, "post", owner, "/api/v1/inventory/entries",
      { productId, cantidad: 3, motivoEntrada: "Compra", lote: { numeroLote: `TARDE-${RUN}`, fechaVencimiento: enDias(300) } }, 201);
    await call(api, "post", owner, "/api/v1/inventory/entries",
      { productId, cantidad: 2, motivoEntrada: "Compra", lote: { numeroLote: `PRONTO-${RUN}`, fechaVencimiento: enDias(120) } }, 201);

    const procedures = await call<{ items: { id: string; activo: boolean; ivaPorcentaje: number | null }[] }>(
      api, "get", owner, "/api/v1/procedures?page=1&pageSize=50");
    procedureId = procedures.items.find((p) => p.activo && p.ivaPorcentaje === null)!.id;

    packageId = (await call<{ id: string }>(api, "post", owner, "/api/v1/packages", {
      nombre: `Paquete venta E2E ${RUN}`, descripcion: "e2e", precioTotal: 300_000, sesionesTotales: 2, vigenciaDias: 60, diasAlertaVencimiento: 10,
    }, 201)).id;
    await call(api, "post", owner, `/api/v1/packages/${packageId}/procedures`, { procedureId, cantidadSesiones: 2 }, 201);

    esteticistaToken = (await login(api, DEMO_EMAIL.laura, demoPassword())).token;
  });

  test.afterAll(async () => {
    // Base de desarrollo: se borra en físico lo que creó el test (ventas, pagos, cajas, lotes y el cajero)
    if (!cajeroId) return;
    const ventas = `SELECT Id FROM Sales WHERE UsuarioId = ${lit(cajeroId)}`;
    const cajas = `SELECT Id FROM CashSessions WHERE UsuarioId = ${lit(cajeroId)}`;
    sql([
      `DELETE FROM Payments WHERE SaleId IN (${ventas}) OR CashSessionId IN (${cajas}) OR RegistradoPorId = ${lit(cajeroId)};`,
      `DELETE FROM PatientPackageSessions WHERE PatientPackageId IN (SELECT Id FROM PatientPackages WHERE SaleId IN (${ventas}));`,
      `DELETE FROM PatientPackages WHERE SaleId IN (${ventas}) OR PatientId = ${lit(patientId)};`,
      `DELETE FROM InventoryMovements WHERE SaleId IN (${ventas}) OR ProductId = ${lit(productId)};`,
      `DELETE FROM InventoryEntries WHERE ProductId = ${lit(productId)};`,
      `DELETE FROM ProductLots WHERE ProductId = ${lit(productId)};`,
      `DELETE FROM Products WHERE Id = ${lit(productId)};`,
      `DELETE FROM SaleLines WHERE SaleId IN (${ventas});`,
      `DELETE FROM Sales WHERE UsuarioId = ${lit(cajeroId)};`,
      `DELETE FROM CashMovements WHERE CashSessionId IN (${cajas});`,
      `DELETE FROM CashSessions WHERE UsuarioId = ${lit(cajeroId)};`,
      `DELETE FROM PackageProcedures WHERE PackageId = ${lit(packageId)};`,
      `DELETE FROM Packages WHERE Id = ${lit(packageId)};`,
      `DELETE FROM ClinicalRecords WHERE PatientId = ${lit(patientId)};`,
      `DELETE FROM Patients WHERE Id = ${lit(patientId)};`,
      `DELETE FROM Users WHERE Id = ${lit(cajeroId)};`,
    ].join(" "));
  });

  test("la cotización aplica el IVA de la clínica: 19 % en productos, 0 % en servicios", async ({ request: api }) => {
    const q = await call<{ lineas: Linea[]; subtotal: number; impuestos: number; total: number }>(api, "post", token, "/api/v1/sales/quote", {
      lineas: [
        { tipo: "Producto", referenciaId: productId, cantidad: 2, descuento: 10_000 },
        { tipo: "Procedimiento", referenciaId: procedureId, cantidad: 1 },
        { tipo: "Otro", descripcion: "Kit de cuidado", cantidad: 1, precioUnitario: 20_000, ivaPorcentaje: 5 },
      ],
    });
    const [producto, servicio, otro] = q.lineas;
    // 2 × 50.000 − 10.000 = 90.000 de base; 19 % = 17.100
    expect(producto).toMatchObject({ ivaPorcentaje: 19, subtotal: 90_000, iva: 17_100, total: 107_100 });
    expect(servicio.ivaPorcentaje).toBe(0);
    expect(servicio.iva).toBe(0);
    expect(otro).toMatchObject({ ivaPorcentaje: 5, iva: 1_000, total: 21_000 });
    expect(q.impuestos).toBe(18_100);
    expect(q.total).toBe(q.subtotal + q.impuestos);
  });

  test("un ítem con IVA propio manda sobre el de la clínica", async ({ request: api }) => {
    await call(api, "put", owner, `/api/v1/products/${productId}`, { ivaPorcentaje: 5 }, 204);
    const q = await call<{ lineas: Linea[] }>(api, "post", token, "/api/v1/sales/quote",
      { lineas: [{ tipo: "Producto", referenciaId: productId, cantidad: 1 }] });
    expect(q.lineas[0]).toMatchObject({ ivaPorcentaje: 5, iva: 2_500 });
    await call(api, "put", owner, `/api/v1/products/${productId}`, { ivaPorDefecto: true }, 204);
  });

  test("la esteticista no vende ni ve la caja (403)", async ({ request: api }) => {
    expect((await api.get("/api/v1/sales", auth(esteticistaToken))).status()).toBe(403);
    expect((await api.get("/api/v1/cash/current", auth(esteticistaToken))).status()).toBe(403);
  });

  test("caja: una abierta por usuario, el egreso no pasa del efectivo", async ({ request: api }) => {
    const res = await api.get("/api/v1/cash/current", auth(token));
    expect(res.status(), "el cajero nuevo no tiene caja").toBe(204);

    const caja = await call<Caja>(api, "post", token, "/api/v1/cash/open", { montoApertura: 100_000 });
    expect(caja).toMatchObject({ estado: "Abierta", resumen: { montoApertura: 100_000, efectivoEsperado: 100_000 } });

    const otra = await api.post("/api/v1/cash/open", { ...auth(token), data: { montoApertura: 0 } });
    expect(otra.status()).toBe(409);

    const egreso = await api.post(`/api/v1/cash/sessions/${caja.id}/movements`,
      { ...auth(token), data: { tipo: "Egreso", concepto: "Demasiado", monto: 100_001 } });
    expect(egreso.status()).toBe(422);
  });

  test("venta de producto: FEFO por lote, dos medios de pago y todo en la caja", async ({ request: api }) => {
    const antes = await call<Lote[]>(api, "get", token, `/api/v1/products/${productId}/lots`);
    const pronto = antes.find((l) => l.numeroLote?.startsWith("PRONTO"))!;
    const tarde = antes.find((l) => l.numeroLote?.startsWith("TARDE"))!;

    // 3 unidades × 50.000 × 1,19 = 178.500
    const venta = await call<Venta>(api, "post", token, "/api/v1/sales", {
      patientId,
      lineas: [{ tipo: "Producto", referenciaId: productId, cantidad: 3 }],
      pagos: [{ monto: 100_000, metodoPago: "Efectivo" }, { monto: 78_500, metodoPago: "Tarjeta", referencia: "VOUCHER-1" }],
      confirmar: true,
    }, 201);
    expect(venta).toMatchObject({ estado: "Confirmada", total: 178_500, totalPagado: 178_500, saldo: 0, estadoPago: "Pagado" });

    // FEFO: primero se agota el lote que vence antes (2) y la unidad restante sale del otro
    const despues = await call<Lote[]>(api, "get", token, `/api/v1/products/${productId}/lots`);
    expect(despues.find((l) => l.id === pronto.id)!.cantidadDisponible).toBe(pronto.cantidadDisponible - 2);
    expect(despues.find((l) => l.id === tarde.id)!.cantidadDisponible).toBe(tarde.cantidadDisponible - 1);

    const movs = await call<{ tipoMovimiento: string; cantidad: number; numeroLote: string | null; patientId: string | null }[]>(
      api, "get", token, `/api/v1/inventory/movements/product/${productId}`);
    const salidas = movs.filter((m) => m.tipoMovimiento === "Salida");
    expect(salidas, "un movimiento por lote").toHaveLength(2);
    expect(salidas.every((m) => m.patientId === patientId)).toBe(true);

    const caja = await call<Caja>(api, "get", token, "/api/v1/cash/current");
    expect(caja.resumen.totalEfectivo).toBe(100_000);
    expect(caja.resumen.totalTarjeta).toBe(78_500);
    expect(caja.resumen.efectivoEsperado, "base + efectivo").toBe(200_000);
    expect(caja.movimientos.filter((m) => m.tipo === "Pago" && m.concepto.includes(`#${venta.numero}`))).toHaveLength(2);
  });

  test("los pagos no superan el total ni el saldo (422)", async ({ request: api }) => {
    const mucho = await api.post("/api/v1/sales", {
      ...auth(token),
      data: { lineas: [{ tipo: "Procedimiento", referenciaId: procedureId, cantidad: 1 }], pagos: [{ monto: 99_999_999, metodoPago: "Efectivo" }] },
    });
    expect(mucho.status()).toBe(422);

    // Venta de mostrador a crédito: queda con saldo y el abono no puede pasarse
    const venta = await call<Venta>(api, "post", token, "/api/v1/sales",
      { clienteNombre: "Cliente mostrador E2E", lineas: [{ tipo: "Procedimiento", referenciaId: procedureId, cantidad: 1 }], pagos: [] }, 201);
    expect(venta.estadoPago).toBe("SinPagos");
    expect(venta.saldo).toBe(venta.total);
    const abono = await api.post(`/api/v1/sales/${venta.id}/payments`,
      { ...auth(token), data: { monto: venta.total + 1, metodoPago: "Efectivo" } });
    expect(abono.status()).toBe(422);
    expect((await abono.json()).error).toContain("saldo pendiente");
  });

  test("paquete vendido: queda en la ficha y el abono se ve en la venta y en el paquete", async ({ request: api }) => {
    const sinPaciente = await api.post("/api/v1/sales", {
      ...auth(token), data: { lineas: [{ tipo: "Paquete", referenciaId: packageId, cantidad: 1 }] },
    });
    expect(sinPaciente.status(), "un paquete exige paciente").toBe(400);

    const venta = await call<Venta>(api, "post", token, "/api/v1/sales", {
      patientId,
      lineas: [{ tipo: "Paquete", referenciaId: packageId, cantidad: 1 }],
      pagos: [{ monto: 100_000, metodoPago: "Transferencia", referencia: "TRX-1" }],
    }, 201);
    const ppId = venta.lineas[0].patientPackageId!;
    expect(ppId, "confirmar asigna el paquete").toBeTruthy();
    expect(venta.pagos[0].patientPackageId, "el pago se aplica al paquete").toBe(ppId);

    const paquete = await call<{ saleId: string; saleNumero: number; totalPagado: number; porcentajePagado: number }>(
      api, "get", owner, `/api/v1/patient-packages/${ppId}`);
    expect(paquete).toMatchObject({ saleId: venta.id, saleNumero: venta.numero, totalPagado: 100_000, porcentajePagado: 33 });

    // Abono desde la ficha del paciente: baja también el saldo de la venta
    await call(api, "post", token, `/api/v1/patient-packages/${ppId}/payments`,
      { monto: 50_000, fechaPago: enDias(0), metodoPago: "Efectivo" }, 201);
    const actualizada = await call<Venta>(api, "get", token, `/api/v1/sales/${venta.id}`);
    expect(actualizada.totalPagado).toBe(150_000);
    expect(actualizada.saldo).toBe(150_000);

    // Retirarlo desde la ficha no se puede: se anula la venta
    const borrar = await api.delete(`/api/v1/patient-packages/${ppId}`, auth(owner));
    expect(borrar.status()).toBe(409);

    // Con una sesión realizada, tampoco se puede anular la venta
    const detalle = await call<{ sesiones: { id: string }[] }>(api, "get", owner, `/api/v1/patient-packages/${ppId}`);
    await call(api, "put", owner, `/api/v1/patient-packages/${ppId}/sessions/${detalle.sesiones[0].id}/complete`);
    const anular = await api.post(`/api/v1/sales/${venta.id}/void`, { ...auth(token), data: { motivo: "Prueba" } });
    expect(anular.status()).toBe(409);
    expect((await anular.json()).error).toContain("sesión");
  });

  test("anular revierte stock al mismo lote, retira el paquete y anula los pagos; dos veces no revierte dos veces", async ({ request: api }) => {
    const lotesAntes = await call<Lote[]>(api, "get", token, `/api/v1/products/${productId}/lots`);
    const venta = await call<Venta>(api, "post", token, "/api/v1/sales", {
      patientId,
      lineas: [
        { tipo: "Producto", referenciaId: productId, cantidad: 1 },
        { tipo: "Paquete", referenciaId: packageId, cantidad: 1 },
      ],
      pagos: [{ monto: 59_500, metodoPago: "Efectivo" }],
    }, 201);
    const ppId = venta.lineas[1].patientPackageId!;
    const cajaAntes = await call<Caja>(api, "get", token, "/api/v1/cash/current");

    const sinMotivo = await api.post(`/api/v1/sales/${venta.id}/void`, { ...auth(token), data: { motivo: "" } });
    expect(sinMotivo.status()).toBe(400);

    const r1 = await call<{ yaEstabaAnulada: boolean; paquetesRevertidos: number; movimientosRevertidos: number; pagosAnulados: number; venta: Venta }>(
      api, "post", token, `/api/v1/sales/${venta.id}/void`, { motivo: "Cliente desistió" });
    expect(r1).toMatchObject({ yaEstabaAnulada: false, paquetesRevertidos: 1, movimientosRevertidos: 1, pagosAnulados: 1 });
    expect(r1.venta.estado).toBe("Anulada");
    expect(r1.venta.pagos.every((p) => p.anulado)).toBe(true);

    const lotesDespues = await call<Lote[]>(api, "get", token, `/api/v1/products/${productId}/lots`);
    for (const lote of lotesAntes)
      expect(lotesDespues.find((l) => l.id === lote.id)!.cantidadDisponible, `lote ${lote.numeroLote}`).toBe(lote.cantidadDisponible);

    expect((await api.get(`/api/v1/patient-packages/${ppId}`, auth(owner))).status(), "el paquete se retiró").toBe(404);

    // Cobrado en una caja todavía abierta: el pago deja de sumar (el efectivo sale del cajón)
    const cajaDespues = await call<Caja>(api, "get", token, "/api/v1/cash/current");
    expect(cajaDespues.resumen.totalEfectivo).toBe(cajaAntes.resumen.totalEfectivo - 59_500);
    expect(cajaDespues.movimientos.some((m) => m.anulado)).toBe(true);

    // Idempotente
    const r2 = await call<{ yaEstabaAnulada: boolean; venta: Venta }>(api, "post", token, `/api/v1/sales/${venta.id}/void`, { motivo: "Otra vez" });
    expect(r2.yaEstabaAnulada).toBe(true);
    const lotesOtraVez = await call<Lote[]>(api, "get", token, `/api/v1/products/${productId}/lots`);
    expect(lotesOtraVez.map((l) => l.cantidadDisponible)).toEqual(lotesDespues.map((l) => l.cantidadDisponible));
  });

  test("cierre con diferencia; anular una venta de una caja cerrada devuelve el efectivo desde la caja abierta", async ({ request: api }) => {
    const venta = await call<Venta>(api, "post", token, "/api/v1/sales",
      { lineas: [{ tipo: "Otro", descripcion: "Bono de regalo", cantidad: 1, precioUnitario: 40_000 }], pagos: [{ monto: 40_000, metodoPago: "Efectivo" }] }, 201);

    const caja = await call<Caja>(api, "get", token, "/api/v1/cash/current");
    const esperado = caja.resumen.efectivoEsperado;
    const cerrada = await call<Caja>(api, "post", token, `/api/v1/cash/sessions/${caja.id}/close`,
      { efectivoContado: esperado - 2_000, observacion: "Faltan 2.000" });
    expect(cerrada).toMatchObject({ estado: "Cerrada", diferencia: -2_000 });

    // Cerrada no se mueve
    const mov = await api.post(`/api/v1/cash/sessions/${caja.id}/movements`,
      { ...auth(token), data: { tipo: "Ingreso", concepto: "Tarde", monto: 1 } });
    expect(mov.status()).toBe(409);

    const nueva = await call<Caja>(api, "post", token, "/api/v1/cash/open", { montoApertura: 50_000 });
    await call(api, "post", token, `/api/v1/sales/${venta.id}/void`, { motivo: "Devolución" });

    const conDevolucion = await call<Caja>(api, "get", token, `/api/v1/cash/sessions/${nueva.id}`);
    expect(conDevolucion.resumen.totalEgresos).toBe(40_000);
    expect(conDevolucion.resumen.efectivoEsperado).toBe(10_000);
    expect(conDevolucion.movimientos.some((m) => m.tipo === "Egreso" && m.concepto.includes(`#${venta.numero}`))).toBe(true);

    // La caja cerrada no cambió
    const historica = await call<Caja>(api, "get", token, `/api/v1/cash/sessions/${caja.id}`);
    expect(historica.resumen.efectivoEsperado).toBe(esperado);

    const excel = await api.get(`/api/v1/cash/sessions/${caja.id}/export`, auth(token));
    expect(excel.status()).toBe(200);
    expect(excel.headers()["content-type"]).toContain("spreadsheetml");
  });

  test("facturación electrónica: sin proveedor configurado responde 501", async ({ request: api }) => {
    const ventas = await call<{ items: Venta[] }>(api, "get", token, "/api/v1/sales?estado=Confirmada&pageSize=1");
    const res = await api.post(`/api/v1/sales/${ventas.items[0].id}/invoice`, auth(token));
    expect(res.status()).toBe(501);
  });
});

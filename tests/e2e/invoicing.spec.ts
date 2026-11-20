// Facturación electrónica contra el SANDBOX de Factus (proyecto "factus": pnpm test:factus). No es
// parte de pnpm test: depende de un servicio externo y de credenciales que no están en el repo.
//
// Credenciales del sandbox: variables de entorno Factus__ClientId, Factus__ClientSecret,
// Factus__Username y Factus__Password. Se leen aquí y NUNCA se imprimen: van solo en el cuerpo de
// PUT /invoices/settings, que las guarda cifradas. Sin ellas el archivo entero se salta.
//
// Ciclo: configuración y prueba de conexión → emisión desde una venta (con saldo a crédito) → consulta
// (detalle, listado, Excel, PDF y XML) → rechazo (rango de numeración inexistente) y reintento →
// nota crédito parcial → anular una venta facturada (nota de anulación automática) → factura de un
// abono de paquete y su nota al borrarlo → reenvío por correo → job facturas-reintento.
// Todo lo que crea se borra en físico al final, incluida la configuración de facturación (el gate de
// ventas espera 501 en una clínica sin configurar).
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql, scalar, lit } from "./sql";
import { RIPS_PACIENTE } from "./rips";
import { SUPERADMIN, jobsKey } from "./credentials";

const ADMIN_EMAIL = SUPERADMIN.email;
const ADMIN_PASSWORD = SUPERADMIN.password;
const JOBS_KEY = jobsKey();
const RUN = Date.now().toString().slice(-8);
const CORREO = `facturas.e2e.${RUN}@example.com`;

function factusCredentials() {
  const env = process.env;
  const c = { clientId: env.Factus__ClientId, clientSecret: env.Factus__ClientSecret, usuario: env.Factus__Username, contrasena: env.Factus__Password };
  return Object.values(c).every(Boolean) ? c : null;
}
const CRED = factusCredentials();

const pad = (n: number) => String(n).padStart(2, "0");
const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};
const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function call<T>(api: APIRequestContext, method: "get" | "post" | "put" | "delete", token: string, url: string, data?: unknown, status = 200): Promise<T> {
  const res = await api[method](url, { ...auth(token), data });
  const body = await res.text();
  // El cuerpo puede traer el mensaje del proveedor, nunca credenciales (el API no las devuelve)
  expect(res.status(), `${method.toUpperCase()} ${url} → ${body.slice(0, 1500)}`).toBe(status);
  return (body ? JSON.parse(body) : null) as T;
}

interface Linea { indice: number; descripcion: string; cantidad: number; cantidadDevuelta: number }
interface Factura {
  id: string; tipoDocumento: string; estado: string; referencia: string; numero: string | null; cufe: string | null;
  saleId: string | null; paymentId: string | null; patientId: string | null; total: number; mensaje: string | null;
  pdfId: string | null; xmlId: string | null; facturaOrigenId: string | null; conceptoCorreccion: string | null;
  totalNotasCredito: number; correoEnviadoA: string | null; lineas: Linea[];
}
interface Rango { id: number; documento: string; prefijo: string; vigente: boolean }
interface Venta { id: string; numero: number; total: number; estado: string; facturaEstado: string; facturaNumero: string | null; facturaPdfId: string | null }

test.describe("Facturación electrónica (sandbox de Factus)", () => {
  test.describe.configure({ mode: "serial" });
  test.skip(!CRED, "Sin credenciales Factus__* en el entorno");

  let token = "";
  let tenantId = "";
  let branchId = "";
  let patientId = "";
  let procedureId = "";
  let rangoFactura = 0;
  let rangoNota = 0;
  let venta1: Venta;
  let factura1: Factura;
  const ventas: string[] = [];

  const settingsBody = (rangoFacturaId: number, extra: Record<string, unknown> = {}) => ({
    habilitada: true,
    ambiente: "Pruebas",
    emisorNit: process.env.E2E_FACTUS_EMISOR_NIT ?? "999000001", // ficticio; si el sandbox exige el NIT de su cuenta, pásalo en E2E_FACTUS_EMISOR_NIT
    emisorRazonSocial: "Clínica demo (sandbox Factus)",
    emisorTipoOrganizacion: "1",
    emisorResponsabilidad: "R-99-PN",
    emisorMunicipio: "11001",
    unidadMedida: "94",
    codigoEstandar: "999",
    ivaCeroExcluido: true,
    enviarCorreoAlEmitir: false,
    sedes: [{ branchId, rangoFacturaId, rangoNotaCreditoId: rangoNota }],
    ...extra,
  });

  async function nuevaVenta(api: APIRequestContext, pagado: number | null): Promise<Venta> {
    const lineas = [
      { tipo: "Otro", descripcion: "Kit de cuidado posprocedimiento", cantidad: 2, precioUnitario: 45_000, descuento: 5_000, ivaPorcentaje: 19 },
      { tipo: "Procedimiento", referenciaId: procedureId, cantidad: 1 },
    ];
    const quote = await call<{ total: number }>(api, "post", token, "/api/v1/sales/quote", { lineas });
    const venta = await call<Venta>(api, "post", token, "/api/v1/sales", {
      patientId, lineas, confirmar: true,
      pagos: [{ monto: pagado ?? quote.total, metodoPago: "Tarjeta", referencia: `E2E-${RUN}` }],
    }, 201);
    ventas.push(venta.id);
    return venta;
  }

  test.beforeAll(async ({ request: api }) => {
    const res = await api.post("/api/v1/auth/login", { data: { email: ADMIN_EMAIL, password: ADMIN_PASSWORD } });
    expect(res.status()).toBe(200);
    const login = (await res.json()) as { token: string; userInfo: { tenantId: string } };
    token = login.token;
    tenantId = login.userInfo.tenantId;
    expect(scalar(`SELECT COUNT(*) FROM InvoicingSettings WHERE TenantId = ${lit(tenantId)}`),
      "la clínica demo no debe tener facturación configurada (el test la crea y la borra)").toBe("0");

    branchId = (await call<{ items: { id: string }[] }>(api, "get", token, "/api/v1/branches")).items[0].id;
    patientId = (await call<{ id: string }>(api, "post", token, "/api/v1/patients", {
      nombre: "Facturación", apellido: `E2E ${RUN}`, cedula: `84${RUN}`, telefono: "3000000084", email: CORREO, ...RIPS_PACIENTE,
    }, 201)).id;
    const procedures = await call<{ items: { id: string; activo: boolean; ivaPorcentaje: number | null }[] }>(
      api, "get", token, "/api/v1/procedures?page=1&pageSize=50");
    procedureId = procedures.items.find((p) => p.activo && p.ivaPorcentaje === null)!.id;
  });

  test.afterAll(async () => {
    if (!tenantId) return;
    const facturas = `SELECT Id FROM ElectronicInvoices WHERE TenantId = ${lit(tenantId)} AND (PatientId = ${lit(patientId)} OR SaleId IN (${ventas.length ? ventas.map(lit).join(",") : "NULL"}))`;
    const ventasSql = `SELECT Id FROM Sales WHERE PatientId = ${lit(patientId)}`;
    sql([
      `DELETE FROM Attachments WHERE Id IN (SELECT PdfId FROM ElectronicInvoices WHERE Id IN (${facturas})) OR Id IN (SELECT XmlId FROM ElectronicInvoices WHERE Id IN (${facturas}));`,
      `UPDATE ElectronicInvoices SET FacturaOrigenId = NULL WHERE Id IN (${facturas});`,
      `DELETE FROM ElectronicInvoices WHERE Id IN (${facturas});`,
      `DELETE FROM InvoicingBranchSettings WHERE TenantId = ${lit(tenantId)};`,
      `DELETE FROM InvoicingSettings WHERE TenantId = ${lit(tenantId)};`,
      `DELETE FROM Payments WHERE SaleId IN (${ventasSql}) OR PatientId = ${lit(patientId)} OR PatientPackageId IN (SELECT Id FROM PatientPackages WHERE PatientId = ${lit(patientId)});`,
      `DELETE FROM PatientPackageSessions WHERE PatientPackageId IN (SELECT Id FROM PatientPackages WHERE PatientId = ${lit(patientId)});`,
      `DELETE FROM PatientPackages WHERE PatientId = ${lit(patientId)};`,
      `DELETE FROM SaleLines WHERE SaleId IN (${ventasSql});`,
      `DELETE FROM Sales WHERE PatientId = ${lit(patientId)};`,
      `DELETE FROM ClinicalRecords WHERE PatientId = ${lit(patientId)};`,
      `DELETE FROM Patients WHERE Id = ${lit(patientId)};`,
    ].join(" "));
  });

  test("sin configurar: emitir responde 501", async ({ request: api }) => {
    const venta = await nuevaVenta(api, null);
    const res = await api.post("/api/v1/invoices", { ...auth(token), data: { saleId: venta.id } });
    expect(res.status()).toBe(501);
  });

  test("probar conexión: credenciales malas fallan, las buenas traen emisor y rangos", async ({ request: api }) => {
    const mala = await call<{ exitosa: boolean; mensaje: string }>(api, "post", token, "/api/v1/invoices/settings/test", {
      ambiente: "Pruebas", clientId: CRED!.clientId, clientSecret: CRED!.clientSecret, usuario: CRED!.usuario, contrasena: "no-es-la-clave",
    });
    expect(mala.exitosa).toBe(false);
    // El motivo real de Factus y el ambiente, no un mensaje genérico
    expect(mala.mensaje).toContain("el usuario o la contraseña no son válidos");
    expect(mala.mensaje).toContain("pruebas (sandbox)");
    expect(mala.mensaje).toContain("invalid_grant");

    const prueba = await call<{ exitosa: boolean; emisorNit: string; rangos: Rango[] }>(api, "post", token, "/api/v1/invoices/settings/test", {
      ambiente: "Pruebas", clientId: CRED!.clientId, clientSecret: CRED!.clientSecret, usuario: CRED!.usuario, contrasena: CRED!.contrasena,
    });
    expect(prueba.exitosa).toBe(true);
    expect(prueba.emisorNit).toBeTruthy();
    rangoFactura = prueba.rangos.find((r) => r.documento === "Factura" && r.vigente)!.id;
    rangoNota = prueba.rangos.find((r) => r.documento === "NotaCredito" && r.vigente)!.id;
    expect(rangoFactura && rangoNota, "el sandbox tiene rangos de factura y de nota crédito").toBeTruthy();
  });

  test("guardar la configuración: las credenciales van cifradas y nunca vuelven", async ({ request: api }) => {
    const dto = await call<Record<string, unknown>>(api, "put", token, "/api/v1/invoices/settings", settingsBody(rangoFactura, {
      clientId: CRED!.clientId, clientSecret: CRED!.clientSecret, usuario: CRED!.usuario, contrasena: CRED!.contrasena,
    }));
    const json = JSON.stringify(dto);
    for (const secreto of Object.values(CRED!)) expect(json.includes(secreto), "el DTO no devuelve credenciales").toBe(false);
    expect(dto).toMatchObject({ configurada: true, habilitada: true, tieneCredenciales: true });

    const guardado = scalar(`SELECT CredencialesCifradas FROM InvoicingSettings WHERE TenantId = ${lit(tenantId)}`);
    expect(guardado.startsWith("v1:")).toBe(true);
    for (const secreto of Object.values(CRED!)) expect(guardado.includes(secreto), "en la base van cifradas").toBe(false);

    // Probar con las guardadas (cuerpo vacío) deja registro de la prueba
    const prueba = await call<{ exitosa: boolean; nitNoCoincide: boolean }>(api, "post", token, "/api/v1/invoices/settings/test", {});
    expect(prueba).toMatchObject({ exitosa: true, nitNoCoincide: false });
  });

  test("emisión desde una venta con saldo: número, CUFE, PDF y XML", async ({ request: api }) => {
    venta1 = await nuevaVenta(api, 50_000); // queda saldo: la factura lleva una parte a crédito
    factura1 = await call<Factura>(api, "post", token, "/api/v1/invoices", { saleId: venta1.id });
    expect(factura1, factura1.mensaje ?? "").toMatchObject({ tipoDocumento: "Factura", estado: "Emitida", saleId: venta1.id, patientId });
    expect(factura1.numero).toBeTruthy();
    expect(factura1.cufe).toBeTruthy();
    expect(factura1.total).toBe(venta1.total);

    const venta = await call<Venta>(api, "get", token, `/api/v1/sales/${venta1.id}`);
    expect(venta).toMatchObject({ facturaEstado: "Emitida", facturaNumero: factura1.numero, facturaPdfId: factura1.pdfId });

    // PDF y XML: adjuntos de la venta, por URL firmada como cualquier archivo
    for (const [id, magia] of [[factura1.pdfId, "%PDF"], [factura1.xmlId, "<"]] as const) {
      expect(id).toBeTruthy();
      const firmado = await call<{ url: string }>(api, "get", token, `/api/v1/files/${id}/url`);
      const archivo = await api.get(firmado.url);
      expect(archivo.status()).toBe(200);
      expect((await archivo.body()).subarray(0, 64).toString("utf8").trimStart().startsWith(magia)).toBe(true);
    }

    // La misma venta no se factura dos veces
    const otra = await api.post("/api/v1/invoices", { ...auth(token), data: { saleId: venta1.id } });
    expect(otra.status()).toBe(409);
  });

  test("consulta: detalle, listado con filtros y Excel", async ({ request: api }) => {
    const detalle = await call<Factura>(api, "get", token, `/api/v1/invoices/${factura1.id}`);
    expect(detalle.lineas).toHaveLength(2);
    expect(detalle.lineas[0]).toMatchObject({ cantidad: 2, cantidadDevuelta: 0 });

    const porVenta = await call<{ items: Factura[] }>(api, "get", token, `/api/v1/invoices?saleId=${venta1.id}`);
    expect(porVenta.items.map((f) => f.id)).toEqual([factura1.id]);
    const porNumero = await call<{ items: Factura[] }>(api, "get", token, `/api/v1/invoices?search=${factura1.numero}&estado=Emitida`);
    expect(porNumero.items.some((f) => f.id === factura1.id)).toBe(true);

    const excel = await api.get(`/api/v1/invoices?patientId=${patientId}&formato=xlsx`, auth(token));
    expect(excel.status()).toBe(200);
    expect((await excel.body()).subarray(0, 2).toString("latin1")).toBe("PK");
  });

  test("rechazo: el proveedor no acepta el rango; se corrige y se emite de nuevo", async ({ request: api }) => {
    await call(api, "put", token, "/api/v1/invoices/settings", settingsBody(999_999));
    const venta = await nuevaVenta(api, null);
    const rechazada = await call<Factura>(api, "post", token, "/api/v1/invoices", { saleId: venta.id });
    expect(rechazada.estado, "rango inexistente → Rechazada").toBe("Rechazada");
    expect(rechazada.mensaje, "trae el motivo del proveedor").toBeTruthy();
    expect((await call<Venta>(api, "get", token, `/api/v1/sales/${venta.id}`)).facturaEstado).toBe("Rechazada");

    await call(api, "put", token, "/api/v1/invoices/settings", settingsBody(rangoFactura));
    const emitida = await call<Factura>(api, "post", token, `/api/v1/invoices/${rechazada.id}/retry`);
    expect(emitida, emitida.mensaje ?? "").toMatchObject({ estado: "Emitida", saleId: venta.id });
    expect(emitida.id).not.toBe(rechazada.id);
    expect(emitida.referencia).not.toBe(rechazada.referencia);
    expect((await call<Factura>(api, "get", token, `/api/v1/invoices/${rechazada.id}`)).estado, "la rechazada queda en el historial").toBe("Rechazada");

    // Anular esa venta emite sola la nota crédito de anulación
    await call(api, "post", token, `/api/v1/sales/${venta.id}/void`, { motivo: "Prueba e2e de anulación facturada" });
    const despues = await call<Factura>(api, "get", token, `/api/v1/invoices/${emitida.id}`);
    expect(despues.estado).toBe("Anulada");
    const notas = await call<{ items: Factura[] }>(api, "get", token, `/api/v1/invoices?saleId=${venta.id}&tipoDocumento=NotaCredito`);
    expect(notas.items).toHaveLength(1);
    expect(notas.items[0]).toMatchObject({ estado: "Emitida", facturaOrigenId: emitida.id, conceptoCorreccion: "2", total: emitida.total });
    expect((await call<Venta>(api, "get", token, `/api/v1/sales/${venta.id}`))).toMatchObject({ estado: "Anulada", facturaEstado: "Anulada" });
  });

  test("nota crédito parcial (devolución) desde una factura emitida", async ({ request: api }) => {
    const nota = await call<Factura>(api, "post", token, `/api/v1/invoices/${factura1.id}/credit-note`, {
      tipo: "Devolucion", motivo: "Devolvió un kit", lineas: [{ indice: 0, cantidad: 1 }],
    });
    expect(nota, nota.mensaje ?? "").toMatchObject({ tipoDocumento: "NotaCredito", estado: "Emitida", conceptoCorreccion: "1", facturaOrigenId: factura1.id });
    expect(nota.numero).toBeTruthy();
    expect(nota.pdfId).toBeTruthy();

    const origen = await call<Factura>(api, "get", token, `/api/v1/invoices/${factura1.id}`);
    expect(origen.estado, "una devolución parcial no anula la factura").toBe("Emitida");
    expect(origen.lineas[0].cantidadDevuelta).toBe(1);
    expect(origen.totalNotasCredito).toBe(nota.total);

    // No se devuelve más de lo facturado; y ya no cabe una anulación total
    const demasiado = await api.post(`/api/v1/invoices/${factura1.id}/credit-note`,
      { ...auth(token), data: { tipo: "Devolucion", motivo: "x", lineas: [{ indice: 0, cantidad: 2 }] } });
    expect(demasiado.status()).toBe(400);
    const anulacion = await api.post(`/api/v1/invoices/${factura1.id}/credit-note`, { ...auth(token), data: { tipo: "Anulacion", motivo: "x" } });
    expect(anulacion.status()).toBe(409);
  });

  test("reenvío por correo: el error del proveedor llega tal cual; a un correo autorizado sale", async ({ request: api }) => {
    // El sandbox solo entrega a correos autorizados en la cuenta: el del paciente de prueba no lo es
    const res = await api.post(`/api/v1/invoices/${factura1.id}/send-email`, { ...auth(token), data: {} });
    expect(res.status()).toBe(502);
    expect(((await res.json()) as { error: string }).error).toContain("no esta autorizado");

    // Ni el usuario ni el correo de la empresa están autorizados en este sandbox: el envío real se
    // prueba solo con un correo que Factus haya autorizado (E2E_FACTUS_EMAIL).
    const autorizado = process.env.E2E_FACTUS_EMAIL ?? "";
    test.skip(!autorizado, "Sin correo autorizado para el sandbox (E2E_FACTUS_EMAIL)");
    const enviada = await call<Factura>(api, "post", token, `/api/v1/invoices/${factura1.id}/send-email`, { correo: autorizado });
    expect(enviada.correoEnviadoA === autorizado, "queda registrado a quién se envió").toBe(true);
  });

  test("factura de un abono de paquete y nota crédito al borrar el pago", async ({ request: api }) => {
    const paquetes = await call<{ items: { id: string; activo?: boolean }[] }>(api, "get", token, "/api/v1/packages?page=1&pageSize=5");
    const asignacion = await call<{ id: string }>(api, "post", token, "/api/v1/patient-packages", {
      patientId, packageId: paquetes.items[0].id, precioAcordado: 200_000, fechaInicio: today(),
    }, 201);
    const pago = await call<{ id: string }>(api, "post", token, `/api/v1/patient-packages/${asignacion.id}/payments`,
      { monto: 80_000, fechaPago: today(), metodoPago: "Transferencia", referencia: `TR-${RUN}` }, 201);

    const factura = await call<Factura>(api, "post", token, "/api/v1/invoices", { paymentId: pago.id });
    expect(factura, factura.mensaje ?? "").toMatchObject({ estado: "Emitida", paymentId: pago.id, patientId, total: 80_000 });

    const res = await api.delete(`/api/v1/patient-packages/${asignacion.id}/payments/${pago.id}`, auth(token));
    expect(res.status()).toBe(204);
    expect((await call<Factura>(api, "get", token, `/api/v1/invoices/${factura.id}`)).estado).toBe("Anulada");
  });

  test("el job facturas-reintento corre (sin pendientes no hace nada)", async ({ request: api }) => {
    const res = await api.post("/api/v1/jobs/facturas-reintento", { headers: { "X-Jobs-Key": JOBS_KEY } });
    const body = await res.text();
    expect(res.status(), body).toBe(200);
  });
});

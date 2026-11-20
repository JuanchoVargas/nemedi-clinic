// Reglas de negocio que no deben romperse, verificadas por API contra el tenant demo (proyecto "api" de pnpm test,
// junto con el aislamiento multi-tenant). Cada bloque crea sus propios datos y los elimina al final.
//
//   · Pagos: un pago no puede superar el saldo (422), guarda quién lo registró, referencia y
//     comprobante; POST /files acepta PDF solo por magic bytes y solo para Comprobante/Consentimiento.
//   · Consumo de cabina: la nota de una cita Completada descuenta inventario y deja la salida ligada
//     al paciente; sin cita completada no mueve stock y nunca lo deja negativo.
//   · Un paquete de catálogo eliminado no esconde ni altera las asignaciones ya vendidas.
//   · Recepción crea esteticistas, no administradores.
//   · La evolución llega agrupada por paquete, con el detalle de cada sesión y sin dinero para la esteticista.
//   · El embudo de valoraciones cuadra con los datos demo y convertir un prospecto crea paciente + paquete.
//
// Requisitos: los mismos del gate de aislamiento (API en 5055 y el seed demo).
import { test, expect, type APIRequestContext } from "@playwright/test";
import { sql } from "./sql";
import { RIPS_PACIENTE } from "./rips";
import { samplePng } from "./ui/helpers/sample-image";
import { samplePdf } from "./ui/helpers/sample-pdf";
import { SUPERADMIN, DEMO_EMAIL, demoPassword, tempPassword } from "./credentials";

const ADMIN_EMAIL = SUPERADMIN.email;
const ADMIN_PASSWORD = SUPERADMIN.password;
const RUN = Date.now().toString().slice(-8);

const pad = (n: number) => String(n).padStart(2, "0");
const today = () => enDias(0);
/** Fecha local a N días de hoy (YYYY-MM-DD). Los tests agendan lejos para no chocar con la demo. */
const enDias = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};
const auth = (token: string) => ({ headers: { Authorization: `Bearer ${token}` } });

async function login(api: APIRequestContext, email: string, password: string) {
  const res = await api.post("/api/v1/auth/login", { data: { email, password } });
  expect(res.status(), `login ${email}`).toBe(200);
  return (await res.json()) as { token: string; mustChangePassword: boolean; userInfo: { id: string; nombre: string; apellido: string } };
}

async function post<T>(api: APIRequestContext, token: string, url: string, data: unknown): Promise<T> {
  const res = await api.post(url, { ...auth(token), data });
  expect(res.status(), `POST ${url} → ${await res.text()}`).toBeLessThan(300);
  return (await res.json()) as T;
}

async function get<T>(api: APIRequestContext, token: string, url: string): Promise<T> {
  const res = await api.get(url, auth(token));
  expect(res.status(), `GET ${url}`).toBe(200);
  return (await res.json()) as T;
}

function upload(api: APIRequestContext, token: string, file: { name: string; mimeType: string; buffer: Buffer }, fields: Record<string, string>) {
  return api.post("/api/v1/files", { ...auth(token), multipart: { file, ...fields } });
}

test.describe("Pagos con trazabilidad", () => {
  let token = "";
  let adminName = "";
  let patientId = "";
  let patientPackageId = "";
  const PRECIO = 400_000;

  test.beforeAll(async ({ request: api }) => {
    const session = await login(api, ADMIN_EMAIL, ADMIN_PASSWORD);
    token = session.token;
    adminName = `${session.userInfo.nombre} ${session.userInfo.apellido}`.trim();

    const patient = await post<{ id: string }>(api, token, "/api/v1/patients", {
      nombre: "Pagos", apellido: `E2E ${RUN}`, cedula: `77${RUN}`, telefono: "3000000000", ...RIPS_PACIENTE,
    });
    patientId = patient.id;

    const packages = await get<{ items: { id: string; nombre: string }[] }>(api, token, "/api/v1/packages?page=1&pageSize=50");
    const pkg = packages.items[0];
    expect(pkg, "el seed demo debe tener paquetes").toBeTruthy();

    const assigned = await post<{ id: string }>(api, token, "/api/v1/patient-packages", {
      patientId, packageId: pkg.id, precioAcordado: PRECIO, fechaInicio: today(),
    });
    patientPackageId = assigned.id;
  });

  test.afterAll(async ({ request: api }) => {
    if (patientPackageId) await api.delete(`/api/v1/patient-packages/${patientPackageId}`, auth(token));
    if (patientId) await api.delete(`/api/v1/patients/${patientId}`, auth(token));
  });

  test("sin pagos: 0 % y estado SinPagos", async ({ request: api }) => {
    const pp = await get<{ porcentajePagado: number; estadoPago: string; saldoPendiente: number }>(api, token, `/api/v1/patient-packages/${patientPackageId}`);
    expect(pp).toMatchObject({ porcentajePagado: 0, estadoPago: "SinPagos", saldoPendiente: PRECIO });
  });

  test("un pago mayor al saldo responde 422 con el saldo en el mensaje y no guarda nada", async ({ request: api }) => {
    const res = await api.post(`/api/v1/patient-packages/${patientPackageId}/payments`, {
      ...auth(token), data: { monto: PRECIO + 1, fechaPago: today(), metodoPago: "Efectivo" },
    });
    expect(res.status()).toBe(422);
    expect((await res.json()).error).toBe("El pago supera el saldo pendiente ($400.000)");

    const payments = await get<unknown[]>(api, token, `/api/v1/patient-packages/${patientPackageId}/payments`);
    expect(payments).toHaveLength(0);
  });

  test("abono con referencia y comprobante PDF: queda Parcial, con quién lo registró", async ({ request: api }) => {
    const pdf = await upload(api, token, { name: "transferencia.pdf", mimeType: "application/pdf", buffer: samplePdf() }, { entityType: "Payment", kind: "Comprobante" });
    expect(pdf.status(), await pdf.text()).toBe(201);
    const attachment = (await pdf.json()) as { id: string; contentType: string };
    expect(attachment.contentType).toBe("application/pdf");

    const payment = await post<Record<string, unknown>>(api, token, `/api/v1/patient-packages/${patientPackageId}/payments`, {
      monto: 256_000, fechaPago: today(), metodoPago: "Transferencia", referencia: "  TRF-998877  ", comprobanteId: attachment.id,
    });
    expect(payment).toMatchObject({
      monto: 256_000, referencia: "TRF-998877", registradoPor: adminName,
      comprobanteId: attachment.id, comprobanteContentType: "application/pdf",
    });

    const pp = await get<{ porcentajePagado: number; estadoPago: string; saldoPendiente: number }>(api, token, `/api/v1/patient-packages/${patientPackageId}`);
    expect(pp).toMatchObject({ porcentajePagado: 64, estadoPago: "Parcial", saldoPendiente: 144_000 });

    // El PDF se sirve con su URL firmada y su tipo real
    const signed = await get<{ url: string }>(api, token, `/api/v1/files/${attachment.id}/url`);
    const file = await api.get(signed.url);
    expect(file.status()).toBe(200);
    expect(file.headers()["content-type"]).toContain("application/pdf");
    expect((await file.body()).subarray(0, 5).toString()).toBe("%PDF-");
  });

  test("adjuntar el comprobante después, desde la fila (imagen)", async ({ request: api }) => {
    const payment = await post<{ id: string; comprobanteId: string | null }>(api, token, `/api/v1/patient-packages/${patientPackageId}/payments`, {
      monto: 44_000, fechaPago: today(), metodoPago: "Efectivo",
    });
    expect(payment.comprobanteId).toBeNull();

    const img = await upload(api, token, { name: "recibo.png", mimeType: "image/png", buffer: samplePng([31, 78, 121], [217, 164, 65]) },
      { entityType: "Payment", kind: "Comprobante", entityId: payment.id });
    expect(img.status(), await img.text()).toBe(201);
    const attachment = (await img.json()) as { id: string };

    const res = await api.put(`/api/v1/patient-packages/${patientPackageId}/payments/${payment.id}/comprobante`, { ...auth(token), data: { comprobanteId: attachment.id } });
    expect(res.status(), await res.text()).toBe(200);
    expect(await res.json()).toMatchObject({ comprobanteId: attachment.id, comprobanteContentType: "image/png" });
  });

  test("saldar: el saldo exacto pasa, un peso más no; queda 100 % Pagado", async ({ request: api }) => {
    const over = await api.post(`/api/v1/patient-packages/${patientPackageId}/payments`, {
      ...auth(token), data: { monto: 100_001, fechaPago: today(), metodoPago: "Tarjeta" },
    });
    expect(over.status()).toBe(422);
    expect((await over.json()).error).toContain("$100.000");

    await post(api, token, `/api/v1/patient-packages/${patientPackageId}/payments`, { monto: 100_000, fechaPago: today(), metodoPago: "Tarjeta", referencia: "Voucher 4471" });

    const list = await get<{ id: string; porcentajePagado: number; estadoPago: string; saldoPendiente: number }[]>(api, token, `/api/v1/patient-packages/patient/${patientId}`);
    expect(list.find((p) => p.id === patientPackageId)).toMatchObject({ porcentajePagado: 100, estadoPago: "Pagado", saldoPendiente: 0 });

    const patient = await get<{ paquetesActivos: { porcentajePagado: number }[] }>(api, token, `/api/v1/patients/${patientId}`);
    expect(patient.paquetesActivos[0].porcentajePagado).toBe(100);
  });

  test("POST /files: el PDF se valida por magic bytes y solo aplica a Comprobante y Consentimiento", async ({ request: api }) => {
    // Un .pdf que en realidad es texto → 415
    const fake = await upload(api, token, { name: "falso.pdf", mimeType: "application/pdf", buffer: Buffer.from("esto no es un pdf, de verdad que no") }, { entityType: "Payment", kind: "Comprobante" });
    expect(fake.status()).toBe(415);

    // Un PDF real donde solo se aceptan imágenes → 415
    const asProfile = await upload(api, token, { name: "foto.pdf", mimeType: "image/png", buffer: samplePdf() }, { entityType: "Patient", kind: "Perfil", entityId: patientId });
    expect(asProfile.status()).toBe(415);

    // Consentimiento escaneado: PDF aceptado, exige paciente y no se puede borrar
    const orphan = await upload(api, token, { name: "consentimiento.pdf", mimeType: "application/pdf", buffer: samplePdf("Consentimiento") }, { entityType: "Patient", kind: "Consentimiento" });
    expect(orphan.status()).toBe(400);
    const consent = await upload(api, token, { name: "consentimiento.pdf", mimeType: "application/pdf", buffer: samplePdf("Consentimiento") }, { entityType: "Patient", kind: "Consentimiento", entityId: patientId });
    expect(consent.status(), await consent.text()).toBe(201);
    const del = await api.delete(`/api/v1/files/${(await consent.json()).id}`, auth(token));
    expect(del.status()).toBe(409);
  });
});

test.describe("Consumo de cabina", () => {
  let token = "";
  let patientId = "";
  let esteticistId = "";
  let appointmentId = "";
  let productId = "";
  let stockInicial = 0;

  test.beforeAll(async ({ request: api }) => {
    token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;

    const patient = await post<{ id: string }>(api, token, "/api/v1/patients", {
      nombre: "Cabina", apellido: `E2E ${RUN}`, cedula: `78${RUN}`, telefono: "3000000001", ...RIPS_PACIENTE,
    });
    patientId = patient.id;

    const users = await get<{ items: { id: string; rol: string }[] }>(api, token, "/api/v1/users?page=1&pageSize=50");
    esteticistId = users.items.find((u) => u.rol === "Esteticista")!.id;

    const products = await get<{ items: { id: string; stockActual: number; tipoProducto: string }[] }>(
      api, token, "/api/v1/products?page=1&pageSize=50");
    const insumo = products.items.find((p) => p.tipoProducto !== "Venta" && p.stockActual >= 5)!;
    productId = insumo.id;
    stockInicial = insumo.stockActual;

    const procedures = await get<{ items: { id: string }[] }>(api, token, "/api/v1/procedures?page=1&pageSize=20");
    const branches = await get<{ items: { id: string }[] }>(api, token, "/api/v1/branches");
    const cita = await post<{ id: string }>(api, token, "/api/v1/appointments", {
      patientId, esteticistId, procedureId: procedures.items[0].id, branchId: branches.items[0].id,
      fechaInicio: `${today()}T07:00:00`, fechaFin: `${today()}T07:30:00`,
    });
    appointmentId = cita.id;
  });

  test.afterAll(async ({ request: api }) => {
    // El movimiento de inventario es un libro: no se borra por API. Como esto es la base de
    // desarrollo (y sus datos se usan en las demos), el test limpia lo suyo y devuelve el stock.
    // Desde los lotes, devolver el stock es devolverlo AL LOTE: la columna del producto se
    // recalcula desde ahí y volver a escribirla sola no serviría de nada.
    if (productId) {
      sql(`DELETE FROM InventoryMovements WHERE Referencia LIKE 'Consumo de cabina · Sesión%';`);
      sql(`UPDATE ProductLots SET CantidadDisponible = CantidadInicial WHERE ProductId = '${productId}';`);
      sql(`UPDATE Products SET StockActual = ${stockInicial} WHERE Id = '${productId}';`);
    }
    if (appointmentId) {
      await api.put(`/api/v1/appointments/${appointmentId}/status`, { ...auth(token), data: { estado: "Agendada" } });
      await api.delete(`/api/v1/appointments/${appointmentId}`, auth(token));
    }
    if (patientId) await api.delete(`/api/v1/patients/${patientId}`, auth(token));
  });

  test("una nota sin cita completada guarda los productos pero no mueve inventario", async ({ request: api }) => {
    const res = await post<{ nota: { id: string; productos: unknown[] } }>(
      api, token, `/api/v1/patients/${patientId}/clinical-record/notes`,
      { esteticistId, procedimiento: "Sesión sin cerrar", observaciones: "La cita sigue agendada.", productos: [{ productId, cantidad: 2 }] });

    expect(res.nota.productos).toHaveLength(1);
    const product = await get<{ stockActual: number }>(api, token, `/api/v1/products/${productId}`);
    expect(product.stockActual, "el stock no cambia hasta que la cita se completa").toBe(stockInicial);
  });

  test("al completar la cita, la nota descuenta el stock y deja la salida con paciente", async ({ request: api }) => {
    for (const estado of ["Confirmada", "EnCurso", "Completada"]) {
      const res = await api.put(`/api/v1/appointments/${appointmentId}/status`, { ...auth(token), data: { estado } });
      expect(res.status(), `estado ${estado} → ${await res.text()}`).toBeLessThan(300);
    }

    await post(api, token, `/api/v1/patients/${patientId}/clinical-record/notes`,
      { esteticistId, appointmentId, procedimiento: "Sesión cerrada", observaciones: "Se gastaron insumos.", productos: [{ productId, cantidad: 2 }] });

    const product = await get<{ stockActual: number }>(api, token, `/api/v1/products/${productId}`);
    expect(product.stockActual).toBe(stockInicial - 2);

    const movimientos = await get<{ tipoMovimiento: string; cantidad: number; patientId: string | null; pacienteNombre: string | null }[]>(
      api, token, `/api/v1/inventory/movements/product/${productId}`);
    const salida = movimientos.find((m) => m.tipoMovimiento === "Salida" && m.patientId === patientId);
    expect(salida, "la salida queda ligada al paciente de la sesión").toBeTruthy();
    expect(salida!.cantidad).toBe(2);
    expect(salida!.pacienteNombre).toContain("Cabina");

    const consumo = await get<{ producto: string }[]>(api, token, `/api/v1/patients/${patientId}/consumption`);
    expect(consumo.length).toBeGreaterThanOrEqual(2);
  });

  test("no se puede consumir más de lo que hay en stock", async ({ request: api }) => {
    const res = await api.post(`/api/v1/patients/${patientId}/clinical-record/notes`, {
      ...auth(token),
      data: { esteticistId, appointmentId, procedimiento: "Sesión imposible", observaciones: "x", productos: [{ productId, cantidad: 99_999 }] },
    });
    // Con lotes el rechazo es 422 y dice cuánto hay sin vencer; sin lotes sería 400
    expect(res.status()).toBe(422);
    expect((await res.json()).error).toContain("sin vencer");
  });

  test("un lote vencido no cuenta como existencia", async ({ request: api }) => {
    // La mascarilla de colágeno del seed tiene su único lote vencido
    const products = await get<{ items: { id: string; nombre: string }[] }>(api, token, "/api/v1/products?page=1&pageSize=50");
    const vencido = products.items.find((p) => p.nombre.startsWith("Mascarilla"));
    if (!vencido) return;

    const lotes = await get<{ estado: string; cantidadDisponible: number }[]>(api, token, `/api/v1/products/${vencido.id}/lots`);
    expect(lotes.some((l) => l.estado === "Vencido" && l.cantidadDisponible > 0), "el seed deja un lote vencido con existencia").toBe(true);

    const res = await api.post(`/api/v1/patients/${patientId}/clinical-record/notes`, {
      ...auth(token),
      data: { esteticistId, appointmentId, procedimiento: "Sesión con vencido", observaciones: "x", productos: [{ productId: vencido.id, cantidad: 1 }] },
    });
    expect(res.status()).toBe(422);
    const error = (await res.json()).error as string;
    expect(error).toContain("lotes vencidos");
  });
});

test.describe("Paquete vendido: copia del catálogo", () => {
  test("borrar el paquete del catálogo no esconde la asignación ni cambia lo vendido", async ({ request: api }) => {
    const token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;

    const procedures = await get<{ items: { id: string }[] }>(api, token, "/api/v1/procedures?page=1&pageSize=20");
    const paquete = await post<{ id: string }>(api, token, "/api/v1/packages", {
      nombre: `Paquete E2E ${RUN}`, descripcion: "Se elimina a propósito", precioTotal: 200_000,
      sesionesTotales: 3, vigenciaDias: 90, diasAlertaVencimiento: 15,
    });
    // Los procedimientos del paquete se agregan aparte
    await post(api, token, `/api/v1/packages/${paquete.id}/procedures`, { procedureId: procedures.items[0].id, cantidadSesiones: 2 });
    await post(api, token, `/api/v1/packages/${paquete.id}/procedures`, { procedureId: procedures.items[1].id, cantidadSesiones: 1 });
    const patient = await post<{ id: string }>(api, token, "/api/v1/patients", {
      nombre: "Snapshot", apellido: `E2E ${RUN}`, cedula: `79${RUN}`, telefono: "3000000002", ...RIPS_PACIENTE,
    });
    const asignacion = await post<{ id: string }>(api, token, "/api/v1/patient-packages", {
      patientId: patient.id, packageId: paquete.id, precioAcordado: 200_000, fechaInicio: today(),
    });

    // Sin asignaciones se puede quitar uno (mientras quede al menos otro)
    const sinVender = await post<{ id: string }>(api, token, "/api/v1/packages", {
      nombre: `Paquete libre ${RUN}`, descripcion: "Sin vender", precioTotal: 50_000,
      sesionesTotales: 2, vigenciaDias: 30, diasAlertaVencimiento: 5,
    });
    await post(api, token, `/api/v1/packages/${sinVender.id}/procedures`, { procedureId: procedures.items[0].id, cantidadSesiones: 1 });
    await post(api, token, `/api/v1/packages/${sinVender.id}/procedures`, { procedureId: procedures.items[1].id, cantidadSesiones: 1 });
    const quitado = await api.delete(`/api/v1/packages/${sinVender.id}/procedures/${procedures.items[1].id}`, auth(token));
    expect(quitado.status(), await quitado.text()).toBe(204);
    // El último no se puede quitar: un paquete sin procedimientos no significa nada
    const ultimo = await api.delete(`/api/v1/packages/${sinVender.id}/procedures/${procedures.items[0].id}`, auth(token));
    expect(ultimo.status()).toBe(409);
    await api.delete(`/api/v1/packages/${sinVender.id}`, auth(token));

    // Ya vendido, quitarle un procedimiento se rechaza: movería sesiones pactadas
    const quitar = await api.delete(`/api/v1/packages/${paquete.id}/procedures/${procedures.items[0].id}`, auth(token));
    expect(quitar.status()).toBe(409);
    expect((await quitar.json()).error).toContain("ya está asignado");

    const borrado = await api.delete(`/api/v1/packages/${paquete.id}`, auth(token));
    expect(borrado.status()).toBeLessThan(300);

    const dto = await get<{ packageNombre: string; sesionesTotales: number; fechaVencimiento: string | null; sesiones: unknown[] }>(
      api, token, `/api/v1/patient-packages/${asignacion.id}`);
    expect(dto.packageNombre).toBe(`Paquete E2E ${RUN}`);
    expect(dto.sesionesTotales).toBe(3);
    expect(dto.fechaVencimiento, "la vigencia acordada se conserva").not.toBeNull();
    expect(dto.sesiones).toHaveLength(3);

    await api.delete(`/api/v1/patient-packages/${asignacion.id}`, auth(token));
    await api.delete(`/api/v1/patients/${patient.id}`, auth(token));
  });
});

test.describe("Quién puede crear usuarios", () => {
  test("recepción crea esteticistas pero no administradores", async ({ request: api }) => {
    const superToken = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;
    const creados: string[] = [];
    const recepcionTemporal = tempPassword();

    const recepcion = await post<{ id: string }>(api, superToken, "/api/v1/auth/register", {
      nombre: "Recepción", apellido: `E2E ${RUN}`, email: `recepcion.${RUN}@e2e.test`,
      password: recepcionTemporal, rol: "Admin",
    });
    creados.push(recepcion.id);

    // Toda cuenta nueva nace con clave temporal: hay que cambiarla para poder operar
    const primer = await login(api, `recepcion.${RUN}@e2e.test`, recepcionTemporal);
    expect(primer.mustChangePassword).toBe(true);
    const cambio = await api.post("/api/v1/auth/change-password", {
      ...auth(primer.token), data: { currentPassword: recepcionTemporal, newPassword: tempPassword() },
    });
    expect(cambio.status(), await cambio.text()).toBe(200);
    const adminToken = ((await cambio.json()) as { token: string }).token;

    const esteticista = await api.post("/api/v1/auth/register", {
      ...auth(adminToken),
      data: { nombre: "Cabina", apellido: `E2E ${RUN}`, email: `cabina.${RUN}@e2e.test`, password: tempPassword(), rol: "Esteticista" },
    });
    expect(esteticista.status(), await esteticista.text()).toBeLessThan(300);
    creados.push(((await esteticista.json()) as { id: string }).id);

    const otroAdmin = await api.post("/api/v1/auth/register", {
      ...auth(adminToken),
      data: { nombre: "Otro", apellido: `E2E ${RUN}`, email: `otro.${RUN}@e2e.test`, password: tempPassword(), rol: "Admin" },
    });
    expect(otroAdmin.status()).toBe(403);
    expect((await otroAdmin.json()).error).toContain("Solo el dueño");

    for (const id of creados) await api.delete(`/api/v1/users/${id}`, auth(superToken));
  });
});

test.describe("Evolución agrupada por paquete", () => {
  // Los ids se guardan fuera del test: así la limpieza corre aunque una aserción falle
  // (si no, cada corrida fallida dejaría una cita que choca con la siguiente).
  let token = "";
  let patientId = "";
  let packageId = "";
  let patientPackageId = "";
  let appointmentId = "";
  let esteticistId = "";
  let procedimientos: { id: string; nombre: string; duracionMinutos: number }[] = [];

  test.beforeAll(async ({ request: api }) => {
    token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;

    const patient = await post<{ id: string }>(api, token, "/api/v1/patients", {
      nombre: "Evolución", apellido: `E2E ${RUN}`, cedula: `80${RUN}`, telefono: "3000000003", ...RIPS_PACIENTE,
    });
    patientId = patient.id;

    const users = await get<{ items: { id: string; rol: string }[] }>(api, token, "/api/v1/users?page=1&pageSize=50");
    esteticistId = users.items.find((u) => u.rol === "Esteticista")!.id;
    procedimientos = (await get<{ items: { id: string; nombre: string; duracionMinutos: number }[] }>(
      api, token, "/api/v1/procedures?page=1&pageSize=20")).items;

    const paquete = await post<{ id: string }>(api, token, "/api/v1/packages", {
      nombre: `Paquete evolución ${RUN}`, descripcion: "Dos sesiones", precioTotal: 300_000,
      sesionesTotales: 2, vigenciaDias: 60, diasAlertaVencimiento: 10,
    });
    packageId = paquete.id;
    await post(api, token, `/api/v1/packages/${packageId}/procedures`, { procedureId: procedimientos[0].id, cantidadSesiones: 2 });

    const asignacion = await post<{ id: string }>(api, token, "/api/v1/patient-packages", {
      patientId, packageId, precioAcordado: 300_000, fechaInicio: today(),
    });
    patientPackageId = asignacion.id;
    await post(api, token, `/api/v1/patient-packages/${patientPackageId}/payments`, { monto: 150_000, fechaPago: today(), metodoPago: "Efectivo" });

    // Una nota ligada a la sesión 1 del paquete, con todo el detalle. La cita va lejos en el
    // calendario para no chocar con la agenda demo.
    const detalle = await get<{ sesiones: { id: string; numero: number }[] }>(api, token, `/api/v1/patient-packages/${patientPackageId}`);
    const sesion1 = detalle.sesiones.find((s) => s.numero === 1)!;
    const branches = await get<{ items: { id: string }[] }>(api, token, "/api/v1/branches");
    const cita = await post<{ id: string }>(api, token, "/api/v1/appointments", {
      patientId, esteticistId, procedureId: procedimientos[0].id, branchId: branches.items[0].id,
      fechaInicio: `${enDias(45)}T08:00:00`, fechaFin: `${enDias(45)}T09:00:00`, patientPackageSessionId: sesion1.id,
    });
    appointmentId = cita.id;

    await post(api, token, `/api/v1/patients/${patientId}/clinical-record/notes`, {
      esteticistId, appointmentId, procedimiento: procedimientos[0].nombre,
      observaciones: "Sesión del paquete.", zonaTratada: "Abdomen",
      parametros: "Intensidad media · 30 min", indicacionesPost: "Tomar agua.",
      proximaSesionSugerida: today(), evaluacionPaciente: 5, productos: [],
    });

    // Una nota suelta, sin cita
    await post(api, token, `/api/v1/patients/${patientId}/clinical-record/notes`, {
      esteticistId, procedimiento: procedimientos[1].nombre, observaciones: "Consulta aparte.", productos: [],
    });
  });

  test.afterAll(async ({ request: api }) => {
    if (appointmentId) {
      await api.put(`/api/v1/appointments/${appointmentId}/status`, { ...auth(token), data: { estado: "Agendada" } });
      await api.delete(`/api/v1/appointments/${appointmentId}`, auth(token));
    }
    if (patientPackageId) await api.delete(`/api/v1/patient-packages/${patientPackageId}`, auth(token));
    if (packageId) await api.delete(`/api/v1/packages/${packageId}`, auth(token));
    if (patientId) await api.delete(`/api/v1/patients/${patientId}`, auth(token));
  });

  test("las sesiones del paquete van en su grupo y las notas sueltas en el suyo", async ({ request: api }) => {
    const evolucion = await get<{
      grupos: {
        patientPackageId: string | null; nombre: string; estado: string;
        sesionesCompletadas: number; sesionesTotales: number; porcentajePagado: number | null;
        sesiones: { numeroSesion: number | null; zonaTratada: string | null; parametros: string | null; evaluacionPaciente: number | null; duracionMinutos: number | null; procedureId: string | null }[];
      }[];
    }>(api, token, `/api/v1/patients/${patientId}/evolution`);

    expect(evolucion.grupos).toHaveLength(2);

    const delPaquete = evolucion.grupos[0];
    expect(delPaquete.patientPackageId).toBe(patientPackageId);
    expect(delPaquete.nombre).toBe(`Paquete evolución ${RUN}`);
    expect(delPaquete.estado).toBe("Activo");
    expect(delPaquete.sesionesTotales).toBe(2);
    expect(delPaquete.porcentajePagado, "la mitad pagada").toBe(50);
    expect(delPaquete.sesiones).toHaveLength(1);
    expect(delPaquete.sesiones[0]).toMatchObject({
      numeroSesion: 1, zonaTratada: "Abdomen", parametros: "Intensidad media · 30 min",
      evaluacionPaciente: 5,
      // La duración real sale de la cita, que el API cierra con la duración del procedimiento
      duracionMinutos: procedimientos[0].duracionMinutos,
    });

    const sueltas = evolucion.grupos[1];
    expect(sueltas.patientPackageId).toBeNull();
    expect(sueltas.nombre).toBe("Sesiones sueltas");
    expect(sueltas.sesiones).toHaveLength(1);
    expect(sueltas.sesiones[0].numeroSesion).toBeNull();
    // Sin cita no hay duración, pero el procedimiento se resuelve por nombre para poder agendar
    expect(sueltas.sesiones[0].duracionMinutos).toBeNull();
    expect(sueltas.sesiones[0].procedureId).toBe(procedimientos[1].id);
  });

  test("la esteticista ve la evolución pero no el dinero", async ({ request: api }) => {
    const laura = await login(api, DEMO_EMAIL.laura, demoPassword());
    const evolucion = await get<{ grupos: { porcentajePagado: number | null; sesiones: unknown[] }[] }>(
      api, laura.token, `/api/v1/patients/${patientId}/evolution`);
    expect(evolucion.grupos[0].sesiones.length).toBeGreaterThan(0);
    expect(evolucion.grupos[0].porcentajePagado).toBeNull();
  });
});

test.describe("Valoraciones: embudo y conversión", () => {
  test("los datos demo dan 3 de 8 aceptadas y el monto aceptado cuadra", async ({ request: api }) => {
    const token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;

    const valoraciones = await get<{
      id: string; fecha: string; estado: string; esProspecto: boolean; patientId: string | null;
      patientPackageId: string | null; precioCotizado: number; motivoRechazo: string | null;
      fotos: unknown[];
    }[]>(api, token, "/api/v1/valuations");

    const delMes = valoraciones.filter((v) => v.estado);
    expect(delMes.length, "el seed demo siembra 8 valoraciones").toBeGreaterThanOrEqual(8);

    const aceptadas = delMes.filter((v) => v.estado === "Acepto");
    const pendientes = delMes.filter((v) => v.estado === "Pendiente");
    const rechazadas = delMes.filter((v) => v.estado === "Rechazo");
    expect(aceptadas.length).toBe(3);
    expect(pendientes.length).toBe(3);
    expect(rechazadas.length).toBe(2);

    // Las aceptadas son de pacientes y llevan su paquete asignado: la trazabilidad valoración → venta
    expect(aceptadas.every((v) => !v.esProspecto && v.patientId)).toBe(true);
    expect(aceptadas.every((v) => v.patientPackageId), "cada aceptada enlaza su asignación").toBe(true);

    // Las pendientes son prospectos sin cédula y una trae foto
    expect(pendientes.every((v) => v.esProspecto && !v.patientId)).toBe(true);
    expect(pendientes.some((v) => v.fotos.length > 0), "una pendiente trae foto Antes").toBe(true);

    // Las rechazadas dicen por qué
    expect(rechazadas.every((v) => !!v.motivoRechazo)).toBe(true);
    expect(rechazadas.map((v) => v.motivoRechazo!.toLowerCase()).some((m) => m.includes("precio"))).toBe(true);
    expect(rechazadas.map((v) => v.motivoRechazo!.toLowerCase()).some((m) => m.includes("pensar"))).toBe(true);

    // El seed reparte las valoraciones en el mes en que se sembró: el embudo se pide de ESE mes
    // (pedir el mes en curso fallaba desde el día 1 del mes siguiente)
    const mes = aceptadas[0].fecha.slice(0, 7);
    const stats = await get<{
      total: number; pendientes: number; aceptadas: number; rechazadas: number;
      tasaConversion: number; valorCotizado: number; valorAceptado: number;
    }>(api, token, `/api/v1/valuations/stats?mes=${mes}`);

    expect(stats).toMatchObject({ total: 8, pendientes: 3, aceptadas: 3, rechazadas: 2 });
    expect(Number(stats.tasaConversion.toFixed(3)), "3 de 8 = 37,5 %").toBe(0.375);
    expect(stats.valorCotizado).toBe(delMes.reduce((sum, v) => sum + v.precioCotizado, 0));
    expect(stats.valorAceptado).toBe(aceptadas.reduce((sum, v) => sum + v.precioCotizado, 0));
  });

  test("convertir un prospecto crea el paciente con su paquete y lo deja en la ficha", async ({ request: api }) => {
    const token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;
    const users = await get<{ items: { id: string; rol: string }[] }>(api, token, "/api/v1/users?page=1&pageSize=50");
    const esteticistId = users.items.find((u) => u.rol === "Esteticista")!.id;
    const packages = await get<{ items: { id: string; nombre: string }[] }>(api, token, "/api/v1/packages?page=1&pageSize=20");
    const paquete = packages.items[0];

    const valoracion = await post<{ id: string }>(api, token, "/api/v1/valuations", {
      prospectoNombre: `Prospecto E2E ${RUN}`, prospectoTelefono: "3001112233",
      esteticistId, fecha: `${today()}T10:00:00`,
      diagnostico: "Consulta de prueba automatizada.",
      tratamientoSugerido: "Plan sugerido de prueba.",
      packageId: paquete.id, precioCotizado: 500_000,
    });

    const convertida = await post<{ patientId: string; patientPackageId: string | null }>(
      api, token, `/api/v1/valuations/${valoracion.id}/convert`,
      { cedula: `81${RUN}`, precioAcordado: 500_000, fechaInicio: today() });

    expect(convertida.patientId, "la conversión crea el paciente").toBeTruthy();
    expect(convertida.patientPackageId, "y le asigna el paquete cotizado").toBeTruthy();

    // La valoración queda aceptada y apuntando a la asignación
    const despues = await get<{ estado: string; esProspecto: boolean; patientPackageId: string | null }>(
      api, token, `/api/v1/valuations/${valoracion.id}`);
    expect(despues).toMatchObject({ estado: "Acepto", esProspecto: false, patientPackageId: convertida.patientPackageId });

    // Y el paquete aparece en la ficha del paciente nuevo
    const asignados = await get<{ id: string; packageNombre: string }[]>(
      api, token, `/api/v1/patient-packages/patient/${convertida.patientId}`);
    expect(asignados.map((p) => p.id)).toContain(convertida.patientPackageId);
    expect(asignados[0].packageNombre).toBe(paquete.nombre);

    await api.delete(`/api/v1/patient-packages/${convertida.patientPackageId}`, auth(token));
    await api.delete(`/api/v1/valuations/${valoracion.id}`, auth(token));
    await api.delete(`/api/v1/patients/${convertida.patientId}`, auth(token));
  });
});

test.describe("RIPS: la clínica demo es IPS", () => {
  let token = "";
  const creados: string[] = [];
  const base = () => ({ nombre: "Rips", apellido: `E2E ${RUN}`, telefono: "3000000090" });
  let n = 0;
  const cedula = () => `90${RUN}${n++}`;

  test.beforeAll(async ({ request: api }) => {
    token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;
  });

  test.afterAll(async ({ request: api }) => {
    for (const id of creados) await api.delete(`/api/v1/patients/${id}`, auth(token));
  });

  test("un paciente sin datos RIPS se rechaza con lo que falta; completo, pasa", async ({ request: api }) => {
    const sinRips = await api.post("/api/v1/patients", { ...auth(token), data: { ...base(), cedula: cedula() } });
    expect(sinRips.status()).toBe(400);
    const { error } = (await sinRips.json()) as { error: string };
    for (const campo of ["tipo de documento", "fecha de nacimiento", "sexo", "zona de residencia", "régimen de afiliación"])
      expect(error, campo).toContain(campo);

    const municipioMalo = await api.post("/api/v1/patients", { ...auth(token), data: { ...base(), cedula: cedula(), ...RIPS_PACIENTE, municipioResidencia: "110" } });
    expect(municipioMalo.status(), "el DANE tiene 5 dígitos").toBe(400);

    const contributivoSinEps = await api.post("/api/v1/patients", { ...auth(token), data: { ...base(), cedula: cedula(), ...RIPS_PACIENTE, tipoUsuario: "ContributivoCotizante" } });
    expect(contributivoSinEps.status()).toBe(400);
    expect(((await contributivoSinEps.json()) as { error: string }).error).toContain("entidad (EPS)");

    const particularConEps = await api.post("/api/v1/patients", { ...auth(token), data: { ...base(), cedula: cedula(), ...RIPS_PACIENTE, codigoEntidad: "EPS005" } });
    expect(particularConEps.status(), "un particular no tiene EPS").toBe(400);

    const ok = await post<{ id: string }>(api, token, "/api/v1/patients",
      { ...base(), cedula: cedula(), ...RIPS_PACIENTE, tipoUsuario: "ContributivoCotizante", codigoEntidad: "eps005", nombreEntidad: "EPS Sanitas" });
    creados.push(ok.id);
    const ficha = await get<{ ripsCompleto: boolean | null; ripsFaltantes: string[]; codigoEntidad: string; tipoDocumento: string; municipioResidencia: string }>(
      api, token, `/api/v1/patients/${ok.id}`);
    expect(ficha).toMatchObject({ ripsCompleto: true, ripsFaltantes: [], codigoEntidad: "EPS005", tipoDocumento: "CC", municipioResidencia: "11001" });

    // Pasar a particular borra la EPS
    const res = await api.put(`/api/v1/patients/${ok.id}`, { ...auth(token), data: { tipoUsuario: "Particular" } });
    expect(res.status()).toBeLessThan(300);
    expect((await get<{ codigoEntidad: string | null }>(api, token, `/api/v1/patients/${ok.id}`)).codigoEntidad).toBeNull();
  });

  test("el paciente que llegó por una valoración queda marcado RIPS incompleto", async ({ request: api }) => {
    const lista = await get<{ items: { id: string }[] }>(api, token, "/api/v1/patients?search=1000000010&page=1&pageSize=5");
    expect(lista.items.length, "Mónica Salazar, del seed de valoraciones").toBe(1);
    const ficha = await get<{ ripsCompleto: boolean | null; ripsFaltantes: string[] }>(api, token, `/api/v1/patients/${lista.items[0].id}`);
    expect(ficha.ripsCompleto).toBe(false);
    expect(ficha.ripsFaltantes).toContain("tipo de documento");
  });

  test("un procedimiento que se reporta en RIPS exige CUPS válido, finalidad y tipo de servicio", async ({ request: api }) => {
    const proc = { nombre: `Proc RIPS ${RUN}`, descripcion: "", precioBase: 100_000, duracionMinutos: 30, areaCorporal: "Rostro" };
    const sinDatos = await api.post("/api/v1/procedures", { ...auth(token), data: { ...proc, requiereRips: true } });
    expect(sinDatos.status()).toBe(400);
    expect(((await sinDatos.json()) as { error: string }).error).toContain("código CUPS");

    const cupsMalo = await api.post("/api/v1/procedures", { ...auth(token), data: { ...proc, requiereRips: true, codigoCups: "89 02", finalidadConsulta: "Tratamiento", tipoServicio: "Procedimiento" } });
    expect(cupsMalo.status()).toBe(400);

    const ok = await post<{ id: string; codigoCups: string; finalidadConsulta: string; tipoServicio: string; requiereRips: boolean }>(
      api, token, "/api/v1/procedures", { ...proc, requiereRips: true, codigoCups: "890201", finalidadConsulta: "ValoracionIntegral", tipoServicio: "Consulta" });
    expect(ok).toMatchObject({ requiereRips: true, codigoCups: "890201", finalidadConsulta: "ValoracionIntegral", tipoServicio: "Consulta" });
    // Un procedimiento que no se reporta no pide nada
    const libre = await post<{ id: string }>(api, token, "/api/v1/procedures", { ...proc, nombre: `Proc libre ${RUN}` });
    await api.delete(`/api/v1/procedures/${ok.id}`, auth(token));
    await api.delete(`/api/v1/procedures/${libre.id}`, auth(token));
  });

  test("la clínica IPS necesita su código REPS de 12 dígitos", async ({ request: api }) => {
    const settings = await get<{ esIps: boolean; codigoHabilitacionReps: string | null } & Record<string, unknown>>(api, token, "/api/v1/tenants/current/settings");
    expect(settings.esIps).toBe(true);
    expect(settings.codigoHabilitacionReps).toMatch(/^\d{12}$/);
    for (const codigo of [null, "12345"]) {
      const res = await api.put("/api/v1/tenants/current/settings", { ...auth(token), data: { ...settings, codigoHabilitacionReps: codigo } });
      expect(res.status(), `REPS ${codigo}`).toBe(400);
    }
    const res = await api.put("/api/v1/tenants/current/settings", { ...auth(token), data: settings });
    expect(res.status()).toBe(200);
  });
});

test.describe("Productos: categoría y costo de compra", () => {
  const CAT = `Cat ${RUN}`;
  const ids: string[] = [];

  test.afterAll(() => {
    if (ids.length === 0) return;
    const inList = ids.map((id) => `'${id}'`).join(",");
    sql(`DELETE FROM InventoryMovements WHERE ProductId IN (${inList});
         DELETE FROM InventoryEntries WHERE ProductId IN (${inList});
         DELETE FROM ProductLots WHERE ProductId IN (${inList});
         DELETE FROM Products WHERE Id IN (${inList});`);
  });

  test("la categoría no tiene variantes, se sugiere y filtra; el producto muestra el costo del lote más reciente", async ({ request: api }) => {
    const token = (await login(api, ADMIN_EMAIL, ADMIN_PASSWORD)).token;
    const base = { descripcion: "", tipoProducto: "InsumoCabina", unidadMedida: "unidad", stockMinimo: 0, tipoRegulatorio: "Insumo" };
    const a = await post<{ id: string; categoria: string }>(api, token, "/api/v1/products", { ...base, nombre: `Algodón ${RUN}`, categoria: `  ${CAT} ` });
    const b = await post<{ id: string; categoria: string }>(api, token, "/api/v1/products", { ...base, nombre: `Toallas ${RUN}`, categoria: CAT.toUpperCase() });
    const c = await post<{ id: string; categoria: string | null }>(api, token, "/api/v1/products", { ...base, nombre: `Sin cat ${RUN}` });
    ids.push(a.id, b.id, c.id);
    expect(a.categoria).toBe(CAT);
    expect(b.categoria).toBe(CAT); // misma categoría aunque venga en mayúsculas
    expect(c.categoria).toBeNull();

    expect(await get<string[]>(api, token, "/api/v1/products/categories")).toContain(CAT);
    const filtered = await get<{ items: { id: string }[] }>(api, token, `/api/v1/products?Page=1&PageSize=50&categoria=${encodeURIComponent(CAT)}`);
    expect(filtered.items.map((p) => p.id).sort()).toEqual([a.id, b.id].sort());
    const sin = await get<{ items: { id: string }[] }>(api, token, `/api/v1/products?Page=1&PageSize=50&categoria=sin&Search=${RUN}`);
    expect(sin.items.map((p) => p.id)).toEqual([c.id]);

    // Quitar la categoría con ""
    const put = await api.put(`/api/v1/products/${b.id}`, { ...auth(token), data: { categoria: "" } });
    expect(put.status()).toBe(204);
    expect((await get<{ categoria: string | null }>(api, token, `/api/v1/products/${b.id}`)).categoria).toBeNull();

    // Costo por lote: el producto muestra el del lote más reciente que lo tiene
    const entry = (fecha: string, cantidad: number, costoUnitario?: number) =>
      post(api, token, "/api/v1/inventory/entries", {
        productId: a.id, cantidad, motivoEntrada: "Compra", fechaEntrada: `${fecha}T09:00:00`, lote: { costoUnitario },
      });
    expect((await get<{ ultimoCosto: number | null }>(api, token, `/api/v1/products/${a.id}`)).ultimoCosto).toBeNull();
    await entry(enDias(-10), 50, 1_000);
    await entry(enDias(-2), 30, 1_200);
    await entry(enDias(-1), 10); // sin costo: no borra el último conocido
    expect((await get<{ ultimoCosto: number | null }>(api, token, `/api/v1/products/${a.id}`)).ultimoCosto).toBe(1_200);
    const lots = await get<{ cantidadInicial: number; costoUnitario: number | null }[]>(api, token, `/api/v1/products/${a.id}/lots`);
    expect(lots.map((l) => l.costoUnitario).sort()).toEqual([1_000, 1_200, null].sort());

    // Un costo negativo no entra
    const bad = await api.post("/api/v1/inventory/entries", { ...auth(token), data: { productId: a.id, cantidad: 1, motivoEntrada: "Compra", lote: { costoUnitario: -5 } } });
    expect(bad.status()).toBe(400);
  });
});

import { test, expect } from "@playwright/test";
import { startChapter, step, endChapter } from "../helpers/guide";
import { uiLogin, dialog, lastToast, headerLink, chooseCombobox } from "../walk";
import { apiLogin, CREDS } from "../api";
import { cleanupWalkthroughSales } from "../sales-cleanup";

test.describe.configure({ mode: "serial" });

// Un turno de recepción de principio a fin: abrir la caja, vender (paciente, producto y
// servicio, dos medios de pago), anular una venta de mostrador, registrar un egreso y cerrar
// la caja con el arqueo. Las ventas llevan la observación del recorrido: la limpieza las anula
// (el stock vuelve a su lote) y las borra, junto con la caja de recepción.
const OBS = "Venta del recorrido de manual";

test.afterAll(async () => {
  const sa = await apiLogin(CREDS.superadmin.email, CREDS.superadmin.password);
  await cleanupWalkthroughSales(sa.token);
});

test("Cap7 · Ventas y caja (recepción)", async ({ page }) => {
  startChapter(page, "admin", 7);
  await uiLogin(page, CREDS.admin.email, CREDS.admin.password);
  const d = () => dialog(page);
  const main = () => page.locator("main");
  const total = () => page.locator("[data-testid=sale-total]");

  await step(page, "Haz clic en Caja en el menú lateral", headerLink(page, "Caja"), {
    after: async () => { await expect(main()).toContainText("No tienes una caja abierta"); },
  });

  await step(page, "Presiona Abrir caja", main().locator("button", { hasText: "Abrir caja" }), {
    after: async () => { await expect(d()).toContainText("Base en efectivo"); },
  });

  await step(page, "Escribe la base con la que empiezas (100.000) y presiona Abrir caja", d().locator("button", { hasText: "Abrir caja" }), {
    before: async () => { await d().locator("#caja-base").fill("100000"); },
    after: async () => {
      await lastToast(page, /Caja abierta/);
      await expect(page.locator("[data-testid=efectivo-esperado]")).toContainText("100.000");
    },
  });

  await step(page, "Haz clic en Ventas: están las ventas del mes con su saldo", headerLink(page, "Ventas"), {
    after: async () => { await expect(main().locator("table tbody tr").first()).toBeVisible(); },
  });

  await step(page, "Presiona Nueva venta", main().locator("a", { hasText: "Nueva venta" }).first(), {
    after: async () => { await expect(page).toHaveURL(/\/sales\/new/); },
  });

  await step(page, "Haz clic en Buscar paciente y elige a Valentina Rodríguez", main().locator("button", { hasText: "Buscar paciente" }), {
    after: async () => { await chooseCombobox(page, "Valentina", "Valentina"); },
  });

  await step(page, "Busca \"protector\" y haz clic en el protector solar para agregarlo", main().locator("button[aria-label^='Agregar Protector']"), {
    before: async () => { await main().getByLabel("Buscar en el catálogo").fill("protector"); },
    after: async () => { await expect(main().locator("table")).toContainText("Protector solar"); },
  });

  await step(page, "Busca \"limpieza\" y agrega la limpieza facial: el total ya trae el IVA del protector", main().locator("button[aria-label^='Agregar Limpieza']"), {
    before: async () => { await main().getByLabel("Buscar en el catálogo").fill("limpieza"); },
    after: async () => {
      await expect(main().locator("table")).toContainText("Limpieza facial");
      await expect(total()).not.toHaveText("$0");
    },
  });

  await step(page, "Presiona Registrar pago: se propone el total en efectivo", main().locator("button", { hasText: "Registrar pago" }), {
    after: async () => { await expect(main().getByLabel("Monto en Efectivo")).toBeVisible(); },
  });

  await step(page, "Cambia el efectivo a 100.000 y presiona Agregar otro medio: el resto queda en tarjeta", main().locator("button", { hasText: "Agregar otro medio" }), {
    before: async () => { await main().getByLabel("Monto en Efectivo").fill("100000"); },
    after: async () => {
      await expect(main().getByLabel("Monto en Tarjeta")).toBeVisible();
      await main().getByLabel("Referencia del pago").fill("VCH-778812");
      await expect(main()).toContainText("Queda por cobrar");
    },
  });

  await step(page, "Presiona Confirmar venta: se descuenta el inventario y los pagos quedan en tu caja", main().locator("button", { hasText: "Confirmar venta" }), {
    before: async () => { await main().locator("#venta-obs").fill(OBS); },
    after: async () => {
      await lastToast(page, /Venta #\d+ confirmada/);
      await expect(page).toHaveURL(/\/sales\?saleId=/);
      await expect(d()).toContainText("Factura electrónica");
    },
  });

  await step(page, "El detalle muestra las líneas con su IVA, los dos pagos y que la factura se emitirá al conectar el proveedor", d().locator("button", { hasText: "Cerrar" }), {
    after: async () => { await expect(d()).toBeHidden(); },
  });

  await step(page, "Haz otra venta de mostrador: sin paciente, un protector pagado en efectivo", main().locator("button", { hasText: /^Confirmar venta/ }), {
    before: async () => {
      await page.goto("/sales/new");
      await main().locator("[role=radio]", { hasText: "Mostrador" }).click();
      await main().getByLabel("Buscar en el catálogo").fill("protector");
      await main().locator("button[aria-label^='Agregar Protector']").click();
      await expect(total()).not.toHaveText("$0");
      await main().locator("button", { hasText: "Registrar pago" }).click();
      await main().locator("#venta-obs").fill(OBS);
    },
    after: async () => { await lastToast(page, /confirmada/); await expect(d()).toBeVisible(); },
  });

  await step(page, "En su detalle presiona Anular", d().locator("button", { hasText: /^Anular$/ }), {
    after: async () => { await expect(page.locator("#motivo-anulacion")).toBeVisible(); },
  });

  await step(page, "Escribe el motivo y presiona Anular venta: el protector vuelve a su lote y el pago se anula", page.locator("[role=dialog] button", { hasText: "Anular venta" }), {
    before: async () => { await page.locator("#motivo-anulacion").fill("El cliente se arrepintió en la caja"); },
    after: async () => {
      await lastToast(page, /anulada/);
      await expect(d().first()).toContainText("Anulada");
    },
  });

  await step(page, "Vuelve a Caja: los pagos del día están ahí, el de la venta anulada tachado", headerLink(page, "Caja"), {
    before: async () => { await page.keyboard.press("Escape"); },
    after: async () => { await expect(main()).toContainText("Anulado"); },
  });

  await step(page, "Presiona Ingreso / egreso", main().locator("button", { hasText: "Ingreso / egreso" }), {
    after: async () => { await expect(d()).toContainText("Movimiento de caja"); },
  });

  await step(page, "Registra un egreso de 15.000 por la compra de café y presiona Registrar", d().locator("button", { hasText: "Registrar" }), {
    before: async () => {
      await d().locator("#mov-concepto").fill("Compra de café y agua");
      await d().locator("#mov-monto").fill("15000");
    },
    after: async () => { await lastToast(page, /Egreso registrado/); },
  });

  let esperado = "";
  await step(page, "Presiona Arqueo y cierre", main().locator("button", { hasText: "Arqueo y cierre" }), {
    before: async () => { esperado = (await page.locator("[data-testid=efectivo-esperado]").innerText()).replace(/\D/g, ""); },
    after: async () => { await expect(d()).toContainText("Esperado"); },
  });

  await step(page, "Cuenta el efectivo del cajón, escríbelo y presiona Cerrar caja: si coincide, el arqueo cuadra", d().locator("button", { hasText: "Cerrar caja" }), {
    before: async () => {
      await d().locator("#caja-contado").fill(esperado);
      await expect(d()).toContainText("Cuadra");
    },
    after: async () => {
      await lastToast(page, /Caja cerrada/);
      await expect(main()).toContainText("No tienes una caja abierta");
    },
  });

  await step(page, "En el historial la caja queda cerrada y cuadrada; Excel descarga el arqueo", main().locator("button", { hasText: /^Excel$/ }).first(), {
    click: false,
    after: async () => {
      await expect(main().locator("table")).toContainText("Cuadra");
      const [descarga] = await Promise.all([page.waitForEvent("download"), main().locator("button", { hasText: /^Excel$/ }).first().click()]);
      expect(descarga.suggestedFilename()).toMatch(/\.xlsx$/);
    },
  });

  endChapter();
});

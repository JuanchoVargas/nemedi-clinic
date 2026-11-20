import { test, expect } from "@playwright/test";
import { startChapter, step, endChapter } from "../helpers/guide";
import { uiLogin, headerLink, tab, activePanel } from "../walk";
import { CREDS } from "../api";

test.describe.configure({ mode: "serial" });

// Reportes del dueño: ventas del mes, producción del equipo y cartera, con descarga en Excel.
// Solo lectura: no deja datos.
test("Cap6 · Reportes y caja (dueño)", async ({ page }) => {
  startChapter(page, "superadmin", 6);
  await uiLogin(page, CREDS.superadmin.email, CREDS.superadmin.password);
  const main = () => page.locator("main");

  await step(page, "Haz clic en Reportes en el menú lateral: arranca en Ventas del mes", headerLink(page, "Reportes"), {
    after: async () => {
      await expect(page).toHaveURL(/\/reports/);
      await expect(main()).toContainText("Ventas confirmadas");
    },
  });

  await step(page, "Abre Procedimientos: lo realizado en el mes, de paquete o suelto", tab(page, "Procedimientos"), {
    after: async () => { await expect(activePanel(page)).toContainText("Realizadas"); },
  });

  await step(page, "Abre Producción: citas, horas de cabina y valor producido por esteticista", tab(page, "Producción"), {
    after: async () => { await expect(activePanel(page)).toContainText("Laura Pérez"); },
  });

  await step(page, "Abre Cartera: quién debe, cuánto y desde cuándo", tab(page, "Cartera"), {
    after: async () => { await expect(activePanel(page)).toContainText("Clientes con saldo"); },
  });

  await step(page, "Presiona Descargar Excel: el reporte se baja listo para el contador", main().locator("button", { hasText: "Descargar Excel" }), {
    click: false,
    after: async () => {
      const [descarga] = await Promise.all([page.waitForEvent("download"), main().locator("button", { hasText: "Descargar Excel" }).click()]);
      expect(descarga.suggestedFilename()).toMatch(/\.xlsx$/);
    },
  });

  await step(page, "Haz clic en Caja: tu caja de hoy está abierta y la de ayer cerró con un faltante", headerLink(page, "Caja"), {
    after: async () => {
      await expect(main()).toContainText("Abierta");
      await expect(main().locator("table").last()).toContainText("Faltan");
    },
  });

  endChapter();
});

// El mismo asistente con productos (proyecto "wizard"): Inventario → Importar → archivo con dos lotes del
// mismo producto → la vista previa cuenta productos (no filas) y avisa el detalle de lotes → importar.
// También entra a Procedimientos y Paquetes por su botón. Borra por SQL lo que crea.
import { test, expect, type Page } from "@playwright/test";
import { sql, lit } from "../sql";
import { xlsx } from "../xlsx";
import { CREDS } from "../ui/api";

const RUN = Date.now().toString().slice(-7);
const TAG = `E2E-${RUN}`;
const FILE = `e2e-wizard-productos-${RUN}.xlsx`;

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("input[name=email]").fill(CREDS.superadmin.email);
  await page.locator("input[name=password]").fill(CREDS.superadmin.password);
  await page.locator("form button[type=submit]").click();
  await page.waitForURL(/\/dashboard/);
}

test.afterAll(() => {
  const like = lit(`%${TAG}%`);
  sql(`DELETE FROM InventoryMovements WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
       DELETE FROM InventoryEntries WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
       DELETE FROM ProductLots WHERE ProductId IN (SELECT Id FROM Products WHERE Nombre LIKE ${like});
       DELETE FROM Products WHERE Nombre LIKE ${like};
       DELETE FROM ImportRuns WHERE ArchivoNombre = ${lit(FILE)};`);
});

test("productos: dos filas del mismo producto son un producto con dos lotes", async ({ page }) => {
  await login(page);
  await page.goto("/inventory");
  await page.getByRole("link", { name: "Importar" }).click();
  await expect(page).toHaveURL(/\/inventory\/import$/);
  await expect(page.getByRole("heading", { name: "Importar productos" })).toBeVisible();

  await page.getByTestId("import-file").setInputFiles({
    name: FILE,
    mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    buffer: xlsx([
      ["Ref", "Producto", "Valor", "Clasificación", "Lote", "Cant", "Vence"],
      [`W${RUN}`, `Bloqueador ${TAG}`, "65.000", "Cosmético", "B-1", 12, "06/2027"],
      [`W${RUN}`, `Bloqueador ${TAG}`, "", "", "B-2", 8, "12/2027"],
    ]),
  });
  await expect(page.getByTestId("map-row-F").getByRole("combobox")).toHaveText(/Cantidad/);
  await expect(page.getByTestId("map-row-G").getByRole("combobox")).toHaveText(/vencimiento/);

  await page.getByTestId("import-to-preview").click();
  await expect(page.getByTestId("import-summary")).toContainText("2 listas");
  await expect(page.getByTestId("import-detail")).toContainText("1 producto nuevo");
  await expect(page.getByTestId("import-detail")).toContainText("2 lotes con 20 unidades");
  await expect(page.getByTestId("import-run")).toHaveText(/Importar 1 producto$/);

  await page.getByTestId("import-run").click();
  await expect(page.getByTestId("import-result")).toContainText("1 producto nuevo");
  await expect(page.getByTestId("import-result-detail")).toContainText("2 lotes con 20 unidades");
  expect(sql(`SELECT COUNT(*) FROM Products WHERE Nombre = ${lit(`Bloqueador ${TAG}`)}`)[0][0]).toBe("1");

  await page.getByRole("link", { name: "Historial de importaciones" }).click();
  await expect(page.getByRole("row", { name: new RegExp(FILE) })).toContainText("Productos");
  await expect(page.getByRole("row", { name: new RegExp(FILE) })).toContainText("2 lotes con 20 unidades");
});

test("procedimientos y paquetes tienen su botón Importar", async ({ page }) => {
  await login(page);
  for (const [from, to, title] of [
    ["/procedures", /\/procedures\/import$/, "Importar procedimientos"],
    ["/packages", /\/packages\/import$/, "Importar paquetes"],
  ] as const) {
    await page.goto(from);
    await page.getByRole("link", { name: "Importar" }).click();
    await expect(page).toHaveURL(to);
    await expect(page.getByRole("heading", { name: title })).toBeVisible();
  }
});

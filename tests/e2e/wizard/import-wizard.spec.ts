// El asistente de importación en el navegador (proyecto "wizard": pnpm test:wizard).
//
//   · Pacientes → Importar → subir .xlsx (títulos en la fila 2) → mapeo automático → corregir una
//     columna → revisar (resumen, duplicado, error) → descargar errores → actualizar duplicados →
//     importar → resultado → historial.
//   · Cancelar en el mapeo y en la revisión no deja nada guardado.
// La clínica demo es IPS: el archivo trae los datos RIPS. Borra por SQL lo que crea.
import { test, expect, type Page } from "@playwright/test";
import { sql, scalar, lit } from "../sql";
import { xlsx } from "../xlsx";
import { apiLogin, CREDS, req } from "../ui/api";
import { RIPS_PACIENTE } from "../rips";

const RUN = Date.now().toString().slice(-7);
const doc = (n: number) => `5${RUN}${String(n).padStart(2, "0")}`;
const FILE = `e2e-wizard-${RUN}.xlsx`;

const RIPS = ["CC", "170", "11001", "U", "12"];
const rows = [
  ["Pacientes de la sede norte"],
  ["Nombre completo", "Documento", "Nacimiento", "Género", "Cel", "Tipo doc", "País", "Municipio DANE", "Zona", "Régimen", "Notas"],
  ["ANA LUCÍA TORRES", doc(1), "15/03/1990", "F", "300 111 2233", ...RIPS, "vip"],
  ["Camilo Ríos", doc(2), "02-11-1988", "M", "3001112234", ...RIPS, ""],
  ["Paula Duque", doc(3), "31/02/1990", "F", "3001112235", ...RIPS, ""], // fecha inexistente
  ["Existente Wizard", doc(9), "01/01/1980", "F", "3009999999", ...RIPS, ""], // ya existe
];

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("input[name=email]").fill(CREDS.superadmin.email);
  await page.locator("input[name=password]").fill(CREDS.superadmin.password);
  await page.locator("form button[type=submit]").click();
  await page.waitForURL(/\/dashboard/);
}

async function upload(page: Page) {
  await page.goto("/patients");
  await page.getByRole("link", { name: "Importar" }).click();
  await expect(page).toHaveURL(/\/patients\/import$/);
  await page.getByTestId("import-file").setInputFiles({
    name: FILE,
    mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    buffer: xlsx(rows),
  });
  await expect(page.locator("#header-row")).toBeVisible();
}

const patients = () => Number(scalar(`SELECT COUNT(*) FROM Patients WHERE Cedula LIKE ${lit(`5${RUN}%`)} AND IsDeleted = 0`));
const runs = () => Number(scalar(`SELECT COUNT(*) FROM ImportRuns WHERE ArchivoNombre = ${lit(FILE)}`));

test.describe("Asistente de importación de pacientes", () => {
  test.describe.configure({ mode: "serial" });

  test.beforeAll(async () => {
    const { token } = await apiLogin(CREDS.superadmin.email, CREDS.superadmin.password);
    const r = await req("POST", "/api/v1/patients", {
      token,
      body: { nombre: "Existente", apellido: "Wizard", cedula: doc(9), telefono: "3000000009", ...RIPS_PACIENTE },
    });
    expect(r.status, r.text).toBeLessThan(300);
  });

  test.afterAll(() => {
    const like = lit(`5${RUN}%`);
    sql(`DELETE FROM ClinicalRecords WHERE PatientId IN (SELECT Id FROM Patients WHERE Cedula LIKE ${like});
         DELETE FROM Patients WHERE Cedula LIKE ${like};
         DELETE FROM ImportRuns WHERE ArchivoNombre = ${lit(FILE)};`);
  });

  test("cancelar en cualquier paso no deja nada guardado", async ({ page }) => {
    await login(page);
    await upload(page);
    await page.getByTestId("import-cancel").click();
    await expect(page).toHaveURL(/\/patients$/);

    await upload(page);
    await page.getByTestId("import-to-preview").click();
    await expect(page.getByTestId("import-summary")).toBeVisible();
    await page.getByTestId("import-cancel").click();
    await expect(page).toHaveURL(/\/patients$/);

    expect(patients()).toBe(1); // solo el que ya existía
    expect(runs()).toBe(0);
  });

  test("subir, mapear, revisar e importar", async ({ page }) => {
    await login(page);
    await upload(page);

    // Paso 2: títulos en la fila 2, mapeo automático
    await expect(page.locator("#header-row")).toHaveValue("2");
    const fieldOf = (letter: string) => page.getByTestId(`map-row-${letter}`).getByRole("combobox");
    await expect(fieldOf("A")).toHaveText(/Nombre completo/);
    await expect(fieldOf("B")).toHaveText(/Número de documento/);
    await expect(fieldOf("C")).toHaveText(/Fecha de nacimiento/);
    await expect(fieldOf("D")).toHaveText(/Sexo/);
    await expect(fieldOf("E")).toHaveText(/Teléfono/);
    await expect(fieldOf("K")).toHaveText(/No importar/);

    // Corregir: quitar el teléfono y volverlo a poner (el campo se mueve, no se duplica)
    await fieldOf("E").click();
    await page.getByRole("option", { name: "No importar" }).click();
    await expect(fieldOf("E")).toHaveText(/No importar/);
    await fieldOf("E").click();
    await page.getByRole("option", { name: "Teléfono" }).click();
    await expect(fieldOf("E")).toHaveText(/Teléfono/);

    // Paso 3: revisar
    await page.getByTestId("import-to-preview").click();
    await expect(page.getByTestId("import-summary")).toContainText("2 listas, 1 con error, 1 duplicada");
    // Tres palabras: 1 nombre + 2 apellidos, con aviso para que la persona lo revise
    await expect(page.getByTestId("preview-row-3")).toContainText("se tomó «Ana» como nombre y «Lucía Torres» como apellido");
    await expect(page.getByTestId("preview-row-5")).toContainText("inexistente");
    await expect(page.getByTestId("preview-row-6")).toContainText("Ya existe: Existente Wizard");

    const download = page.waitForEvent("download");
    await page.getByTestId("import-download-errors").click();
    expect((await download).suggestedFilename()).toContain("errores");

    await expect(page.getByTestId("import-run")).toHaveText(/Importar 2 pacientes/);
    await page.getByRole("radio", { name: "Actualizar" }).click();
    await expect(page.getByTestId("import-run")).toHaveText(/Importar 3 pacientes/);
    expect(patients()).toBe(1); // aún nada

    // Paso 4: importar
    await page.getByTestId("import-run").click();
    await expect(page.getByTestId("import-result")).toContainText("2 pacientes nuevos, 1 actualizado");
    expect(patients()).toBe(3);
    expect(scalar(`SELECT Telefono FROM Patients WHERE Cedula = ${lit(doc(9))}`)).toBe("3009999999");
    expect(runs()).toBe(1);

    // Historial
    await page.getByRole("link", { name: "Historial de importaciones" }).click();
    await expect(page).toHaveURL(/\/imports$/);
    await expect(page.getByRole("row", { name: new RegExp(FILE) })).toBeVisible();
  });
});

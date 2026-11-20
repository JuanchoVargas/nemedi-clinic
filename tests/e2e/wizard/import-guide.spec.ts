// Capturas de la guía de importación de pacientes (pnpm test:wizard; quedan en docs/manual/img, sin versionar).
//
// Recorre el asistente como la dueña de la clínica, con un archivo realista: título arriba, títulos
// en la fila 2, columnas con nombres propios de la clínica, una fecha imposible y una paciente que
// ya existe. Cada paso queda en docs/manual/img/importacion/cap1-NN-*.png con el clic marcado
// (helpers/guide.ts, el mismo del manual). Los duplicados se OMITEN: los datos demo no cambian.
// Borra por SQL los pacientes y el registro del historial que crea.
import { test, expect } from "@playwright/test";
import { sql, lit } from "../sql";
import { xlsx } from "../xlsx";
import { CREDS } from "../ui/api";
import { endChapter, startChapter, step } from "../ui/helpers/guide";

const RUN = Date.now().toString().slice(-6);
const doc = (n: number) => `4${RUN}${String(n).padStart(3, "0")}`;
const FILE = "Pacientes Clinica Demo.xlsx";
const RIPS = ["CC", "170", "11001", "Urbana", "Particular"];

const rows = [
  ["Base de pacientes · Clínica Demo · septiembre 2026"],
  ["Nombre completo", "Cédula", "Fecha de nacimiento", "Género", "Celular", "Correo", "Domicilio", "Lugar", "Tipo doc", "País", "Municipio DANE", "Zona", "Régimen", "Observaciones"],
  ["CAROLINA MEJÍA ARANGO", doc(1), "14/02/1988", "F", "300 000 0201", "carolina.mejia@example.com", "Calle 1 # 1-01", "Bogotá", ...RIPS, "Prefiere cita en la mañana"],
  ["Daniela Ospina", doc(2), "3/7/1995", "F", "+57 300 000 0202", "daniela.ospina@example.com", "Calle 2 # 2-02", "Bogotá", ...RIPS, ""],
  ["Juliana Restrepo Gil", doc(3), "22-11-1990", "F", "3000000203", "", "Carrera 3 # 3-03", "Bogotá", ...RIPS, "Alergia al látex"],
  ["Santiago Herrera", doc(4), "1985-06-30", "M", "3000000204", "santiago.h@example.com", "", "Chía", ...RIPS, ""],
  ["Laura Valentina Cruz Peña", doc(5), "9 de mayo de 1999", "F", "3000000205", "", "Calle 4 # 4-04", "Bogotá", ...RIPS, ""],
  ["Paula Andrea Ríos", doc(6), "31/02/1992", "F", "3000000206", "paula.rios@example.com", "", "Bogotá", ...RIPS, "Revisar fecha"],
  ["Mariana Gómez", "1000000002", "12/08/1991", "F", "3001234567", "", "", "Bogotá", ...RIPS, "Ya es paciente"],
  ["Natalia Suárez", doc(8), "17/10/1987", "F", "3000000207", "natalia.suarez@example.com", "Carrera 5 # 5-05", "Bogotá", ...RIPS, ""],
];

test("guía de importación: capturas de cada paso", async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto("/login");
  await page.locator("input[name=email]").fill(CREDS.superadmin.email);
  await page.locator("input[name=password]").fill(CREDS.superadmin.password);
  await page.locator("form button[type=submit]").click();
  await page.waitForURL(/\/dashboard/);
  await page.goto("/patients");
  await page.waitForLoadState("networkidle");

  startChapter(page, "importacion", 1);
  const mapRow = (letter: string) => page.getByTestId(`map-row-${letter}`).getByRole("combobox");

  await step(page, "En Pacientes, haz clic en Importar", page.getByRole("link", { name: "Importar" }), {
    after: async () => { await expect(page).toHaveURL(/\/patients\/import$/); },
  });

  await step(page, "Sube el archivo", page.getByRole("button", { name: "Elegir archivo para importar" }), {
    click: false,
    after: async () => {
      await page.getByTestId("import-file").setInputFiles({
        name: FILE,
        mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        buffer: xlsx(rows),
      });
      await expect(page.locator("#header-row")).toHaveValue("2");
    },
  });

  await step(page, "Revisa el mapeo de columnas", page.getByRole("table"), { click: false });

  await step(page, "Revisa la fila de los títulos", page.locator("#header-row"), { click: false });

  // "Lugar" no se reconoce solo: la dueña la asigna a Ciudad
  await step(page, "Corrige una columna que no se reconoció", page.getByRole("option", { name: "Ciudad" }), {
    before: async () => {
      await mapRow("H").scrollIntoViewIfNeeded();
      await expect(mapRow("H")).toHaveText(/No importar/);
      await mapRow("H").click();
    },
    after: async () => { await expect(mapRow("H")).toHaveText(/Ciudad/); },
  });

  await step(page, "Haz clic en Revisar datos", page.getByTestId("import-to-preview"), {
    after: async () => { await expect(page.getByTestId("import-summary")).toBeVisible(); },
  });

  await step(page, "Lee el resumen", page.getByTestId("import-stats"), { click: false });

  await step(page, "Muestra solo las filas con problemas", page.getByTestId("import-only-problems"), {
    after: async () => { await expect(page.getByTestId("preview-row-3")).toBeVisible(); await page.waitForTimeout(500); },
  });

  await step(page, "Revisa las filas con error o duplicadas", page.getByTestId("preview-row-8"), {
    click: false,
    before: async () => { await page.getByTestId("preview-row-8").scrollIntoViewIfNeeded(); },
  });

  await step(page, "Descarga las filas con error", page.getByTestId("import-download-errors"), {
    before: async () => { await page.getByTestId("import-download-errors").scrollIntoViewIfNeeded(); },
    after: async () => { await page.waitForTimeout(800); },
  });

  await step(page, "Decide qué hacer con los duplicados", page.getByRole("radiogroup", { name: "Qué hacer con los duplicados" }), {
    click: false,
    before: async () => { await page.getByRole("radiogroup").scrollIntoViewIfNeeded(); },
  });

  await step(page, "Haz clic en Importar", page.getByTestId("import-run"), {
    before: async () => { await page.getByTestId("import-run").scrollIntoViewIfNeeded(); },
    after: async () => { await expect(page.getByTestId("import-result")).toBeVisible({ timeout: 30_000 }); },
  });

  await step(page, "Lee el resultado", page.getByTestId("import-result"), { click: false });

  await step(page, "Consulta el historial de importaciones", page.getByRole("link", { name: "Historial de importaciones" }), {
    after: async () => { await expect(page).toHaveURL(/\/imports$/); },
  });

  await step(page, "El historial guarda cada importación", page.getByRole("row", { name: new RegExp(FILE) }).first(), { click: false });

  const results = endChapter();
  expect(results.filter((r) => !r.ok).map((r) => `${r.n}. ${r.name}: ${r.error}`)).toEqual([]);
});

test.afterAll(() => {
  const like = lit(`4${RUN}%`);
  sql(`DELETE FROM ClinicalRecords WHERE PatientId IN (SELECT Id FROM Patients WHERE Cedula LIKE ${like});
       DELETE FROM Patients WHERE Cedula LIKE ${like};
       DELETE FROM ImportRuns WHERE ArchivoNombre = ${lit(FILE)};`);
});

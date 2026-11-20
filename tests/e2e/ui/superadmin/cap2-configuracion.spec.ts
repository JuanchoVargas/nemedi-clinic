import { test, expect } from "@playwright/test";
import { startChapter, step, endChapter } from "../helpers/guide";
import { uiLogin, pickSelect, dialog, closeDialog, headerMenu, openHeaderMenu, menuItem, lastToast, tab } from "../walk";
import { apiLogin, CREDS, listAll, safeDelete } from "../api";
import { tempPassword } from "../../credentials";

test.describe.configure({ mode: "serial" });

const SEDE = "Sede Chapinero";
const ADMIN = { nombre: "Mónica", apellido: "Herrera", email: "monica.herrera@nemedi.demo" };
const ESTE = { nombre: "Daniela", apellido: "Ospina", email: "daniela.ospina@nemedi.demo" };

test.afterAll(async () => {
  const sa = await apiLogin(CREDS.superadmin.email, CREDS.superadmin.password);
  const users = await listAll<{ id: string; email: string }>(sa.token, "/api/v1/users");
  for (const u of users.filter((x) => [ADMIN.email, ESTE.email].includes(x.email))) await safeDelete(sa.token, `/api/v1/users/${u.id}`);
  const branches = await listAll<{ id: string; nombre: string }>(sa.token, "/api/v1/branches");
  for (const b of branches.filter((x) => x.nombre === SEDE)) await safeDelete(sa.token, `/api/v1/branches/${b.id}`);
});

test("Cap2 · Configurar la clínica (dueño)", async ({ page }) => {
  startChapter(page, "superadmin", 2);
  await uiLogin(page, CREDS.superadmin.email, CREDS.superadmin.password);
  const d = () => dialog(page);
  const fillUser = async (u: { nombre: string; apellido: string; email: string }, rol: string | RegExp, sede: string) => {
    await d().locator("input[name=nombre]").fill(u.nombre);
    await d().locator("input[name=apellido]").fill(u.apellido);
    await d().locator("input[name=email]").fill(u.email);
    await d().locator("input[name=password]").fill(tempPassword());
    await pickSelect(d().locator("button[role=combobox]").nth(0), rol);
    await pickSelect(d().locator("button[role=combobox]").nth(1), sede);
  };

  await step(page, "Haz clic en Administración", headerMenu(page, "Administración"), {
    after: async () => { await expect(menuItem(page, "Configuración")).toBeVisible(); },
  });

  await step(page, "Elige Configuración: los datos de tu clínica, sus sedes, políticas e impuestos están en un solo lugar", menuItem(page, "Configuración"), {
    after: async () => {
      await expect(page).toHaveURL(/\/settings/);
      await expect(page.locator("main input[name=nombre]")).not.toHaveValue("");
    },
  });

  await step(page, "En Datos, revisa Prestador de servicios de salud: la clínica es IPS y su código de habilitación REPS queda aquí", page.locator("main input[name=codigoHabilitacionReps]"), {
    click: false,
    after: async () => {
      await expect(page.locator("main")).toContainText("IPS habilitada");
      await expect(page.locator("main input[name=codigoHabilitacionReps]")).toHaveValue(/^\d{12}$/);
    },
  });

  await step(page, "Abre la pestaña Sedes y horarios", tab(page, "Sedes y horarios"), {
    after: async () => { await expect(page.locator("main table")).toContainText("Sede Principal"); },
  });

  await step(page, "Haz clic en Nueva sede", page.locator("main button", { hasText: "Nueva sede" }), {
    after: async () => { await expect(d()).toBeVisible(); },
  });

  await step(page, `Escribe los datos de la sede y presiona Crear`, d().locator("button[form=branch-form]"), {
    before: async () => {
      await d().locator("input[name=nombre]").fill(SEDE);
      await d().locator("input[name=direccion]").fill("Calle 63 # 11-40");
      await d().locator("input[name=telefono]").fill("6017001234");
    },
    after: async () => { await expect(page.locator("main table")).toContainText(SEDE); },
  });

  await step(page, `Haz clic en Horario de ${SEDE}: enciende los días que atiende y ajusta las franjas`, page.locator(`button[aria-label="Horario de ${SEDE}"]`), {
    after: async () => { await expect(d()).toContainText("Horario de"); },
  });

  await step(page, "Deja lunes a viernes de 8:00 a 18:00 y presiona Guardar horario", d().locator("button", { hasText: "Guardar horario" }), {
    before: async () => {
      for (const dia of ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes"])
        await d().locator(`[role=switch][aria-label="Abierto el ${dia}"]`).click();
    },
    after: async () => { await lastToast(page, /Horario guardado/); await expect(d()).toBeHidden(); },
  });

  await step(page, "Abre Administración y elige Usuarios", menuItem(page, "Usuarios"), {
    before: async () => { await openHeaderMenu(page, "Administración"); },
    after: async () => {
      await expect(page).toHaveURL(/\/admin\/users/);
      await expect(page.locator("main table")).toContainText("laura.perez@nemedi.demo");
    },
  });

  await step(page, "Haz clic en Nuevo usuario", page.locator("main button", { hasText: "Nuevo usuario" }), {
    after: async () => { await expect(d()).toBeVisible(); },
  });

  await step(page, "Completa los datos de Mónica con rol Admin y Sede Principal, y presiona Crear", d().locator("button[form=user-form]"), {
    before: async () => { await fillUser(ADMIN, /^Admin$/, "Sede Principal"); },
    after: async () => { await expect(page.locator("main table")).toContainText(ADMIN.email); },
  });

  await step(page, `Crea a Daniela con rol Esteticista en ${SEDE} y presiona Crear`, d().locator("button[form=user-form]"), {
    before: async () => {
      await page.locator("main button", { hasText: "Nuevo usuario" }).click();
      await fillUser(ESTE, "Esteticista", SEDE);
    },
    after: async () => { await expect(page.locator("main table")).toContainText(ESTE.email); },
  });

  await step(page, "Haz clic en el lápiz de Daniela para editarla", page.locator(`button[aria-label="Editar ${ESTE.nombre}"]`), {
    after: async () => { await expect(d()).toBeVisible(); },
  });

  await step(page, "Cambia el apellido y presiona Guardar cambios", d().locator("button[form=user-form]"), {
    before: async () => { await d().locator("input[name=apellido]").fill("Ospina Ríos"); },
    after: async () => {
      await expect(page.locator("main table")).toContainText("Ospina Ríos");
      await lastToast(page, /actualizado/i);
    },
  });

  await step(page, "Vuelve a editar y apaga el interruptor Activo para desactivarla", d().locator("[role=switch]"), {
    before: async () => { await page.locator(`button[aria-label="Editar ${ESTE.nombre}"]`).click(); },
    after: async () => {
      await d().locator("button[form=user-form]").click();
      await expect(page.locator("main table tr", { hasText: ESTE.email })).toContainText("Inactivo");
    },
  });

  await step(page, "Vuelve a Configuración y abre Políticas: anticipación mínima para agendar y consentimiento obligatorio", tab(page, "Políticas"), {
    before: async () => {
      await closeDialog(page);
      await openHeaderMenu(page, "Administración");
      await menuItem(page, "Configuración").click();
      await expect(page).toHaveURL(/\/settings/);
    },
    after: async () => { await expect(page.locator("main")).toContainText("Exigir consentimiento informado"); },
  });

  await step(page, "Abre Impuestos: el IVA por defecto de productos y de servicios", tab(page, "Impuestos"), {
    after: async () => { await expect(page.locator("main")).toContainText("IVA de productos"); },
  });

  endChapter();
});

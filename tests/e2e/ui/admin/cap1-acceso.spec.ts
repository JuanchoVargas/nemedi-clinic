import { test, expect } from "@playwright/test";
import { startChapter, step, endChapter } from "../helpers/guide";
import { headerMenu, openHeaderMenu, menuItem, sidebar, tab } from "../walk";
import { CREDS } from "../api";

test.describe.configure({ mode: "serial" });

test("Cap1 · Entrar y qué puedes hacer (recepción)", async ({ page }) => {
  startChapter(page, "admin", 1);

  await step(page, "Escribe tu correo y contraseña y presiona Ingresar", page.locator("form button[type=submit]"), {
    before: async () => {
      await page.goto("/login");
      await page.locator("input[name=email]").fill(CREDS.admin.email);
      await page.locator("input[name=password]").fill(CREDS.admin.password);
    },
    after: async () => {
      await page.waitForURL(/\/dashboard/, { timeout: 15_000 });
      await expect(sidebar(page)).toContainText(CREDS.admin.email);
    },
  });

  await step(page, "Haz clic en Administración: verás Usuarios y Configuración", headerMenu(page, "Administración"), {
    after: async () => {
      const items = page.locator("[data-sidebar=menu-sub-button]");
      await expect(items.filter({ hasText: "Usuarios" })).toHaveCount(1);
      await expect(items.filter({ hasText: "Configuración" })).toHaveCount(1);
    },
  });

  await step(page, "Elige Configuración: para recepción solo están las sedes y sus horarios (sin Nueva sede)", menuItem(page, "Configuración"), {
    after: async () => {
      await expect(page).toHaveURL(/\/settings/);
      await expect(tab(page, "Sedes y horarios")).toBeVisible();
      await expect(tab(page, "Datos")).toHaveCount(0);
      await expect(page.locator("main table")).toContainText("Sede Principal");
      await expect(page.locator("main button", { hasText: "Nueva sede" })).toHaveCount(0);
      await expect(page.locator('main button[aria-label^="Horario de"]').first()).toBeVisible();
    },
  });

  await step(page, "Ventas, Caja y Reportes están en Finanzas", null, {
    after: async () => {
      for (const item of ["Ventas", "Caja", "Reportes"])
        await expect(page.locator("[data-sidebar=menu-button][href]", { hasText: item }).first()).toBeVisible();
    },
  });

  await step(page, "Abre Administración y elige Usuarios: puedes dar de alta esteticistas", menuItem(page, "Usuarios"), {
    before: async () => { await openHeaderMenu(page, "Administración"); },
    after: async () => {
      await expect(page).toHaveURL(/\/admin\/users/);
      await expect(page.locator("main button", { hasText: "Nuevo usuario" })).toBeVisible();
      await expect(page.locator('main button[aria-label^="Editar"]').first()).toBeVisible();
    },
  });

  await step(page, "Presiona Nuevo usuario: solo puedes crear esteticistas", page.locator("main button", { hasText: "Nuevo usuario" }), {
    after: async () => {
      const d = page.locator("[role=dialog]");
      await expect(d).toContainText("Solo el dueño puede crear usuarios de recepción");
      await page.keyboard.press("Escape");
    },
  });

  await step(page, "Si escribes la dirección de los datos de la clínica, solo ves las sedes: los datos, las políticas y el IVA son del dueño", null, {
    before: async () => { await page.goto("/settings?tab=datos"); },
    after: async () => {
      await expect(tab(page, "Sedes y horarios")).toBeVisible();
      await expect(page.locator("main input[name=nit]")).toHaveCount(0);
    },
  });

  endChapter();
});

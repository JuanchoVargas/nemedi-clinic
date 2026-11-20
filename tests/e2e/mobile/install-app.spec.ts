// "Instalar la app" (PWA): la tarjeta del inicio y el menú de usuario, según el dispositivo.
//
// El dispositivo sale del User-Agent (apps/web/src/lib/device.ts), no del ancho: cada caso fija el
// suyo. En `vite dev` no hay service worker, así que Chrome nunca dispara `beforeinstallprompt`:
// offerInstall() lo simula con un evento que cuenta las llamadas a prompt() y trae la elección.
import { test, expect, devices, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { CREDS } from "../ui/api";

// Las tarjetas de Android y de iPhone van al capítulo "Instalar la aplicación" del manual
const SHOTS = "docs/manual/img/mobile";
mkdirSync(SHOTS, { recursive: true });

// Solo lo que describe el aparato: `defaultBrowserType` no se puede cambiar dentro de un archivo
const IPHONE = { userAgent: devices["iPhone 13"].userAgent, viewport: devices["iPhone 13"].viewport, deviceScaleFactor: 3, isMobile: true, hasTouch: true };
const ANDROID = { userAgent: devices["Pixel 7"].userAgent, viewport: devices["Pixel 7"].viewport, deviceScaleFactor: 2.625, isMobile: true, hasTouch: true };

async function offerInstall(page: Page, outcome: "accepted" | "dismissed"): Promise<boolean> {
  return page.evaluate((choice) => {
    const w = window as unknown as { __prompts: number };
    w.__prompts = 0;
    const event = Object.assign(new Event("beforeinstallprompt", { cancelable: true }), {
      prompt: () => {
        w.__prompts += 1;
        return Promise.resolve();
      },
      userChoice: Promise.resolve({ outcome: choice, platform: "web" }),
    });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  }, outcome);
}

const prompts = (page: Page) => page.evaluate(() => (window as unknown as { __prompts?: number }).__prompts ?? 0);
const installCard = (page: Page) => page.locator('[data-slot="card"]', { hasText: "Instalar la app" });
const installedNotice = (page: Page) => page.getByRole("status").filter({ hasText: "Tienes la app" });

async function expectFitsScreen(page: Page) {
  const fits = await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth);
  expect(fits, "sin scroll horizontal").toBe(true);
}

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("input[name=email]").fill(CREDS.esteticista.email);
  await page.locator("input[name=password]").fill(CREDS.esteticista.password);
  await page.locator("form button[type=submit]").click();
  await page.waitForURL((url) => !url.pathname.startsWith("/login"));
}

test.describe("iPhone", () => {
  test.use(IPHONE);

  test("inicio: instrucciones (no un botón), y 'Ahora no' la oculta el resto de la sesión", async ({ page, context }) => {
    await page.goto("/");
    const card = installCard(page);
    await expect(card).toBeVisible();
    await expect(card).toContainText("Compartir");
    await expect(card).toContainText("Agregar a inicio");
    await expect(card.getByRole("button", { name: "Instalar la app" })).toHaveCount(0);
    await expectFitsScreen(page);
    await card.screenshot({ path: `${SHOTS}/instalar-iphone.png` });

    await card.getByRole("button", { name: "Ahora no" }).click();
    await expect(card).toHaveCount(0);
    await page.reload();
    await expect(page.getByText("Pacientes").first()).toBeVisible();
    await expect(installCard(page)).toHaveCount(0);

    // Otra pestaña es otra sesión: se ofrece de nuevo
    const other = await context.newPage();
    await other.goto("/");
    await expect(installCard(other)).toBeVisible();
  });

  test("'Ya la agregué': no se vuelve a ofrecer y, en otra sesión, avisa que la abran desde el inicio", async ({ page, context }) => {
    await page.goto("/");
    const card = installCard(page);
    await card.getByRole("button", { name: "Ya la agregué" }).click();
    await expect(card).toHaveCount(0);
    await expect(installedNotice(page), "recién la agregó: sin aviso en esta sesión").toHaveCount(0);

    const other = await context.newPage();
    await other.goto("/");
    await expect(other.getByText("Pacientes").first()).toBeVisible();
    await expect(installCard(other)).toHaveCount(0);
    const notice = installedNotice(other);
    await expect(notice).toContainText("NemediClinic");
    await expect(notice).toContainText("pantalla de inicio");
    await expectFitsScreen(other);
    await notice.screenshot({ path: `${SHOTS}/instalada-aviso.png` });
    await notice.getByRole("button", { name: "Cerrar aviso" }).click();
    await expect(installedNotice(other)).toHaveCount(0);
  });

  test("ya instalada (display-mode: standalone): no se ofrece", async ({ page }) => {
    await page.addInitScript(() => {
      const original = window.matchMedia.bind(window);
      window.matchMedia = (query: string) =>
        query.includes("display-mode: standalone") ? ({ ...original(query), matches: true, media: query } as MediaQueryList) : original(query);
    });
    await page.goto("/");
    await expect(page.getByText("Pacientes").first()).toBeVisible();
    await expect(installCard(page)).toHaveCount(0);
  });

  test("menú de usuario: el texto con el camino y 'Ahora no'", async ({ page }) => {
    await login(page);
    await page.getByRole("button", { name: "Menú de usuario" }).click();
    const menu = page.getByRole("menu");
    await expect(menu).toContainText("Instalar la app");
    await expect(menu).toContainText("Agregar a inicio");
    await expect(menu.getByRole("menuitem", { name: "Instalar la app" })).toHaveCount(0);
    await page.screenshot({ path: test.info().outputPath("iphone-menu.png") });

    await menu.getByRole("menuitem", { name: "Ahora no" }).click();
    await page.getByRole("button", { name: "Menú de usuario" }).click();
    await expect(page.getByRole("menu")).not.toContainText("Agregar a inicio");
  });
});

test.describe("Android", () => {
  test.use(ANDROID);

  test("inicio: el botón abre el diálogo del navegador; cancelarlo la oculta en la sesión", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Pacientes").first()).toBeVisible();
    await expect(installCard(page)).toHaveCount(0); // sin el evento el navegador no la ofrece

    expect(await offerInstall(page, "dismissed"), "preventDefault: sin el aviso propio del navegador").toBe(true);
    const card = installCard(page);
    await expect(card).toBeVisible();
    await expect(card).not.toContainText("Compartir");
    await expectFitsScreen(page);
    await card.screenshot({ path: `${SHOTS}/instalar-android.png` });

    await card.getByRole("button", { name: "Instalar la app" }).click();
    await expect(card).toHaveCount(0);
    expect(await prompts(page)).toBe(1);

    // El navegador vuelve a ofrecerla, pero la persona ya dijo que no en esta sesión
    await offerInstall(page, "dismissed");
    await page.reload();
    await offerInstall(page, "dismissed");
    await expect(page.getByText("Pacientes").first()).toBeVisible();
    await expect(installCard(page)).toHaveCount(0);
  });

  test("aceptada: appinstalled confirma con el ícono y el nombre, y la retira", async ({ page, context }) => {
    await page.goto("/");
    await offerInstall(page, "accepted");
    await installCard(page).getByRole("button", { name: "Instalar la app" }).click();
    await page.evaluate(() => window.dispatchEvent(new Event("appinstalled")));
    const dialog = page.getByRole("dialog");
    await expect(dialog).toContainText("Listo, NemediClinic ya está instalada");
    await expect(dialog).toContainText("pantalla de inicio");
    await expect(dialog.locator('img[src="/icons/icon-192.png"]')).toBeVisible();
    await expectFitsScreen(page);
    await page.screenshot({ path: `${SHOTS}/instalada-confirmacion.png` });
    await dialog.getByRole("button", { name: "Entendido" }).click();
    await expect(dialog).toHaveCount(0);
    await expect(installCard(page)).toHaveCount(0);
    await expect(installedNotice(page), "en la misma sesión no hace falta el aviso").toHaveCount(0);

    // Otra sesión en el navegador: la app está instalada → aviso para abrirla desde el inicio
    const other = await context.newPage();
    await other.goto("/");
    await expect(installedNotice(other)).toBeVisible();

    // Si Chrome la vuelve a ofrecer es que la desinstalaron: se ofrece de nuevo y sin aviso
    await offerInstall(other, "accepted");
    await expect(installCard(other)).toBeVisible();
    await expect(installedNotice(other)).toHaveCount(0);
  });

  test("menú de usuario: 'Instalar la app' abre el diálogo del navegador", async ({ page }) => {
    await login(page);
    await offerInstall(page, "dismissed");
    await page.getByRole("button", { name: "Menú de usuario" }).click();
    await page.screenshot({ path: test.info().outputPath("android-menu.png") });
    await page.getByRole("menuitem", { name: "Instalar la app" }).click();
    expect(await prompts(page)).toBe(1);
  });
});

test("computador: nunca se ofrece, aunque el navegador lo permita", async ({ page }) => {
  // El proyecto mobile solo achica la ventana: el User-Agent es el de Chrome de escritorio
  await page.goto("/");
  await offerInstall(page, "dismissed");
  await expect(page.getByText("Pacientes").first()).toBeVisible();
  await expect(installCard(page)).toHaveCount(0);
});

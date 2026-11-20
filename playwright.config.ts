import { defineConfig, devices } from "@playwright/test";

// Tests e2e de la raíz.
//   - api:          pnpm test (aislamiento multi-tenant y reglas de negocio), solo APIRequestContext.
//   - setup + superadmin → esteticista → admin: recorrido de UI por rol con
//     capturas para docs/manual (orden forzado con `dependencies`).
//   - mobile:       cero scroll horizontal a 390×844 en toda ruta y rol (pnpm test:mobile).
//   - wizard:       el asistente de importación de pacientes en el navegador (pnpm test:wizard).
// Los servidores se levantan solos si no hay nada escuchando (API 5055, Vite 5173).
//
// URL única, como en producción (clinic.example.com con el API en /api): la web llama a /api en su
// mismo origen y Vite lo reenvía al API (proxy en apps/web/vite.config.ts). E2E_URL apunta todo
// (recorrido de UI y tests de API) a una sola URL, p. ej. E2E_URL=https://clinic.example.com; sin
// ella, los tests de API van directo a :5055 y el recorrido a :5173.
const SINGLE_URL = process.env.E2E_URL?.replace(/\/+$/, "");
const API_URL = process.env.E2E_API_URL ?? SINGLE_URL ?? "http://localhost:5055";
const WEB_URL = process.env.E2E_WEB_URL ?? SINGLE_URL ?? "http://localhost:5173";

const uiUse = {
  ...devices["Desktop Chrome"],
  baseURL: WEB_URL,
  viewport: { width: 1440, height: 900 },
  locale: "es-CO",
  timezoneId: "America/Bogota",
  // Un paso que no encuentra su elemento falla en 15 s y el capítulo sigue con el siguiente paso.
  actionTimeout: 15_000,
  navigationTimeout: 30_000,
};

export default defineConfig({
  testDir: "./tests/e2e",
  // Cada capítulo de UI encadena hasta 12 pasos con captura; el gate de API usa mucho menos.
  timeout: 600_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  projects: [
    {
      name: "api",
      testMatch: /(tenant-isolation|business-rules|sales-cash|clinic-rules|jobs|clock|iis-app-path|storage|imports|imports-catalog|user-security|auth-rate-limit)\.spec\.ts/,
      // Sin Content-Type global: Playwright pone application/json cuando `data` es un objeto y
      // multipart/form-data (con su boundary) cuando se usa `multipart` — p. ej. al subir imágenes.
      use: { baseURL: API_URL },
    },
    // Facturación electrónica contra el sandbox de Factus (pnpm test:factus). Fuera del gate: depende
    // de un servicio externo y de las credenciales Factus__* del sandbox (variables de entorno).
    { name: "factus", testMatch: /invoicing\.spec\.ts/, use: { baseURL: API_URL } },
    // Asistente de importación en el navegador (pnpm test:wizard): subir, mapear, revisar, importar y cancelar.
    { name: "wizard", testMatch: /wizard\/.*\.spec\.ts/, use: uiUse },
    { name: "setup", testMatch: /ui\/global\.setup\.ts/, use: uiUse },
    { name: "superadmin", testMatch: /ui\/superadmin\/.*\.spec\.ts/, dependencies: ["setup"], use: uiUse },
    { name: "esteticista", testMatch: /ui\/esteticista\/.*\.spec\.ts/, dependencies: ["superadmin"], use: uiUse },
    { name: "admin", testMatch: /ui\/admin\/.*\.spec\.ts/, dependencies: ["esteticista"], use: uiUse },
    // Responsive: 390×844 táctil. Falla si alguna ruta tiene scroll horizontal; capturas en docs/manual/img/mobile.
    {
      name: "mobile",
      testMatch: /mobile\/.*\.spec\.ts/,
      use: { ...uiUse, viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 },
    },
  ],
  webServer: [
    {
      command: "dotnet run --project apps/api/src/NemediClinic.Api --urls " + API_URL,
      url: `${API_URL}/api/health`,
      reuseExistingServer: true,
      timeout: 180_000,
      stdout: "ignore",
      stderr: "pipe",
    },
    {
      // VITE_HIDE_DEVTOOLS: sin el badge de TanStack, que si no sale en todas las capturas del manual
      command: "pnpm exec vite --port 5173 --strictPort",
      cwd: "apps/web",
      env: { VITE_HIDE_DEVTOOLS: "1" },
      url: WEB_URL,
      reuseExistingServer: true,
      timeout: 120_000,
      stdout: "ignore",
      stderr: "pipe",
    },
  ],
});

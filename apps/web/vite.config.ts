import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";
import path from "path";

// Rutas del API cuyos GET se guardan para consultarlos sin conexión (24 h):
// hoja del día y citas, y la ficha del paciente (datos, historia clínica, evolución).
// NetworkFirst: con red siempre gana la respuesta fresca; la copia solo se usa si la red falla.
// /api/v1/files NO entra: son URLs firmadas que expiran en 10 minutos.
// OJO: workbox copia cada urlPattern al sw.js con toString(), así que la función no puede usar
// variables de este archivo: la expresión regular va escrita dentro.
// El mismo nombre está en src/lib/offline-cache.ts, que la borra al cerrar sesión.
const OFFLINE_CACHE = "clinic-api";

export default defineConfig(({ mode }) => {
  // El mismo nombre que la marca por defecto de la web (DEFAULT_BRANDING en src/api/branding.api.ts)
  const appName = loadEnv(mode, process.cwd(), "VITE_").VITE_APP_NAME || "NemediClinic";

  return {
    plugins: [
      react(),
      tailwindcss(),
      VitePWA({
        registerType: "autoUpdate",
        // Manifest de respaldo, estático y con la marca por defecto: sin él, instalar la app dependería
        // del API. El build lo enlaza en index.html; cuando el API contesta el branding,
        // use-apply-branding.ts cambia el <link rel="manifest"> por el del canal del dominio
        // (GET /api/v1/branding/manifest.webmanifest). Mismos campos y el mismo start_url que el del
        // API (BrandingController): para Chrome son la misma app.
        manifest: {
          name: appName,
          // Lo que se ve bajo el ícono (~12 caracteres): "Nemedi Clinic" → "NemediClinic"; si no cabe,
          // la primera palabra. Misma regla que src/lib/app-short-name.ts y BrandingController.
          short_name: appName.replace(/\s+/g, "").length <= 12 ? appName.replace(/\s+/g, "") : appName.trim().split(/\s+/)[0],
          description: "Agenda, pacientes e historia clínica de la clínica.",
          lang: "es",
          start_url: "/dashboard",
          scope: "/",
          display: "standalone",
          orientation: "any",
          theme_color: "#1F4E79",
          background_color: "#F7F6F3",
          icons: [
            { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
            { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
            { src: "/icons/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
          ],
        },
        includeAssets: ["favicon.svg", "icons/*.png"],
        workbox: {
          // Toda navegación recibe la app (las rutas las resuelve TanStack Router), salvo /api y las
          // rutas cuyo último segmento tiene extensión: /sw.js, /manifest.webmanifest, /icons/… son
          // archivos y van al servidor. Sin esa excepción, abrir /sw.js en un navegador con el service
          // worker activo mostraba la app y parecía que IIS servía HTML en vez del archivo. Workbox
          // compara contra pathname + search; ninguna ruta de la SPA lleva punto en el último segmento.
          navigateFallback: "/index.html",
          navigateFallbackDenylist: [/^\/api\//, /^[^?]*\/[^/?]*\.[^/?]*(\?.*)?$/],
          runtimeCaching: [
            {
              urlPattern: ({ url, request }) => request.method === "GET" && /^\/api\/v1\/(appointments|patients)(\/|$)/i.test(url.pathname),
              handler: "NetworkFirst",
              options: {
                cacheName: OFFLINE_CACHE,
                networkTimeoutSeconds: 5,
                expiration: { maxEntries: 200, maxAgeSeconds: 24 * 60 * 60 },
                cacheableResponse: { statuses: [200] },
              },
            },
            {
              // Tipografías de Google Fonts: cambian muy poco
              urlPattern: ({ url }) => url.origin === "https://fonts.googleapis.com" || url.origin === "https://fonts.gstatic.com",
              handler: "StaleWhileRevalidate",
              options: { cacheName: "fonts", expiration: { maxEntries: 20, maxAgeSeconds: 365 * 24 * 60 * 60 } },
            },
          ],
        },
        // Sin service worker en `vite dev`: cachear durante el desarrollo solo confunde.
        devOptions: { enabled: false },
      }),
    ],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src"),
      },
    },
    server: {
      port: 5173,
      host: true,
      // Misma forma que producción (clinic.example.com y clinic.example.com/api): una sola URL, sin
      // CORS. El Host se conserva (changeOrigin: false) para el branding por dominio.
      proxy: {
        "/api": { target: "http://localhost:5055", changeOrigin: false },
      },
    },
    preview: {
      proxy: {
        "/api": { target: "http://localhost:5055", changeOrigin: false },
      },
    },
  };
});

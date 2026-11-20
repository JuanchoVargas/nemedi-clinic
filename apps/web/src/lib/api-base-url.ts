// ============================================================
// api-base-url.ts — Origen del API
//
// Todas las llamadas ya llevan su ruta completa ("/api/v1/…"): API_BASE_URL es solo el ORIGEN
// que se antepone. Vacío = mismo origen, que es el caso normal en todos los entornos:
//   · IIS: clinic.example.com sirve la web y la aplicación /api es el API.
//   · Docker: Caddy manda /api/* al API.
//   · Desarrollo: Vite reenvía /api al API de :5055 (proxy en vite.config.ts).
// VITE_API_URL acepta "/", "/api" o vacío para eso mismo ("/api" se normaliza para no pedir
// /api/api/v1/…), o un origen completo ("https://otro-dominio") si el API vive en otro sitio.
// Mismo origen es también lo que permite el branding por dominio: el backend resuelve la marca
// por el Host. Aparte de axios.ts para que la renovación de sesión la use sin importar el cliente.
// ============================================================
const configuredUrl = (import.meta.env.VITE_API_URL ?? "").trim();

export const API_BASE_URL = configuredUrl.replace(/\/+$/, "").replace(/^\/api$/, "").replace(/\/api$/, "");

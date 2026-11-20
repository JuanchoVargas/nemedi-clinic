// Credenciales del admin de plataforma, sin escribirlas en las pruebas: las mismas de credentials.ts
// (E2E_PLATFORM_EMAIL / E2E_PLATFORM_PASSWORD, o Platform__AdminEmail / Platform__AdminPassword,
// con las que el API lo siembra al arrancar).
import { PLATFORM } from "./credentials";

export const PLATFORM_EMAIL = PLATFORM.email;
export const PLATFORM_PASSWORD = PLATFORM.password;

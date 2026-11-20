// Credenciales de las pruebas e2e. Ninguna contraseña vive en el repositorio:
//   · SuperAdmin del tenant demo: E2E_SUPERADMIN_EMAIL (por defecto admin@nemedi.demo) y
//     E2E_SUPERADMIN_PASSWORD, o Demo__Password, la misma variable con la que el API
//     (POST /api/v1/dev/seed-demo) deja la clave del SuperAdmin y de los usuarios demo.
//   · Usuarios demo (recepción y esteticistas): E2E_DEMO_PASSWORD, o la del SuperAdmin.
//   · PlatformAdmin: E2E_PLATFORM_EMAIL / E2E_PLATFORM_PASSWORD, o Platform__AdminEmail /
//     Platform__AdminPassword, las mismas con las que el API lo siembra al arrancar.
//   · Jobs: E2E_JOBS_KEY, o Jobs__ApiKey.
// Si el API lo levanta Playwright (webServer), hereda estas variables del mismo entorno.
// Los usuarios que crea cada prueba reciben una contraseña aleatoria (tempPassword).
import { randomBytes } from "node:crypto";

function env(...names: string[]): string | undefined {
  for (const name of names) {
    const value = process.env[name];
    if (value) return value;
  }
  return undefined;
}

function required(...names: string[]): string {
  const value = env(...names);
  if (!value) throw new Error(`Falta la variable de entorno ${names.join(" o ")} (ver README, "Pruebas end-to-end").`);
  return value;
}

export const SUPERADMIN = {
  get email() {
    return env("E2E_SUPERADMIN_EMAIL") ?? "admin@nemedi.demo";
  },
  get password() {
    return required("E2E_SUPERADMIN_PASSWORD", "Demo__Password");
  },
};

export const PLATFORM = {
  get email() {
    return env("E2E_PLATFORM_EMAIL", "Platform__AdminEmail") ?? "platform@example.com";
  },
  get password() {
    return required("E2E_PLATFORM_PASSWORD", "Platform__AdminPassword");
  },
};

/** Clave de los usuarios del seed demo (recepción y esteticistas). */
export const demoPassword = () => required("E2E_DEMO_PASSWORD", "Demo__Password", "E2E_SUPERADMIN_PASSWORD");

/** Usuarios que crea el seed demo (laura, camila) y el setup del recorrido (recepción). */
export const DEMO_EMAIL = {
  recepcion: "recepcion@nemedi.demo",
  laura: "laura.perez@nemedi.demo",
  camila: "camila.ruiz@nemedi.demo",
} as const;

/** Clave del API para POST /api/v1/jobs/{nombre}. */
export const jobsKey = () => required("E2E_JOBS_KEY", "Jobs__ApiKey");

/** Contraseña aleatoria que cumple la política del API (8+ caracteres, letras y números). */
export const tempPassword = () => `E2e-${randomBytes(9).toString("base64url")}-7x`;

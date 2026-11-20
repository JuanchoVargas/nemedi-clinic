// ============================================================
// auth-session.ts — Sesión desde la respuesta del backend y su renovación
//
// toSession: LoginResponse del backend (.NET) → shape de sesión de la plantilla.
//   Lo comparten login, cambio de clave y renovación.
// refreshSession: pide un JWT nuevo con el refresh token (POST /auth/refresh) y lo guarda.
//   Una sola petición en vuelo: si diez requests reciben 401 a la vez, todas esperan la
//   misma renovación. Usa un axios SIN interceptores (si no, un 401 del propio refresh
//   volvería a intentar renovar). Devuelve false si no se pudo (el refresh también venció,
//   la clínica se suspendió…): quien llama cierra la sesión.
//
// EQUIVALENTE A: la acción renovarToken() del store de sesión en SINERGIA
// ============================================================
import axios from "axios";
import { API_BASE_URL } from "@/lib/api-base-url";
import { useAuthStore } from "@/stores/auth.store";
import type { User } from "@/types/auth";

export interface NemediLoginResponse {
  token: string;
  refreshToken: string;
  /** Vencimiento real del JWT (hora local). */
  expiration: string;
  mustChangePassword: boolean;
  userInfo: {
    id: string;
    nombre: string;
    apellido: string;
    email: string;
    rol: string;
    tenantId: string;
    branchId: string | null;
  };
}

export interface Session {
  token: string;
  refreshToken: string;
  user: User;
}

export function toSession(data: NemediLoginResponse): Session {
  return {
    token: data.token,
    refreshToken: data.refreshToken,
    user: {
      id: data.userInfo.id,
      email: data.userInfo.email,
      name: `${data.userInfo.nombre} ${data.userInfo.apellido}`.trim(),
      role: data.userInfo.rol,
      tenantId: data.userInfo.tenantId,
      mustChangePassword: data.mustChangePassword,
    },
  };
}

let inflight: Promise<boolean> | null = null;

export function refreshSession(): Promise<boolean> {
  if (inflight) return inflight;
  const { token, refreshToken } = useAuthStore.getState();
  if (!token || !refreshToken) return Promise.resolve(false);

  inflight = axios
    .post<NemediLoginResponse>(`${API_BASE_URL}/api/v1/auth/refresh`, { token, refreshToken })
    .then(({ data }) => {
      const session = toSession(data);
      useAuthStore.getState().renewSession(session.token, session.refreshToken, session.user);
      return true;
    })
    .catch(() => false)
    .finally(() => {
      inflight = null;
    });
  return inflight;
}

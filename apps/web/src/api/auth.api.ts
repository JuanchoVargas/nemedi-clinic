// ============================================================
// auth.api.ts — Hooks de autenticación (Nemedi backend real)
//
// Backend .NET 8: POST /api/v1/auth/login
// Adaptamos el response real del backend al shape estándar
// LoginResponse de la plantilla.
//
// Shape real (verificado en LoginResponse.cs):
//   { token, refreshToken, expiration, userInfo: {
//       id, nombre, apellido, email, rol, tenantId, branchId? } }
// ASP.NET serializa en camelCase por default.
//
// EQUIVALENTE A: stores/sesion.js > acción autenticar() en SINERGIA
// ============================================================

import { useMutation } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import { toSession, type NemediLoginResponse } from "@/lib/auth-session";
import type { LoginRequest, LoginResponse } from "@/types/auth";

async function login(request: LoginRequest): Promise<LoginResponse> {
  const { data } = await api.post<NemediLoginResponse>("/api/v1/auth/login", request);
  return toSession(data);
}

export function useLogin() {
  return useMutation({
    mutationFn: (request: LoginRequest) => login(request),
  });
}

/**
 * Cambia la contraseña del usuario en sesión. El backend invalida los refresh tokens y
 * devuelve una sesión nueva (JWT sin el bloqueo de clave temporal): hay que guardarla.
 */
export function useChangePassword() {
  return useMutation({
    mutationFn: async (body: { currentPassword: string; newPassword: string }) => {
      const { data } = await api.post<NemediLoginResponse>("/api/v1/auth/change-password", body);
      return toSession(data);
    },
  });
}

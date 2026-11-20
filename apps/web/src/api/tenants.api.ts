// ============================================================
// tenants.api.ts — Hooks de tenants (clínicas)
//
// Backend: /api/v1/Tenants
// GET /current es para cualquier rol. Los datos editables de la clínica están en settings.api.ts.
// Crear y eliminar tenants es del PlatformAdmin → platform.api.ts.
// ============================================================

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import type { CurrentTenant } from "@/types/platform";

const BASE = "/api/v1/Tenants";

/**
 * Tenant de la sesión (plan y estado). Alimenta el aviso de "Cuenta suspendida".
 * `enabled` lo decide quien llama: solo tiene sentido con sesión de clínica.
 */
export function useCurrentTenant(enabled: boolean) {
  return useQuery({
    queryKey: ["tenants", "current"],
    queryFn: async () => {
      const { data } = await api.get<CurrentTenant>(`${BASE}/current`);
      return data;
    },
    enabled,
    staleTime: 60 * 1000,
    meta: { silent: true },
  });
}

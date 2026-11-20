// ============================================================
// settings.api.ts — Configuración de la clínica y horario de las sedes
//
// Backend: GET/PUT /api/v1/tenants/current/settings (leer: cualquier rol; cambiar: SuperAdmin)
//          GET/PUT /api/v1/branches/{id}/schedule   (Admin)
// ============================================================

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import type { BranchScheduleDay, ClinicSettings, UpdateClinicSettingsRequest } from "@/types/settings";

export function useClinicSettings(enabled = true) {
  return useQuery({
    queryKey: ["settings"],
    queryFn: async () => {
      const { data } = await api.get<ClinicSettings>("/api/v1/tenants/current/settings");
      return data;
    },
    enabled,
  });
}

/**
 * ¿La clínica es prestador habilitado (IPS)? Decide si los formularios piden los datos RIPS.
 * Comparte la caché de useClinicSettings (misma queryKey).
 */
export function useIsIps() {
  const { data, isLoading } = useClinicSettings();
  return { isIps: data?.esIps ?? false, isLoading };
}

export function useUpdateClinicSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateClinicSettingsRequest) => {
      const { data } = await api.put<ClinicSettings>("/api/v1/tenants/current/settings", body);
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(["settings"], data);
      // El nombre y el logo del menú lateral salen de /tenants/current; el IVA cambia el buscador de ventas
      qc.invalidateQueries({ queryKey: ["tenants"] });
      qc.invalidateQueries({ queryKey: ["sales", "catalog"] });
    },
  });
}

export function useBranchSchedule(branchId: string) {
  return useQuery({
    queryKey: ["branches", branchId, "schedule"],
    queryFn: async () => {
      const { data } = await api.get<BranchScheduleDay[]>(`/api/v1/branches/${branchId}/schedule`);
      return data;
    },
  });
}

export function useSaveBranchSchedule(branchId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dias: BranchScheduleDay[]) => {
      const { data } = await api.put<BranchScheduleDay[]>(`/api/v1/branches/${branchId}/schedule`, dias);
      return data;
    },
    onSuccess: (data) => qc.setQueryData(["branches", branchId, "schedule"], data),
  });
}

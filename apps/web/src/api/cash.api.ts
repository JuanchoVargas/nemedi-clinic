// ============================================================
// cash.api.ts — Caja por sede y usuario
//
// Backend: /api/v1/cash (policy Admin). GET /current responde 204 sin caja abierta:
// el hook devuelve null en ese caso.
// ============================================================

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import { saveBlob } from "@/lib/download-file";
import type { CashMovementRequest, CashSession, CloseCashRequest, OpenCashRequest } from "@/types/cash";

const BASE = "/api/v1/cash";

/** Mi caja abierta, o null. `enabled` permite no consultarla con un rol que no tiene caja. */
export function useCurrentCash(enabled = true) {
  return useQuery({
    queryKey: ["cash", "current"],
    queryFn: async () => {
      const res = await api.get<CashSession | "">(`${BASE}/current`);
      return res.status === 204 || !res.data ? null : res.data;
    },
    enabled,
    staleTime: 30 * 1000,
  });
}

export function useCashSessions(desde: string, hasta: string) {
  return useQuery({
    queryKey: ["cash", "sessions", { desde, hasta }],
    queryFn: async () => {
      const { data } = await api.get<CashSession[]>(`${BASE}/sessions`, { params: { desde, hasta } });
      return data;
    },
  });
}

export function useCashSession(id: string | undefined) {
  return useQuery({
    queryKey: ["cash", "detail", id],
    queryFn: async () => {
      const { data } = await api.get<CashSession>(`${BASE}/sessions/${id}`);
      return data;
    },
    enabled: !!id,
  });
}

function useInvalidateCash() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: ["cash"] });
}

export function useOpenCash() {
  const invalidate = useInvalidateCash();
  return useMutation({
    mutationFn: async (body: OpenCashRequest) => {
      const { data } = await api.post<CashSession>(`${BASE}/open`, body);
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useAddCashMovement() {
  const invalidate = useInvalidateCash();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CashMovementRequest }) => {
      const { data } = await api.post<CashSession>(`${BASE}/sessions/${id}/movements`, body);
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useCloseCash() {
  const invalidate = useInvalidateCash();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CloseCashRequest }) => {
      const { data } = await api.post<CashSession>(`${BASE}/sessions/${id}/close`, body);
      return data;
    },
    onSuccess: invalidate,
  });
}

/** Arqueo de una caja en Excel (resumen + movimientos). */
export async function downloadCashSession(id: string) {
  const { data, headers } = await api.get<Blob>(`${BASE}/sessions/${id}/export`, { responseType: "blob" });
  saveBlob(data, headers, `arqueo-caja-${id.slice(0, 8)}.xlsx`);
}

/** Listado de cajas del rango en Excel. */
export async function downloadCashSessions(desde: string, hasta: string) {
  const { data, headers } = await api.get<Blob>(`${BASE}/sessions`, {
    params: { desde, hasta, formato: "xlsx" },
    responseType: "blob",
  });
  saveBlob(data, headers, `cajas-${desde}-a-${hasta}.xlsx`);
}

// ============================================================
// reports.api.ts — Reportes gerenciales (policy Admin)
//
// Backend: GET /api/v1/reports/{sales|procedures|production|cartera}?desde=&hasta=
//          con &formato=xlsx el mismo reporte en Excel.
// ============================================================

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import { saveBlob } from "@/lib/download-file";
import type { CarteraReport, ProceduresReport, ProductionReport, ReportKind, SalesReport } from "@/types/report";

const BASE = "/api/v1/reports";

function useReport<T>(kind: ReportKind, desde: string, hasta: string, enabled: boolean) {
  return useQuery({
    queryKey: ["reports", kind, { desde, hasta }],
    queryFn: async () => {
      const { data } = await api.get<T>(`${BASE}/${kind}`, { params: kind === "cartera" ? {} : { desde, hasta } });
      return data;
    },
    enabled: enabled && (kind === "cartera" || (!!desde && !!hasta && desde <= hasta)),
    placeholderData: keepPreviousData,
  });
}

export const useSalesReport = (desde: string, hasta: string, enabled: boolean) => useReport<SalesReport>("sales", desde, hasta, enabled);
export const useProceduresReport = (desde: string, hasta: string, enabled: boolean) =>
  useReport<ProceduresReport>("procedures", desde, hasta, enabled);
export const useProductionReport = (desde: string, hasta: string, enabled: boolean) =>
  useReport<ProductionReport>("production", desde, hasta, enabled);
export const useCarteraReport = (enabled: boolean) => useReport<CarteraReport>("cartera", "", "", enabled);

/** El mismo reporte en Excel; lo arma el backend. */
export async function downloadReport(kind: ReportKind, desde: string, hasta: string) {
  const { data, headers } = await api.get<Blob>(`${BASE}/${kind}`, {
    params: kind === "cartera" ? { formato: "xlsx" } : { desde, hasta, formato: "xlsx" },
    responseType: "blob",
  });
  saveBlob(data, headers, kind === "cartera" ? `cartera.xlsx` : `${kind}-${desde}-a-${hasta}.xlsx`);
}

// ============================================================
// imports.api.ts — Importador desde Excel/CSV (policy Admin)
//
// Backend: /api/v1/imports
//   POST {entidad}/analyze  (file)                         → columnas, encabezado y mapeo propuesto
//   POST {entidad}/preview  (file + mapeo…)                → resumen de todas las filas + muestra de 50
//   POST {entidad}/errors   (file + mapeo…)                → Excel con las filas que fallaron
//   POST {entidad}/execute  (file + mapeo + duplicados)    → guarda en una transacción
//   GET  ?page=&pageSize=                                  → historial
//
// El servidor no guarda el archivo: cada paso lo reenvía (el navegador lo tiene en memoria).
// Nada se escribe hasta "execute"; cancelar antes no deja rastro.
// ============================================================

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import { saveBlob } from "@/lib/download-file";
import type { PagedResponse } from "@/types/api";
import type {
  ImportAnalysis,
  ImportEntity,
  ImportPreview,
  ImportResult,
  ImportRun,
  ImportStepInput,
} from "@/types/import";

const BASE = "/api/v1/imports";
// Sin Content-Type fijo (el cliente trae application/json): el navegador pone multipart/form-data con su boundary
const INVALIDATES: Record<ImportEntity, string[]> = {
  patients: ["patients"],
  products: ["products", "inventory"],
  procedures: ["procedures"],
  packages: ["packages"],
};

const MULTIPART = { headers: { "Content-Type": undefined } };

function stepForm(input: ImportStepInput, extra?: Record<string, string>): FormData {
  const form = new FormData();
  form.append("file", input.file);
  form.append("filaEncabezados", String(input.filaEncabezados));
  form.append("mapeo", JSON.stringify(input.mapeo));
  form.append("duplicados", input.duplicados);
  for (const [k, v] of Object.entries(extra ?? {})) form.append(k, v);
  return form;
}

export function useAnalyzeImport(entity: ImportEntity) {
  return useMutation({
    /** filaEncabezados (1-based) solo cuando la persona corrige la detectada. */
    mutationFn: async ({ file, filaEncabezados }: { file: File; filaEncabezados?: number }) => {
      const form = new FormData();
      form.append("file", file);
      if (filaEncabezados) form.append("filaEncabezados", String(filaEncabezados));
      const { data } = await api.post<ImportAnalysis>(`${BASE}/${entity}/analyze`, form, MULTIPART);
      return data;
    },
  });
}

export function usePreviewImport(entity: ImportEntity) {
  return useMutation({
    mutationFn: async ({ input, soloProblemas }: { input: ImportStepInput; soloProblemas: boolean }) => {
      const { data } = await api.post<ImportPreview>(
        `${BASE}/${entity}/preview`,
        stepForm(input, { soloProblemas: String(soloProblemas) }),
        MULTIPART,
      );
      return data;
    },
  });
}

export function useExecuteImport(entity: ImportEntity) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: ImportStepInput) => {
      const { data } = await api.post<ImportResult>(`${BASE}/${entity}/execute`, stepForm(input), {
        ...MULTIPART,
        timeout: 300_000, // 5.000 filas en una transacción
      });
      return data;
    },
    onSuccess: () => {
      // Lo importado y lo que depende de ello (el inventario mueve el dashboard y las alertas)
      for (const key of INVALIDATES[entity]) qc.invalidateQueries({ queryKey: [key] });
      qc.invalidateQueries({ queryKey: ["imports"] });
      qc.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

/** Excel con las filas que fallaron y su motivo (lo arma el backend). */
export function useDownloadImportErrors(entity: ImportEntity) {
  return useMutation({
    mutationFn: async (input: ImportStepInput) => {
      const { data, headers } = await api.post<Blob>(`${BASE}/${entity}/errors`, stepForm(input), {
        ...MULTIPART,
        responseType: "blob",
      });
      saveBlob(data, headers, "errores.xlsx");
    },
  });
}

export function useImportHistory(page: number, pageSize: number) {
  return useQuery({
    queryKey: ["imports", { page, pageSize }],
    queryFn: async () => {
      const { data } = await api.get<PagedResponse<ImportRun>>(BASE, { params: { page, pageSize } });
      return data;
    },
    placeholderData: keepPreviousData,
  });
}

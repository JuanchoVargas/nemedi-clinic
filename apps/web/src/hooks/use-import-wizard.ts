// ============================================================
// use-import-wizard.ts — Estado y pasos del importador (subir → mapear → revisar → importar)
//
// El archivo vive solo aquí, en memoria del navegador: el backend no lo guarda y cada paso lo
// reenvía. Hasta "Importar" nada se escribe; cancelar en cualquier paso no deja nada a medias.
// La vista (ImportWizard) solo pinta y llama a estas acciones.
//
// EQUIVALENTE A: un store Pinia local de un asistente con su "paso actual"
// ============================================================
import { useCallback, useMemo, useState } from "react";
import {
  useAnalyzeImport,
  useDownloadImportErrors,
  useExecuteImport,
  usePreviewImport,
} from "@/api/imports.api";
import type {
  DuplicateMode,
  ImportAnalysis,
  ImportEntity,
  ImportField,
  ImportMapping,
  ImportPreview,
  ImportResult,
  ImportStepInput,
} from "@/types/import";

export type ImportStep = "upload" | "mapping" | "preview" | "result";

export const MAX_FILE_BYTES = 10 * 1024 * 1024;
const EXTENSIONS = [".xlsx", ".xls", ".csv"];

/** Revisión rápida en el navegador; la de verdad (por contenido) la hace el backend. */
export function checkImportFile(file: File): string | null {
  const name = file.name.toLowerCase();
  if (!EXTENSIONS.some((ext) => name.endsWith(ext))) return "Sube un archivo de Excel (.xlsx, .xls) o CSV.";
  if (file.size === 0) return "El archivo está vacío.";
  if (file.size > MAX_FILE_BYTES) return "El archivo pesa más de 10 MB. Pártelo en varios.";
  return null;
}

/** Campos obligatorios que el mapeo deja sin columna (nombre/apellido se cubren con "Nombre completo"). */
export function missingRequired(fields: ImportField[], mapping: ImportMapping): ImportField[] {
  const has = (key: string) => key in mapping;
  const missing = fields.filter((f) => f.required && !has(f.key));
  const fullName = has("nombreCompleto");
  for (const key of ["nombre", "apellido"]) {
    const field = fields.find((f) => f.key === key);
    if (field && !has(key) && !fullName) missing.push(field);
  }
  return missing;
}

export function useImportWizard(entity: ImportEntity) {
  const [step, setStep] = useState<ImportStep>("upload");
  const [file, setFile] = useState<File | null>(null);
  const [analysis, setAnalysis] = useState<ImportAnalysis | null>(null);
  const [mapping, setMapping] = useState<ImportMapping>({});
  const [duplicados, setDuplicados] = useState<DuplicateMode>("Omitir");
  const [soloProblemas, setSoloProblemas] = useState(false);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);

  const analyze = useAnalyzeImport(entity);
  const previewMutation = usePreviewImport(entity);
  const execute = useExecuteImport(entity);
  const downloadErrors = useDownloadImportErrors(entity);

  const input = useCallback(
    (): ImportStepInput | null =>
      file && analysis ? { file, filaEncabezados: analysis.filaEncabezados, mapeo: mapping, duplicados } : null,
    [file, analysis, mapping, duplicados],
  );

  /** Paso 1: elegir archivo → el backend propone encabezado y mapeo. */
  const chooseFile = async (selected: File) => {
    const problem = checkImportFile(selected);
    setFileError(problem);
    if (problem) return;
    try {
      const data = await analyze.mutateAsync({ file: selected });
      setFile(selected);
      setAnalysis(data);
      setMapping(data.mapeoSugerido);
      setPreview(null);
      setStep("mapping");
    } catch {
      // El toast global ya mostró el motivo (vacío, no es Excel, más de 5.000 filas…)
    }
  };

  /** Paso 2: la fila de encabezados detectada no era; se vuelve a analizar con la elegida. */
  const changeHeaderRow = async (fila: number) => {
    if (!file || !analysis || fila === analysis.filaEncabezados) return;
    try {
      const data = await analyze.mutateAsync({ file, filaEncabezados: fila });
      setAnalysis(data);
      setMapping(data.mapeoSugerido);
    } catch {
      // toast global
    }
  };

  /** Paso 2: asigna (o quita, con null) el campo de una columna. Un campo vive en una sola columna. */
  const assignColumn = (column: number, fieldKey: string | null) => {
    setMapping((current) => {
      const next: ImportMapping = {};
      for (const [key, col] of Object.entries(current)) {
        if (col !== column && key !== fieldKey) next[key] = col;
      }
      if (fieldKey) next[fieldKey] = column;
      return next;
    });
  };

  const loadPreview = async (onlyProblems = soloProblemas) => {
    const data = input();
    if (!data) return;
    try {
      setPreview(await previewMutation.mutateAsync({ input: data, soloProblemas: onlyProblems }));
      setStep("preview");
    } catch {
      // toast global
    }
  };

  const toggleSoloProblemas = (value: boolean) => {
    setSoloProblemas(value);
    void loadPreview(value);
  };

  const runImport = async () => {
    const data = input();
    if (!data) return;
    try {
      setResult(await execute.mutateAsync(data));
      setStep("result");
    } catch {
      // toast global; la transacción del backend no dejó nada a medias
    }
  };

  const downloadErrorRows = () => {
    const data = input();
    if (data) downloadErrors.mutate(data);
  };

  const reset = () => {
    setStep("upload");
    setFile(null);
    setAnalysis(null);
    setMapping({});
    setDuplicados("Omitir");
    setSoloProblemas(false);
    setPreview(null);
    setResult(null);
    setFileError(null);
  };

  const missing = useMemo(() => (analysis ? missingRequired(analysis.campos, mapping) : []), [analysis, mapping]);

  /** Registros (no filas) que se escribirían con el modo de duplicados elegido. */
  const toWrite = preview ? preview.nuevos + (duplicados === "Actualizar" ? preview.existentes : 0) : 0;

  return {
    step,
    setStep,
    file,
    fileError,
    analysis,
    mapping,
    missing,
    duplicados,
    setDuplicados,
    soloProblemas,
    preview,
    result,
    toWrite,
    analyzing: analyze.isPending,
    previewing: previewMutation.isPending,
    importing: execute.isPending,
    downloadingErrors: downloadErrors.isPending,
    chooseFile,
    changeHeaderRow,
    assignColumn,
    loadPreview,
    toggleSoloProblemas,
    runImport,
    downloadErrorRows,
    reset,
  };
}

export type ImportWizardState = ReturnType<typeof useImportWizard>;

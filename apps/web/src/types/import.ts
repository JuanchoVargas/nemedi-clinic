// ============================================================
// import.ts — Importador desde Excel/CSV (mirrors de DTOs/Imports/ImportDtos.cs)
// ============================================================

/** Entidades que el backend sabe importar (IImportDefinition.Entidad). */
export type ImportEntity = "patients" | "products" | "procedures" | "packages";

export interface ImportField {
  key: string;
  label: string;
  required: boolean;
  hint?: string | null;
  /** "RIPS" para los campos que solo aparecen en una IPS. */
  group?: string | null;
}

export interface ImportColumn {
  index: number;
  letter: string;
  header: string;
  samples: string[];
}

/** Campo → índice de columna. */
export type ImportMapping = Record<string, number>;

export interface ImportAnalysis {
  archivoNombre: string;
  formato: "xlsx" | "xls" | "csv";
  /** 1-based, como en Excel. */
  filaEncabezados: number;
  filasDeDatos: number;
  maxFilas: number;
  columnas: ImportColumn[];
  campos: ImportField[];
  mapeoSugerido: ImportMapping;
  formatosDeFecha: string[];
}

export type ImportRowState = "Valida" | "Error" | "Duplicada";
export type DuplicateMode = "Omitir" | "Actualizar";

export interface ImportPreviewRow {
  fila: number;
  estado: ImportRowState;
  valores: Record<string, string>;
  errores: string[];
  avisos: string[];
  existente?: string | null;
}

export interface ImportPreview {
  total: number;
  validas: number;
  conError: number;
  duplicadas: number;
  conAvisos: number;
  /** Registros (no filas) que se crearían: un producto con varios lotes o un paquete con varias sesiones cuenta uno. */
  nuevos: number;
  /** Registros que ya existen: se omiten o se actualizan. */
  existentes: number;
  /** "12 lotes con 340 unidades para los productos nuevos." */
  detalle?: string | null;
  /** Avisos sobre el archivo entero ("no trae cantidades: el stock se carga aparte"). */
  avisosGenerales: string[];
  filas: ImportPreviewRow[];
}

export interface ImportResult {
  id: string;
  total: number;
  creadas: number;
  actualizadas: number;
  omitidas: number;
  conError: number;
  detalle?: string | null;
}

export interface ImportRun {
  id: string;
  entidad: string;
  entidadNombre: string;
  archivoNombre: string;
  fecha: string;
  usuario: string;
  filasTotales: number;
  creadas: number;
  actualizadas: number;
  omitidas: number;
  conError: number;
  modoDuplicados: DuplicateMode;
  detalle?: string | null;
}

/** Lo que cada paso después del primero le reenvía al backend junto con el archivo. */
export interface ImportStepInput {
  file: File;
  filaEncabezados: number;
  mapeo: ImportMapping;
  duplicados: DuplicateMode;
}

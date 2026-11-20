// ============================================================
// iva.ts — IVA de un ítem del catálogo en los formularios
//
// En el formulario el IVA es un string: "clinica" (usar el de la configuración de
// la clínica) o una tarifa. En el API es un número o null (null = el de la clínica).
// ============================================================

export const IVA_CLINICA = "clinica";

/** Tarifas de IVA vigentes en Colombia. */
export const IVA_TARIFAS = ["0", "5", "19"] as const;

export function ivaToForm(value: number | null | undefined): string {
  return value == null ? IVA_CLINICA : String(value);
}

/** Campos del body: en crear basta con no mandar el IVA; en editar hay que pedir "volver al de la clínica". */
export function ivaToRequest(value: string): { ivaPorcentaje?: number; ivaPorDefecto: boolean } {
  return value === IVA_CLINICA ? { ivaPorDefecto: true } : { ivaPorcentaje: Number(value), ivaPorDefecto: false };
}

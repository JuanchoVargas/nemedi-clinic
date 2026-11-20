// ============================================================
// cash.ts — Caja (CashController)
//
//   GET  /api/v1/cash/current              → CashSession | 204 (sin caja abierta)
//   GET  /api/v1/cash/sessions[/{id}]      → historial / detalle con movimientos
//   Abierta: los totales se calculan en vivo. Cerrada: son los congelados al cerrar.
// ============================================================

export interface CashSummary {
  montoApertura: number;
  totalEfectivo: number;
  totalTransferencia: number;
  totalTarjeta: number;
  totalOtro: number;
  totalPagos: number;
  totalIngresos: number;
  totalEgresos: number;
  /** Base + efectivo + ingresos − egresos. */
  efectivoEsperado: number;
  cantidadPagos: number;
}

export interface CashMovement {
  id: string;
  /** "Pago" (automático) | "Ingreso" | "Egreso" (manuales). */
  tipo: "Pago" | "Ingreso" | "Egreso";
  concepto: string;
  monto: number;
  metodoPago?: string | null;
  referencia?: string | null;
  fecha: string;
  usuario?: string | null;
  saleId?: string | null;
  saleNumero?: number | null;
  patientId?: string | null;
  pacienteNombre?: string | null;
  /** Pago de una venta anulada: tachado, no suma. */
  anulado: boolean;
}

export interface CashSession {
  id: string;
  branchId: string;
  branchNombre: string;
  usuarioId: string;
  usuario: string;
  estado: "Abierta" | "Cerrada";
  fechaApertura: string;
  observacionApertura?: string | null;
  fechaCierre?: string | null;
  cerradaPor?: string | null;
  observacionCierre?: string | null;
  efectivoContado?: number | null;
  /** Contado − esperado. Negativa = faltante. */
  diferencia?: number | null;
  abiertaDesdeOtroDia: boolean;
  puedeOperar: boolean;
  resumen: CashSummary;
  movimientos: CashMovement[];
}

export interface OpenCashRequest {
  branchId?: string;
  montoApertura: number;
  observacion?: string;
}

export interface CashMovementRequest {
  tipo: "Ingreso" | "Egreso";
  concepto: string;
  monto: number;
}

export interface CloseCashRequest {
  efectivoContado: number;
  observacion?: string;
}

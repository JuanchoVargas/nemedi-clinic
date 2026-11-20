// ============================================================
// sale.ts — Ventas (SalesController)
//
//   GET  /api/v1/sales            → PagedResponse<Sale> (sin líneas ni pagos)
//   GET  /api/v1/sales/{id}       → Sale con líneas y pagos (los anulados vienen marcados)
//   GET  /api/v1/sales/catalog    → SaleCatalogItem[] (buscador de "Nueva venta")
//   POST /api/v1/sales/quote      → SaleQuote (el IVA lo calcula el backend, nunca la web)
// ============================================================

export const SaleLineType = {
  Producto: "Producto",
  Procedimiento: "Procedimiento",
  Paquete: "Paquete",
  Otro: "Otro",
} as const;
export type SaleLineType = (typeof SaleLineType)[keyof typeof SaleLineType];

export const SALE_LINE_LABELS: Record<SaleLineType, string> = {
  Producto: "Producto",
  Procedimiento: "Procedimiento",
  Paquete: "Paquete",
  Otro: "Otro",
};

export type SaleStatus = "Borrador" | "Confirmada" | "Anulada";

export const SALE_STATUS_BADGE: Record<SaleStatus, "outline" | "success" | "secondary"> = {
  Borrador: "outline",
  Confirmada: "success",
  Anulada: "secondary",
};

export const PAYMENT_METHODS = ["Efectivo", "Transferencia", "Tarjeta", "Otro"] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];

/** Estado de la factura electrónica. Sin proveedor configurado siempre es "SinFacturar". */
export const INVOICE_STATUS_LABELS: Record<string, string> = {
  SinFacturar: "Sin facturar",
  Pendiente: "En proceso",
  Emitida: "Emitida",
  Rechazada: "Rechazada",
  Anulada: "Anulada",
};

export interface SaleLine {
  id: string;
  orden: number;
  tipo: SaleLineType;
  referenciaId?: string | null;
  descripcion: string;
  cantidad: number;
  precioUnitario: number;
  descuento: number;
  ivaPorcentaje: number;
  subtotal: number;
  iva: number;
  total: number;
  patientPackageId?: string | null;
}

export interface SalePayment {
  id: string;
  monto: number;
  fechaPago: string;
  registradoEl: string;
  metodoPago: PaymentMethod;
  referencia?: string | null;
  observacion?: string | null;
  registradoPor?: string | null;
  comprobanteId?: string | null;
  patientPackageId?: string | null;
  /** Nombre del paquete al que se aplicó el abono. */
  aplicadoA?: string | null;
  anulado: boolean;
}

export interface Sale {
  id: string;
  numero: number;
  fecha: string;
  estado: SaleStatus;
  branchId: string;
  branchNombre: string;
  patientId?: string | null;
  cliente: string;
  clienteDocumento?: string | null;
  esMostrador: boolean;
  subtotal: number;
  descuento: number;
  impuestos: number;
  total: number;
  totalPagado: number;
  saldo: number;
  /** SinPagos | Parcial | Pagado; vacío si no está confirmada. */
  estadoPago: string;
  vendedor: string;
  observacion?: string | null;
  fechaAnulacion?: string | null;
  anuladaPor?: string | null;
  motivoAnulacion?: string | null;
  resumen: string;
  facturaEstado: string;
  facturaNumero?: string | null;
  facturaCufe?: string | null;
  facturaFecha?: string | null;
  lineas: SaleLine[];
  pagos: SalePayment[];
}

export interface SaleCatalogItem {
  tipo: SaleLineType;
  id: string;
  nombre: string;
  detalle?: string | null;
  /** Sin IVA. */
  precio: number;
  ivaPorcentaje: number;
  stock?: number | null;
  unidadMedida?: string | null;
  imagenId?: string | null;
}

export interface SaleLineRequest {
  tipo: SaleLineType;
  referenciaId?: string;
  descripcion?: string;
  cantidad: number;
  precioUnitario?: number;
  descuento: number;
  ivaPorcentaje?: number;
}

export interface SalePaymentRequest {
  monto: number;
  metodoPago: PaymentMethod;
  referencia?: string;
  observacion?: string;
  comprobanteId?: string;
}

export interface SaveSaleRequest {
  branchId?: string;
  patientId?: string;
  clienteNombre?: string;
  clienteDocumento?: string;
  observacion?: string;
  lineas: SaleLineRequest[];
  pagos: SalePaymentRequest[];
  confirmar: boolean;
}

export interface SaleQuote {
  lineas: SaleLine[];
  subtotal: number;
  descuento: number;
  impuestos: number;
  total: number;
}

export interface VoidSaleResult {
  venta: Sale;
  yaEstabaAnulada: boolean;
  movimientosRevertidos: number;
  paquetesRevertidos: number;
  pagosAnulados: number;
  devolucionEfectivo: number;
  devolucionFueraDeCaja: number;
}

export interface SaleFilters {
  desde?: string;
  hasta?: string;
  estado?: SaleStatus;
  conSaldo?: boolean;
  search?: string;
  patientId?: string;
  page: number;
  pageSize: number;
}

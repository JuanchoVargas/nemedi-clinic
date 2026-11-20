// ============================================================
// invoice.ts — Facturación electrónica (espejo de DTOs/Invoicing)
// ============================================================

export type InvoiceStatus = "Pendiente" | "Emitida" | "Rechazada" | "Anulada";
export type InvoiceDocumentType = "Factura" | "NotaCredito";

export const INVOICE_BADGE: Record<string, "success" | "warning" | "destructive" | "secondary" | "outline"> = {
  Emitida: "success",
  Pendiente: "warning",
  Rechazada: "destructive",
  Anulada: "secondary",
  SinFacturar: "outline",
};

export const DOCUMENT_LABELS: Record<InvoiceDocumentType, string> = {
  Factura: "Factura",
  NotaCredito: "Nota crédito",
};

export interface InvoiceLine {
  indice: number;
  codigo: string;
  descripcion: string;
  cantidad: number;
  precioUnitario: number;
  descuento: number;
  ivaPorcentaje: number;
  total: number;
  cantidadDevuelta: number;
}

export interface Invoice {
  id: string;
  tipoDocumento: InvoiceDocumentType;
  estado: InvoiceStatus;
  referencia: string;
  numero: string | null;
  cufe: string | null;
  fechaEmision: string | null;
  creada: string;
  saleId: string | null;
  ventaNumero: number | null;
  paymentId: string | null;
  patientId: string | null;
  branchId: string;
  branchNombre: string;
  clienteNombre: string;
  clienteDocumento: string;
  clienteEmail: string | null;
  subtotal: number;
  impuestos: number;
  total: number;
  mensaje: string | null;
  advertencias: string | null;
  facturaOrigenId: string | null;
  facturaOrigenNumero: string | null;
  conceptoCorreccion: string | null;
  conceptoCorreccionNombre: string | null;
  motivo: string | null;
  totalNotasCredito: number;
  pdfId: string | null;
  xmlId: string | null;
  intentos: number;
  ultimoIntento: string | null;
  correoEnviadoA: string | null;
  fechaCorreo: string | null;
  emitidaPor: string;
  lineas: InvoiceLine[];
}

export interface InvoiceFilters {
  desde?: string;
  hasta?: string;
  estado?: string;
  tipoDocumento?: string;
  branchId?: string;
  patientId?: string;
  saleId?: string;
  search?: string;
  page: number;
  pageSize: number;
}

export interface CreditNoteRequest {
  tipo: "Anulacion" | "Devolucion";
  motivo: string;
  lineas: { indice: number; cantidad: number }[];
}

// ── Configuración ──

export interface InvoicingBranch {
  branchId: string;
  branchNombre: string;
  rangoFacturaId: number | null;
  prefijoFactura: string | null;
  rangoNotaCreditoId: number | null;
  prefijoNotaCredito: string | null;
}

export interface InvoicingSettings {
  configurada: boolean;
  habilitada: boolean;
  ambiente: "Pruebas" | "Produccion";
  proveedor: string;
  tieneCredenciales: boolean;
  /** Guardadas pero no se pueden descifrar (cambió la clave del servidor): hay que volver a escribirlas. */
  credencialesIlegibles: boolean;
  usuarioPista: string | null;
  clientIdPista: string | null;
  emisorNit: string;
  emisorDv: string | null;
  emisorRazonSocial: string;
  emisorTipoOrganizacion: string;
  emisorResponsabilidad: string;
  emisorMunicipio: string | null;
  emisorDireccion: string | null;
  emisorTelefono: string | null;
  emisorEmail: string | null;
  unidadMedida: string;
  codigoEstandar: string;
  ivaCeroExcluido: boolean;
  enviarCorreoAlEmitir: boolean;
  codigosMetodoPago: Record<string, string>;
  ultimaPrueba: string | null;
  ultimaPruebaExitosa: boolean;
  ultimaPruebaMensaje: string | null;
  sedes: InvoicingBranch[];
}

export interface UpdateInvoicingSettings
  extends Omit<InvoicingSettings, "configurada" | "proveedor" | "tieneCredenciales" | "credencialesIlegibles" | "usuarioPista" | "clientIdPista" | "ultimaPrueba" | "ultimaPruebaExitosa" | "ultimaPruebaMensaje"> {
  clientId?: string;
  clientSecret?: string;
  usuario?: string;
  contrasena?: string;
}

export interface TestConnectionRequest {
  ambiente?: string;
  clientId?: string;
  clientSecret?: string;
  usuario?: string;
  contrasena?: string;
}

export interface NumberingRange {
  id: number;
  documento: string;
  prefijo: string;
  desde: number | null;
  hasta: number | null;
  actual: number;
  resolucion: string | null;
  vigente: boolean;
}

export interface TestConnectionResult {
  exitosa: boolean;
  mensaje: string;
  emisorNit: string | null;
  emisorNombre: string | null;
  nitNoCoincide: boolean;
  rangos: NumberingRange[];
}

export interface CatalogItem {
  codigo: string;
  nombre: string;
}

export interface InvoicingCatalogs {
  fuente: string;
  generado: string;
  tiposDocumento: CatalogItem[];
  tiposOrganizacion: CatalogItem[];
  responsabilidades: CatalogItem[];
  mediosPago: CatalogItem[];
  conceptosCorreccion: CatalogItem[];
  codigosEstandar: CatalogItem[];
  tributos: CatalogItem[];
  mediosPagoPorDefecto: Record<string, string>;
}

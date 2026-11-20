// ============================================================
// report.ts — Reportes (ReportsController). Todo llega calculado.
// ============================================================

export interface SalesReport {
  desde: string;
  hasta: string;
  ventas: number;
  anuladas: number;
  subtotal: number;
  impuestos: number;
  total: number;
  pagado: number;
  saldo: number;
  porDia: { fecha: string; ventas: number; total: number }[];
  porTipo: { tipo: string; cantidad: number; subtotal: number; iva: number; total: number }[];
  porMetodo: { metodo: string; total: number }[];
  filas: {
    id: string;
    numero: number;
    fecha: string;
    cliente: string;
    documento?: string | null;
    estado: string;
    subtotal: number;
    impuestos: number;
    total: number;
    pagado: number;
    saldo: number;
    vendedor: string;
    sede: string;
  }[];
}

export interface ProceduresReport {
  desde: string;
  hasta: string;
  total: number;
  valorTotal: number;
  filas: { procedureId: string; procedimiento: string; realizadas: number; dePaquete: number; sueltas: number; valor: number; esteticistas: string }[];
}

export interface ProductionReport {
  desde: string;
  hasta: string;
  filas: {
    esteticistId: string;
    esteticista: string;
    completadas: number;
    canceladas: number;
    noAsistio: number;
    horas: number;
    pacientes: number;
    valorProducido: number;
    notasClinicas: number;
  }[];
}

export interface CarteraReport {
  corte: string;
  total: number;
  clientes: number;
  filas: {
    patientId?: string | null;
    cliente: string;
    documento?: string | null;
    telefono?: string | null;
    saldoPaquetes: number;
    saldoVentas: number;
    total: number;
    documentos: number;
    desdeCuando: string;
    diasMora: number;
  }[];
}

export type ReportKind = "sales" | "procedures" | "production" | "cartera";

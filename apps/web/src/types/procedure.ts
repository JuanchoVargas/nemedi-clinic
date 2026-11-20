// ============================================================
// procedure.ts — Tipos del catálogo de procedimientos
//
// Backend: ProceduresController.cs + DTOs/Procedures/*.cs
// Policy de mutación: Admin. GET ahora abierto a cualquier autenticado.
// ============================================================

export interface Procedure {
  /** La cita no pasa a "En curso" sin un consentimiento informado firmado. */
  requiereConsentimiento?: boolean;
  /** IVA propio; null = el de servicios de la clínica. */
  ivaPorcentaje?: number | null;
  id: string;
  nombre: string;
  descripcion: string;
  precioBase: number;
  duracionMinutos: number;
  areaCorporal: string;
  activo: boolean;
  createdAt?: string;
  /** Id del Attachment con la imagen (se sube antes con POST /files). */
  imagenId?: string | null;
  /** Se reporta en RIPS (en una clínica IPS exige CUPS, finalidad y tipo de servicio). */
  requiereRips?: boolean;
  codigoCups?: string | null;
  finalidadConsulta?: string | null;
  tipoServicio?: string | null;
}

/** Campos RIPS del procedimiento en los bodies (valores = nombres de los enums del backend). */
export interface ProcedureRipsFields {
  requiereRips?: boolean;
  codigoCups?: string;
  finalidadConsulta?: string;
  tipoServicio?: string;
}

export interface CreateProcedureRequest extends ProcedureRipsFields {
  nombre: string;
  descripcion: string;
  precioBase: number;
  duracionMinutos: number;
  areaCorporal: string;
  /** Id del Attachment con la imagen (se sube antes con POST /files). */
  imagenId?: string | null;
  requiereConsentimiento?: boolean;
  ivaPorcentaje?: number;
  ivaPorDefecto?: boolean;
}

export interface UpdateProcedureRequest extends ProcedureRipsFields {
  nombre?: string;
  descripcion?: string;
  precioBase?: number;
  duracionMinutos?: number;
  areaCorporal?: string;
  activo?: boolean;
  /** Id del Attachment con la imagen (se sube antes con POST /files). */
  imagenId?: string | null;
  requiereConsentimiento?: boolean;
  ivaPorcentaje?: number;
  ivaPorDefecto?: boolean;
}

// ============================================================
// settings.ts — Configuración de la clínica (TenantsController /current/settings)
//               y horario de atención por sede (BranchesController /{id}/schedule)
// ============================================================

export interface ClinicSettings {
  id: string;
  nombre: string;
  nit: string;
  telefono: string;
  email: string;
  direccion?: string | null;
  ciudad?: string | null;
  logoId?: string | null;
  /** Horas mínimas para agendar; 0 = sin mínimo. */
  anticipacionMinimaHoras: number;
  /** false apaga el bloqueo de "Iniciar" sin consentimiento firmado. */
  exigirConsentimiento: boolean;
  ivaProductos: number;
  ivaServicios: number;
  /** Prestador habilitado (IPS). Solo lectura: lo marca la plataforma. */
  esIps: boolean;
  /** Código de habilitación REPS (12 dígitos); obligatorio si es IPS. */
  codigoHabilitacionReps?: string | null;
}

export type UpdateClinicSettingsRequest = Omit<ClinicSettings, "id" | "esIps">;

/** DiaSemana: 0 = domingo … 6 = sábado. Horas "HH:mm:ss". */
export interface BranchScheduleDay {
  diaSemana: number;
  horaApertura: string;
  horaCierre: string;
}

export const DIAS_SEMANA = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"] as const;

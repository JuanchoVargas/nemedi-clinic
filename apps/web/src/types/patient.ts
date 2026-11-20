// ============================================================
// patient.ts — Tipos del dominio Paciente
//
// Mirrors de las DTOs del backend .NET (ASP.NET serializa en camelCase).
// Backend: PatientsController.cs + DTOs/Patients/*.cs
// ============================================================

/**
 * Paciente completo (response de GET /api/v1/Patients/{id}).
 * En el form de creación/edición trabajamos sobre un subset.
 */
export interface Patient {
  id: string;
  nombre: string;
  apellido: string;
  cedula: string;
  telefono: string;
  email?: string | null;
  fechaNacimiento?: string | null; // ISO 8601
  direccion?: string | null;
  ciudad?: string | null;
  fotoUrl?: string | null;
  /** Id del Attachment (Kind=Perfil) con la foto de perfil. */
  imagenId?: string | null;
  /** Próxima cita Agendada o Confirmada (hora local). */
  proximaCita?: string | null;
  notasGenerales?: string | null;
  isActive: boolean;
  createdAt: string;
  paquetesActivos?: PatientPackageSummary[];
  // ── RIPS (valores = nombres de los enums del backend) ──
  tipoDocumento?: string | null;
  sexo?: string | null;
  paisResidencia?: string | null;
  municipioResidencia?: string | null;
  zonaResidencia?: string | null;
  tipoUsuario?: string | null;
  codigoEntidad?: string | null;
  nombreEntidad?: string | null;
  /** Solo en una clínica IPS: si tiene todo lo que RIPS exige. Null si la clínica no es IPS. */
  ripsCompleto?: boolean | null;
  ripsFaltantes?: string[];
}

/** Datos RIPS del paciente en los bodies. Solo se exigen si la clínica es IPS. */
export interface PatientRipsFields {
  tipoDocumento?: string;
  sexo?: string;
  paisResidencia?: string;
  municipioResidencia?: string;
  zonaResidencia?: string;
  tipoUsuario?: string;
  codigoEntidad?: string;
  nombreEntidad?: string;
}

/**
 * Item de listado de pacientes (response de GET /api/v1/Patients).
 * Más liviano que Patient.
 */
export interface PatientSummary {
  id: string;
  nombre: string;
  apellido: string;
  cedula: string;
  telefono: string;
  imagenId?: string | null;
  /** Nombre del paquete activo si existe; sino undefined. */
  paqueteActivo?: string | null;
  /** TODO: el backend aún no lo expone; queda como placeholder. */
  proximaCita?: string | null;
}

export interface PatientPackageSummary {
  id: string;
  packageNombre: string;
  estado: string; // "Activo" | "Pausado" | "Completado" | "Vencido"
  sesionesCompletadas: number;
  sesionesTotales: number;
  /** 0–100; null para Esteticista (no ve datos financieros). */
  porcentajePagado?: number | null;
}

/**
 * Body del POST /api/v1/Patients.
 */
export interface CreatePatientRequest extends PatientRipsFields {
  nombre: string;
  apellido: string;
  cedula: string;
  telefono: string;
  email?: string;
  fechaNacimiento?: string;
  direccion?: string;
  ciudad?: string;
  fotoUrl?: string;
  notasGenerales?: string;
  /** Id del Attachment con la imagen (se sube antes con POST /files). */
  imagenId?: string | null;
}

/**
 * Body del PUT /api/v1/Patients/{id}. Todos los campos opcionales
 * (el backend actualiza solo los que vienen distintos de null).
 */
export interface UpdatePatientRequest extends PatientRipsFields {
  nombre?: string;
  apellido?: string;
  cedula?: string;
  telefono?: string;
  email?: string;
  fechaNacimiento?: string;
  direccion?: string;
  ciudad?: string;
  fotoUrl?: string;
  notasGenerales?: string;
  isActive?: boolean;
  /** Id del Attachment con la imagen (se sube antes con POST /files). */
  imagenId?: string | null;
}

// ============================================================
// rips.ts — Catálogos RIPS (Resolución 2275 de 2023) para los formularios
//
// Los valores son los nombres de los enums del backend (RipsEnums.cs); las etiquetas
// llevan el código oficial entre paréntesis para que quien reporta lo reconozca.
// Estos datos solo se piden cuando la clínica es IPS (useIsIps).
// ============================================================

export interface RipsOption {
  value: string;
  label: string;
}

export const TIPOS_DOCUMENTO: RipsOption[] = [
  { value: "CC", label: "CC · Cédula de ciudadanía" },
  { value: "TI", label: "TI · Tarjeta de identidad" },
  { value: "RC", label: "RC · Registro civil" },
  { value: "CE", label: "CE · Cédula de extranjería" },
  { value: "PA", label: "PA · Pasaporte" },
  { value: "PT", label: "PT · Permiso por protección temporal" },
  { value: "PE", label: "PE · Permiso especial de permanencia" },
  { value: "CD", label: "CD · Carné diplomático" },
  { value: "SC", label: "SC · Salvoconducto" },
  { value: "CN", label: "CN · Certificado de nacido vivo" },
  { value: "AS", label: "AS · Adulto sin identificar" },
  { value: "MS", label: "MS · Menor sin identificar" },
  { value: "DE", label: "DE · Documento extranjero" },
  { value: "SI", label: "SI · Sin identificación" },
];

export const SEXOS: RipsOption[] = [
  { value: "Mujer", label: "Mujer (M)" },
  { value: "Hombre", label: "Hombre (H)" },
  { value: "Indeterminado", label: "Indeterminado (I)" },
];

export const ZONAS: RipsOption[] = [
  { value: "Urbana", label: "Urbana (02)" },
  { value: "Rural", label: "Rural (01)" },
];

export const TIPOS_USUARIO: RipsOption[] = [
  { value: "Particular", label: "Particular (12)" },
  { value: "ContributivoCotizante", label: "Contributivo cotizante (01)" },
  { value: "ContributivoBeneficiario", label: "Contributivo beneficiario (02)" },
  { value: "ContributivoAdicional", label: "Contributivo adicional (03)" },
  { value: "Subsidiado", label: "Subsidiado (04)" },
  { value: "NoAfiliado", label: "No afiliado (05)" },
  { value: "EspecialCotizante", label: "Especial o excepción cotizante (06)" },
  { value: "EspecialBeneficiario", label: "Especial o excepción beneficiario (07)" },
  { value: "EspecialNoCotizante", label: "Especial o excepción no cotizante (13)" },
  { value: "PrivadoLibertadFondoSalud", label: "Privado de la libertad · Fondo de salud (08)" },
  { value: "TomadorArl", label: "Tomador / amparado ARL (09)" },
  { value: "TomadorSoat", label: "Tomador / amparado SOAT (10)" },
  { value: "TomadorPlanVoluntario", label: "Tomador / amparado plan voluntario (11)" },
];

/** Regímenes afiliados a una entidad: exigen la EPS (mismo criterio que RipsRules.RequiereEntidad). */
const CON_ENTIDAD = new Set([
  "ContributivoCotizante", "ContributivoBeneficiario", "ContributivoAdicional", "Subsidiado",
  "EspecialCotizante", "EspecialBeneficiario", "EspecialNoCotizante",
]);
export const requiereEntidad = (tipoUsuario?: string | null) => !!tipoUsuario && CON_ENTIDAD.has(tipoUsuario);

export const COLOMBIA = "170";

/** Países más frecuentes (código ISO 3166 numérico, el que pide RIPS). */
export const PAISES: RipsOption[] = [
  { value: "170", label: "Colombia (170)" },
  { value: "862", label: "Venezuela (862)" },
  { value: "218", label: "Ecuador (218)" },
  { value: "604", label: "Perú (604)" },
  { value: "591", label: "Panamá (591)" },
  { value: "484", label: "México (484)" },
  { value: "032", label: "Argentina (032)" },
  { value: "152", label: "Chile (152)" },
  { value: "076", label: "Brasil (076)" },
  { value: "840", label: "Estados Unidos (840)" },
  { value: "724", label: "España (724)" },
];

/** Sugerencias del municipio (código DANE de 5 dígitos). Se puede escribir cualquier otro. */
export const MUNICIPIOS_FRECUENTES: RipsOption[] = [
  { value: "11001", label: "Bogotá D.C." },
  { value: "05001", label: "Medellín" },
  { value: "76001", label: "Cali" },
  { value: "08001", label: "Barranquilla" },
  { value: "13001", label: "Cartagena" },
  { value: "68001", label: "Bucaramanga" },
  { value: "66001", label: "Pereira" },
  { value: "17001", label: "Manizales" },
  { value: "25175", label: "Chía" },
  { value: "25754", label: "Soacha" },
  { value: "25899", label: "Zipaquirá" },
  { value: "25269", label: "Facatativá" },
  { value: "25473", label: "Mosquera" },
  { value: "25430", label: "Madrid" },
  { value: "25126", label: "Cajicá" },
  { value: "05360", label: "Itagüí" },
  { value: "05266", label: "Envigado" },
];

export const FINALIDADES: RipsOption[] = [
  { value: "Tratamiento", label: "Tratamiento (16)" },
  { value: "ValoracionIntegral", label: "Valoración integral (11)" },
  { value: "Diagnostico", label: "Diagnóstico (15)" },
  { value: "DeteccionTemprana", label: "Detección temprana (12)" },
  { value: "ProteccionEspecifica", label: "Protección específica (14)" },
  { value: "Rehabilitacion", label: "Rehabilitación (17)" },
  { value: "Paliacion", label: "Paliación (18)" },
];

export const TIPOS_SERVICIO: RipsOption[] = [
  { value: "Procedimiento", label: "Procedimiento" },
  { value: "Consulta", label: "Consulta" },
  { value: "OtroServicio", label: "Otro servicio" },
];

export const labelOf = (options: RipsOption[], value?: string | null) =>
  options.find((o) => o.value === value)?.label ?? value ?? "";

// Datos RIPS válidos de un paciente. La clínica demo es IPS, así que todo paciente que los tests
// crean contra ella los necesita (Res. 2275 de 2023: CC, Mujer, Colombia, Bogotá, urbana, particular).
export const RIPS_PACIENTE = {
  tipoDocumento: "CC",
  sexo: "Mujer",
  fechaNacimiento: "1990-06-15",
  paisResidencia: "170",
  municipioResidencia: "11001",
  zonaResidencia: "Urbana",
  tipoUsuario: "Particular",
} as const;

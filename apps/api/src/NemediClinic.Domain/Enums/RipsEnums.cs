namespace NemediClinic.Domain.Enums;

// Tablas de referencia de RIPS (Resolución 2275 de 2023, tablas publicadas en SISPRO).
// Se guardan por nombre (string); el valor numérico del enum es el código oficial, que es lo que
// va en el JSON de RIPS (con dos dígitos donde la tabla lo pide: RipsCodes.Codigo).

/// <summary>Tipo de documento de identificación (tabla TipoIdPISIS). El nombre ES el código.</summary>
public enum TipoDocumento
{
    CC,   // Cédula de ciudadanía
    CE,   // Cédula de extranjería
    CD,   // Carné diplomático
    PA,   // Pasaporte
    SC,   // Salvoconducto de permanencia
    PE,   // Permiso especial de permanencia
    PT,   // Permiso por protección temporal
    RC,   // Registro civil
    TI,   // Tarjeta de identidad
    CN,   // Certificado de nacido vivo
    AS,   // Adulto sin identificación
    MS,   // Menor sin identificación
    DE,   // Documento extranjero
    SI    // Sin identificación
}

/// <summary>Sexo (codSexo): H, M, I.</summary>
public enum Sexo
{
    Hombre,
    Mujer,
    Indeterminado
}

/// <summary>Zona territorial de residencia (codZonaTerritorialResidencia).</summary>
public enum ZonaResidencia
{
    Rural = 1,
    Urbana = 2
}

/// <summary>Tipo de usuario / régimen de afiliación (tipoUsuario).</summary>
public enum TipoUsuarioRips
{
    ContributivoCotizante = 1,
    ContributivoBeneficiario = 2,
    ContributivoAdicional = 3,
    Subsidiado = 4,
    NoAfiliado = 5,
    EspecialCotizante = 6,
    EspecialBeneficiario = 7,
    PrivadoLibertadFondoSalud = 8,
    TomadorArl = 9,
    TomadorSoat = 10,
    TomadorPlanVoluntario = 11,
    Particular = 12,
    EspecialNoCotizante = 13
}

/// <summary>
/// Finalidad de la tecnología en salud (finalidadTecnologiaSalud). Subconjunto que aplica a una
/// clínica estética; la tabla completa (maternidad, detección laboral, etc.) no se usa aquí.
/// </summary>
public enum FinalidadConsulta
{
    ValoracionIntegral = 11,
    DeteccionTemprana = 12,
    ProteccionEspecifica = 14,
    Diagnostico = 15,
    Tratamiento = 16,
    Rehabilitacion = 17,
    Paliacion = 18
}

/// <summary>En qué registro del RIPS se reporta el servicio: consultas, procedimientos u otros servicios.</summary>
public enum TipoServicioRips
{
    Consulta = 1,
    Procedimiento = 2,
    OtroServicio = 3
}

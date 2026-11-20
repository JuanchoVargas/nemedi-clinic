using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

public class Patient : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    /// <summary>Dirección de residencia (texto libre; la usa también la factura electrónica).</summary>
    public string? Direccion { get; set; }
    /// <summary>Ciudad como la escribe la clínica. El código DANE de RIPS es MunicipioResidencia.</summary>
    public string? Ciudad { get; set; }
    public string? FotoUrl { get; set; }
    /// <summary>Foto de perfil (Attachment Kind=Perfil). FotoUrl queda como campo legado sin uso en la web.</summary>
    public Guid? ImagenId { get; set; }
    public string? NotasGenerales { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Datos RIPS (Res. 2275/2023). Opcionales; obligatorios solo si la clínica es IPS (RipsRules) ──
    public TipoDocumento? TipoDocumento { get; set; }
    public Sexo? Sexo { get; set; }
    /// <summary>Código de país ISO 3166 numérico (Colombia = 170).</summary>
    public string? PaisResidencia { get; set; }
    /// <summary>Código DANE del municipio, 5 dígitos (Bogotá = 11001). Obligatorio si reside en Colombia.</summary>
    public string? MunicipioResidencia { get; set; }
    public ZonaResidencia? ZonaResidencia { get; set; }
    /// <summary>Régimen de afiliación (tipoUsuario de RIPS).</summary>
    public TipoUsuarioRips? TipoUsuario { get; set; }
    /// <summary>Código de la entidad administradora (EPS…), p. ej. EPS037. Solo si el régimen la tiene.</summary>
    public string? CodigoEntidad { get; set; }
    public string? NombreEntidad { get; set; }

    public ClinicalRecord? ClinicalRecord { get; set; }
    public ICollection<PatientPackage> PatientPackages { get; set; } = [];
}

using System.ComponentModel.DataAnnotations;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Application.DTOs.Patients;

public class CreatePatientRequest
{
    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Apellido { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Cedula { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(200)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    public string? FotoUrl { get; set; }

    /// <summary>Id de un Attachment (Kind=Perfil) subido antes con POST /files.</summary>
    public Guid? ImagenId { get; set; }

    [MaxLength(2000)]
    public string? NotasGenerales { get; set; }

    // ── Datos RIPS (obligatorios solo si la clínica es IPS) ──
    public TipoDocumento? TipoDocumento { get; set; }
    public Sexo? Sexo { get; set; }
    [MaxLength(3)]
    public string? PaisResidencia { get; set; }
    [MaxLength(5)]
    public string? MunicipioResidencia { get; set; }
    public ZonaResidencia? ZonaResidencia { get; set; }
    public TipoUsuarioRips? TipoUsuario { get; set; }
    [MaxLength(10)]
    public string? CodigoEntidad { get; set; }
    [MaxLength(150)]
    public string? NombreEntidad { get; set; }
}

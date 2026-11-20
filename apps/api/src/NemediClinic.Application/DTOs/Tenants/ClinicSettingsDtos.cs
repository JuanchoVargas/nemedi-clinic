using System.ComponentModel.DataAnnotations;

namespace NemediClinic.Application.DTOs.Tenants;

/// <summary>Configuración de la clínica en un solo lugar: datos, políticas e impuestos.</summary>
public class ClinicSettingsDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NIT { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public Guid? LogoId { get; set; }

    public int AnticipacionMinimaHoras { get; set; }
    public bool ExigirConsentimiento { get; set; }

    public decimal IvaProductos { get; set; }
    public decimal IvaServicios { get; set; }

    /// <summary>Prestador habilitado (IPS). Solo lectura: lo cambia la plataforma (afecta el precio).</summary>
    public bool EsIps { get; set; }
    public string? CodigoHabilitacionReps { get; set; }
}

public class UpdateClinicSettingsRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio."), MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El NIT es obligatorio."), MaxLength(20)]
    public string NIT { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    /// <summary>Attachment (Kind=Logo). Null = no cambiar.</summary>
    public Guid? LogoId { get; set; }

    [Range(0, 720, ErrorMessage = "La anticipación va de 0 a 720 horas.")]
    public int AnticipacionMinimaHoras { get; set; }

    public bool ExigirConsentimiento { get; set; } = true;

    [Range(0, 100)]
    public decimal IvaProductos { get; set; }

    [Range(0, 100)]
    public decimal IvaServicios { get; set; }

    /// <summary>12 dígitos. Obligatorio si la clínica es IPS.</summary>
    [MaxLength(12)]
    public string? CodigoHabilitacionReps { get; set; }
}

/// <summary>Una franja del horario de atención. DiaSemana: 0 = domingo … 6 = sábado. Horas "HH:mm".</summary>
public class BranchScheduleDayDto
{
    [Range(0, 6)]
    public int DiaSemana { get; set; }
    public TimeOnly HoraApertura { get; set; }
    public TimeOnly HoraCierre { get; set; }
}

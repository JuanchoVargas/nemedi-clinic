using System.ComponentModel.DataAnnotations;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Application.DTOs.Appointments;

public class UpdateAppointmentStatusRequest
{
    [Required]
    public AppointmentStatus Estado { get; set; }

    public string? Notas { get; set; }

    /// <summary>Obligatorio al pasar a Cancelada.</summary>
    [MaxLength(500)]
    public string? Motivo { get; set; }
}

/// <summary>Mover una cita (arrastrar en el calendario o "Reagendar"). Conserva la duración.</summary>
public class RescheduleAppointmentRequest
{
    [Required]
    public DateTime FechaInicio { get; set; }

    /// <summary>Solo al estirar la cita en el calendario; null = conserva la duración.</summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>Null = la misma esteticista.</summary>
    public Guid? EsteticistId { get; set; }
}

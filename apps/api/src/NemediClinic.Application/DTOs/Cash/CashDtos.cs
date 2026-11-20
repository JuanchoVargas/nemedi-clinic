using System.ComponentModel.DataAnnotations;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Application.DTOs.Cash;

public class OpenCashRequest
{
    /// <summary>Null = la sede del usuario (o la primera de la clínica).</summary>
    public Guid? BranchId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal MontoApertura { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }
}

public class CashMovementRequest
{
    [Required]
    public CashMovementType Tipo { get; set; }

    [Required(ErrorMessage = "Indica el concepto."), MaxLength(250)]
    public string Concepto { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue)]
    public decimal Monto { get; set; }
}

public class CloseCashRequest
{
    /// <summary>Lo que se contó en el cajón.</summary>
    [Range(0, double.MaxValue)]
    public decimal EfectivoContado { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }
}

/// <summary>Totales de la caja. Abierta: en vivo. Cerrada: los que quedaron congelados al cerrar.</summary>
public class CashSummaryDto
{
    public decimal MontoApertura { get; set; }
    public decimal TotalEfectivo { get; set; }
    public decimal TotalTransferencia { get; set; }
    public decimal TotalTarjeta { get; set; }
    public decimal TotalOtro { get; set; }
    /// <summary>Σ de todos los pagos (todos los métodos).</summary>
    public decimal TotalPagos { get; set; }
    public decimal TotalIngresos { get; set; }
    public decimal TotalEgresos { get; set; }
    /// <summary>Base + efectivo + ingresos − egresos.</summary>
    public decimal EfectivoEsperado { get; set; }
    public int CantidadPagos { get; set; }
}

public class CashMovementDto
{
    public Guid Id { get; set; }
    /// <summary>"Pago" (automático, cualquier método) | "Ingreso" | "Egreso" (manuales).</summary>
    public string Tipo { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string? MetodoPago { get; set; }
    public string? Referencia { get; set; }
    public DateTime Fecha { get; set; }
    public string? Usuario { get; set; }
    public Guid? SaleId { get; set; }
    public int? SaleNumero { get; set; }
    public Guid? PatientId { get; set; }
    public string? PacienteNombre { get; set; }
    /// <summary>Pago de una venta que después se anuló: se muestra tachado y no suma.</summary>
    public bool Anulado { get; set; }
}

public class CashSessionDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchNombre { get; set; } = string.Empty;
    public Guid UsuarioId { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaApertura { get; set; }
    public string? ObservacionApertura { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string? CerradaPor { get; set; }
    public string? ObservacionCierre { get; set; }
    public decimal? EfectivoContado { get; set; }
    /// <summary>Contado − esperado. Negativa = faltante.</summary>
    public decimal? Diferencia { get; set; }
    /// <summary>La caja sigue abierta desde un día anterior: hay que cerrarla.</summary>
    public bool AbiertaDesdeOtroDia { get; set; }
    /// <summary>¿Quien consulta puede registrar movimientos y cerrarla? (su dueño, o el SuperAdmin).</summary>
    public bool PuedeOperar { get; set; }
    public CashSummaryDto Resumen { get; set; } = new();
    /// <summary>Solo en el detalle.</summary>
    public List<CashMovementDto> Movimientos { get; set; } = [];
}

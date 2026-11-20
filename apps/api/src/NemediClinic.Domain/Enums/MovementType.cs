namespace NemediClinic.Domain.Enums;

public enum MovementType
{
    Entrada = 0,
    Salida = 1,
    /// <summary>Retiro de un lote (vencido, dañado): sale del inventario pero no es consumo.</summary>
    Baja = 2
}

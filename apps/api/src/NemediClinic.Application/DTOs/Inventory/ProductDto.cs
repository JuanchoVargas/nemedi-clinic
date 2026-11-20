namespace NemediClinic.Application.DTOs.Inventory;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public string? Categoria { get; set; }
    /// <summary>Costo de compra por unidad del lote más reciente que lo tiene; null si ningún lote trae costo.</summary>
    public decimal? UltimoCosto { get; set; }
    public string TipoProducto { get; set; } = string.Empty; // enum serializado
    public string UnidadMedida { get; set; } = string.Empty;
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal? StockMaximo { get; set; }
    public bool Activo { get; set; }
    public Guid? ImagenId { get; set; }
    /// <summary>Precio de venta sin IVA (0 = sin precio: no aparece en el buscador de ventas).</summary>
    public decimal PrecioVenta { get; set; }
    /// <summary>IVA propio; null = el de productos de la clínica.</summary>
    public decimal? IvaPorcentaje { get; set; }
    /// <summary>"Verde" | "Amarillo" | "Rojo"</summary>
    public string SemaforoStock { get; set; } = string.Empty;

    // ── Trazabilidad sanitaria ──
    public string TipoRegulatorio { get; set; } = string.Empty;
    public string? RegistroSanitarioInvima { get; set; }
    public string? PrincipioActivo { get; set; }
    public string? Concentracion { get; set; }
    public bool RequiereCadenaFrio { get; set; }
    /// <summary>Peor estado entre sus lotes con existencia: null si el producto no maneja lotes.</summary>
    public string? SemaforoLotes { get; set; }
    public int LotesPorVencer { get; set; }
    public int LotesVencidos { get; set; }
    public DateTime CreatedAt { get; set; }
}

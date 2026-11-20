using System.ComponentModel.DataAnnotations.Schema;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

public class Product : BaseEntity
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    /// <summary>Categoría libre de la clínica (Facial, Corporal, Capilar…). La web sugiere las ya usadas.</summary>
    public string? Categoria { get; set; }
    public ProductType TipoProducto { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public decimal StockActual { get; set; } = 0m;
    public decimal StockMinimo { get; set; } = 0m;
    public decimal? StockMaximo { get; set; }
    public bool Activo { get; set; } = true;
    public Guid? ImagenId { get; set; }

    // ── Venta ──
    /// <summary>Precio de venta al público, sin IVA. 0 = no tiene precio de venta definido.</summary>
    public decimal PrecioVenta { get; set; }
    /// <summary>IVA propio del producto (%). Null = el IVA de productos configurado en la clínica.</summary>
    public decimal? IvaPorcentaje { get; set; }

    // ── Trazabilidad sanitaria (lo que pide la Secretaría de Salud) ──
    /// <summary>Cómo lo clasifica el INVIMA: define qué hay que reportar de él.</summary>
    public RegulatoryType TipoRegulatorio { get; set; } = RegulatoryType.Insumo;
    /// <summary>Registro sanitario INVIMA. Obligatorio en la práctica para medicamentos y dispositivos.</summary>
    public string? RegistroSanitarioInvima { get; set; }
    public string? PrincipioActivo { get; set; }
    /// <summary>"2 %", "100 UI/ml"… tal como aparece en la etiqueta.</summary>
    public string? Concentracion { get; set; }
    public bool RequiereCadenaFrio { get; set; }

    /// <summary>Lotes del producto. El stock sale de aquí (ver ProductLot).</summary>
    public ICollection<ProductLot> Lotes { get; set; } = [];

    /// <summary>
    /// Estado de stock calculado, NO mapeado en DB.
    /// Verde: stock >= mínimo. Amarillo: mínimo/2 ≤ stock < mínimo.
    /// Rojo: stock == 0 o stock < mínimo/2.
    /// La proyección equivalente en SQL está duplicada en
    /// ProductsController (proyección a ProductDto.SemaforoStock).
    /// </summary>
    [NotMapped]
    public StockStatus SemaforoStock
    {
        get
        {
            if (StockActual == 0m || StockActual < StockMinimo * 0.5m)
                return StockStatus.Rojo;
            if (StockActual < StockMinimo)
                return StockStatus.Amarillo;
            return StockStatus.Verde;
        }
    }
}

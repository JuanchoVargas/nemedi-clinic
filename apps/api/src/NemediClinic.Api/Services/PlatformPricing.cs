using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Api.Services;

/// <summary>
/// Tarifas mensuales (COP) para la liquidación. Se leen de la sección "Platform:Precios"
/// de la configuración; los valores de este repositorio son de ejemplo, no la lista de precios real.
/// </summary>
public class PlatformPricing
{
    public decimal Basico { get; set; } = 100_000m;
    public decimal Pro { get; set; } = 200_000m;
    public decimal SedeAdicional { get; set; } = 50_000m;
    /// <summary>Recargo fijo mensual para tenants IPS (RIPS, habilitación).</summary>
    public decimal RecargoIps { get; set; } = 0m;

    public decimal PrecioMensual(Tenant tenant) =>
        (tenant.Plan == TenantPlan.Pro ? Pro : Basico)
        + tenant.SedesAdicionales * SedeAdicional
        + (tenant.EsIps ? RecargoIps : 0m);
}

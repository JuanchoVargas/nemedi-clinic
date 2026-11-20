using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Inventory;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttachmentService _attachments;
    private readonly RegulatoryReportService _regulatory;
    private readonly ProductLotService _lots;

    public ProductsController(AppDbContext db, AttachmentService attachments, RegulatoryReportService regulatory, ProductLotService lots)
    {
        _db = db;
        _attachments = attachments;
        _regulatory = regulatory;
        _lots = lots;
    }

    // ── GET /api/v1/products?Page=&PageSize=&Search=&tipo=&semaforo= ──
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] PagedRequest request,
        [FromQuery] ProductType? tipo,
        [FromQuery] string? semaforo,
        [FromQuery] RegulatoryType? tipoRegulatorio,
        [FromQuery] string? categoria)
    {
        var query = _db.Products.AsNoTracking();

        // "sin" = productos sin categoría; cualquier otro valor, esa categoría (sin distinguir mayúsculas: la collation de SQL Server ya no lo hace)
        if (categoria == "sin")
            query = query.Where(p => p.Categoria == null);
        else if (!string.IsNullOrWhiteSpace(categoria))
            query = query.Where(p => p.Categoria == categoria.Trim());

        if (tipoRegulatorio.HasValue)
            query = query.Where(p => p.TipoRegulatorio == tipoRegulatorio.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(p =>
                p.Nombre.ToLower().Contains(search) ||
                (p.Referencia != null && p.Referencia.ToLower().Contains(search)));
        }

        if (tipo.HasValue)
            query = query.Where(p => p.TipoProducto == tipo.Value);

        query = ApplySemaforoFilter(query, semaforo);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.Nombre)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(ProjectExpression())
            .ToListAsync();

        return Ok(new PagedResponse<ProductDto>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    // ── GET /api/v1/products/categories ───────────────────────────────
    /// <summary>Las categorías ya usadas en la clínica, para sugerirlas en el formulario y filtrar la grilla.</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<string>>> GetCategories(CancellationToken ct) =>
        Ok(await _db.Products.AsNoTracking().Where(p => p.Categoria != null)
            .Select(p => p.Categoria!).Distinct().OrderBy(c => c).ToListAsync(ct));

    // ── GET /api/v1/products/alerts ───────────────────────────────────
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts()
    {
        // Solo productos con stock < mínimo (Amarillo + Rojo).
        // Filtramos StockMinimo > 0 para evitar división por cero al ordenar.
        var items = await _db.Products
            .AsNoTracking()
            .Where(p => p.Activo && p.StockMinimo > 0 && p.StockActual < p.StockMinimo)
            .OrderBy(p => p.StockActual / p.StockMinimo) // más urgente primero
            .ThenBy(p => p.Nombre)
            .Select(ProjectExpression())
            .ToListAsync();

        return Ok(items);
    }

    // ── GET /api/v1/products/{id} ─────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ProjectExpression())
            .FirstOrDefaultAsync();

        if (product is null)
            return NotFound(new { error = "Producto no encontrado." });

        return Ok(product);
    }

    // ── POST /api/v1/products ─────────────────────────────────────────
    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var product = new Product
        {
            Nombre = request.Nombre,
            Descripcion = request.Descripcion ?? "",
            Referencia = request.Referencia,
            Categoria = await ProductCategories.NormalizeAsync(_db, request.Categoria),
            TipoProducto = request.TipoProducto,
            TipoRegulatorio = request.TipoRegulatorio,
            RegistroSanitarioInvima = request.RegistroSanitarioInvima,
            PrincipioActivo = request.PrincipioActivo,
            Concentracion = request.Concentracion,
            RequiereCadenaFrio = request.RequiereCadenaFrio,
            UnidadMedida = request.UnidadMedida,
            StockActual = 0m,
            StockMinimo = request.StockMinimo,
            StockMaximo = request.StockMaximo,
            ImagenId = request.ImagenId,
            PrecioVenta = request.PrecioVenta,
            IvaPorcentaje = request.IvaPorcentaje
        };

        _db.Products.Add(product);
        await _attachments.AssignImageAsync(AttachmentEntityType.Product, product.Id, request.ImagenId, null);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id },
            await BuildDtoAsync(product.Id));
    }

    // ── PUT /api/v1/products/{id} ─────────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
            return NotFound(new { error = "Producto no encontrado." });

        if (request.Nombre is not null) product.Nombre = request.Nombre;
        if (request.Descripcion is not null) product.Descripcion = request.Descripcion;
        if (request.Referencia is not null) product.Referencia = request.Referencia;
        // "" quita la categoría
        if (request.Categoria is not null) product.Categoria = await ProductCategories.NormalizeAsync(_db, request.Categoria);
        if (request.TipoProducto.HasValue) product.TipoProducto = request.TipoProducto.Value;
        if (request.UnidadMedida is not null) product.UnidadMedida = request.UnidadMedida;
        if (request.StockMinimo.HasValue) product.StockMinimo = request.StockMinimo.Value;
        if (request.StockMaximo.HasValue) product.StockMaximo = request.StockMaximo;
        if (request.Activo.HasValue) product.Activo = request.Activo.Value;
        // Trazabilidad sanitaria: antes el PUT las ignoraba y un producto no se podía reclasificar
        if (request.TipoRegulatorio.HasValue) product.TipoRegulatorio = request.TipoRegulatorio.Value;
        if (request.RegistroSanitarioInvima is not null) product.RegistroSanitarioInvima = Limpio(request.RegistroSanitarioInvima);
        if (request.PrincipioActivo is not null) product.PrincipioActivo = Limpio(request.PrincipioActivo);
        if (request.Concentracion is not null) product.Concentracion = Limpio(request.Concentracion);
        if (request.RequiereCadenaFrio.HasValue) product.RequiereCadenaFrio = request.RequiereCadenaFrio.Value;
        if (request.PrecioVenta.HasValue) product.PrecioVenta = request.PrecioVenta.Value;
        if (request.IvaPorDefecto == true) product.IvaPorcentaje = null;
        else if (request.IvaPorcentaje.HasValue) product.IvaPorcentaje = request.IvaPorcentaje.Value;
        if (request.ImagenId.HasValue)
        {
            await _attachments.AssignImageAsync(AttachmentEntityType.Product, product.Id, request.ImagenId, product.ImagenId);
            product.ImagenId = request.ImagenId;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── DELETE /api/v1/products/{id} ──────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
            return NotFound(new { error = "Producto no encontrado." });

        product.IsDeleted = true;
        product.Activo = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers privados ──────────────────────────────────────────────

    private static string? Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static IQueryable<Product> ApplySemaforoFilter(IQueryable<Product> q, string? semaforo)
    {
        if (string.IsNullOrEmpty(semaforo)) return q;
        return semaforo switch
        {
            "Verde" => q.Where(p => p.StockActual >= p.StockMinimo),
            "Amarillo" => q.Where(p =>
                p.StockActual > 0
                && p.StockActual >= p.StockMinimo * 0.5m
                && p.StockActual < p.StockMinimo),
            "Rojo" => q.Where(p =>
                p.StockActual == 0m
                || p.StockActual < p.StockMinimo * 0.5m),
            _ => q
        };
    }

    /// <summary>
    /// Proyección a DTO con SemaforoStock calculado en SQL.
    /// Lógica: Rojo si stock==0 o stock < min/2; Amarillo si stock < min;
    /// Verde en otro caso.
    /// </summary>
    private static System.Linq.Expressions.Expression<System.Func<Product, ProductDto>> ProjectExpression()
    {
        return p => new ProductDto
        {
            Id = p.Id,
            Nombre = p.Nombre,
            Descripcion = p.Descripcion,
            Referencia = p.Referencia,
            Categoria = p.Categoria,
            UltimoCosto = p.Lotes
                .Where(l => l.CostoUnitario != null)
                .OrderByDescending(l => l.FechaIngreso).ThenByDescending(l => l.CreatedAt)
                .Select(l => l.CostoUnitario)
                .FirstOrDefault(),
            TipoProducto = p.TipoProducto.ToString(),
            TipoRegulatorio = p.TipoRegulatorio.ToString(),
            RegistroSanitarioInvima = p.RegistroSanitarioInvima,
            PrincipioActivo = p.PrincipioActivo,
            Concentracion = p.Concentracion,
            RequiereCadenaFrio = p.RequiereCadenaFrio,
            UnidadMedida = p.UnidadMedida,
            StockActual = p.StockActual,
            StockMinimo = p.StockMinimo,
            StockMaximo = p.StockMaximo,
            Activo = p.Activo,
            ImagenId = p.ImagenId,
            PrecioVenta = p.PrecioVenta,
            IvaPorcentaje = p.IvaPorcentaje,
            SemaforoStock =
                p.StockActual == 0m || p.StockActual < p.StockMinimo * 0.5m
                    ? "Rojo"
                    : p.StockActual < p.StockMinimo
                        ? "Amarillo"
                        : "Verde",
            CreatedAt = p.CreatedAt
        };
    }

    // ── GET /api/v1/products/{id}/lots ────────────────────────────────
    /// <summary>Lotes del producto, del que vence antes al que vence después.</summary>
    [HttpGet("{id:guid}/lots")]
    public async Task<ActionResult<List<ProductLotDto>>> GetLots(Guid id, CancellationToken ct)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == id, ct))
            return NotFound(new { error = "Producto no encontrado." });

        return Ok(await _regulatory.GetLotsAsync(id, ct));
    }

    // ── POST /api/v1/products/{id}/lots/{lotId}/write-off ─────────────
    /// <summary>Da de baja lo que queda de un lote (vencido, dañado…). Queda un movimiento "Baja" con el motivo.</summary>
    [HttpPost("{id:guid}/lots/{lotId:guid}/write-off")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> WriteOffLot(Guid id, Guid lotId, [FromBody] WriteOffLotRequest request, CancellationToken ct)
    {
        await _lots.WriteOffAsync(id, lotId, request.Motivo, User.UserId(), ct);
        return Ok(await _regulatory.GetLotsAsync(id, ct));
    }

    // ── GET /api/v1/products/lot-alerts ───────────────────────────────
    /// <summary>Lotes vencidos o por vencer (90 días o menos) con existencia. Para el tab de Alertas.</summary>
    [HttpGet("lot-alerts")]
    public async Task<ActionResult<List<LotAlertaDto>>> GetLotAlerts(CancellationToken ct) =>
        Ok(await _regulatory.GetLotAlertsAsync(ct));

    // ── GET /api/v1/products/regulatory-report?desde=&hasta=&tipo= ────
    /// <summary>
    /// Reporte para la Secretaría de Salud: por tipo regulatorio, cada producto con su registro
    /// INVIMA, sus lotes con vencimiento, lo disponible y lo consumido en el periodo.
    /// Con Accept: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet (o ?formato=xlsx)
    /// se descarga en Excel.
    /// </summary>
    [HttpGet("regulatory-report")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> GetRegulatoryReport(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        [FromQuery] RegulatoryType? tipo, [FromQuery] string? formato, CancellationToken ct)
    {
        var reporte = await _regulatory.BuildAsync(desde, hasta, tipo, ct);

        if (!string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase))
            return Ok(reporte);

        var clinica = await _db.Tenants.Select(t => t.Nombre).FirstOrDefaultAsync(ct) ?? "Clínica";
        var bytes = RegulatoryReportService.ToExcel(reporte, clinica);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"inventario-secretaria-salud-{reporte.Desde:yyyy-MM-dd}-a-{reporte.Hasta:yyyy-MM-dd}.xlsx");
    }

    private async Task<ProductDto?> BuildDtoAsync(Guid id)
    {
        return await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(ProjectExpression())
            .FirstOrDefaultAsync();
    }
}

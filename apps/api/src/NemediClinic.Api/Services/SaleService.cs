using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Sales;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Ventas: la capa sobre la que se monta la facturación electrónica.
///
/// Reglas:
///   · Una línea copia del catálogo la descripción, el precio (sin IVA) y el IVA: el IVA sale del
///     ítem si lo fija, si no de la clínica (productos 19 %, servicios 0 % por defecto). Solo una
///     línea "Otro" trae su propio IVA. Un paquete se vende de a uno y exige paciente.
///   · Borrador no mueve nada. Confirmar, en una transacción: los productos salen por FEFO (un
///     movimiento por lote, ligado a la venta y al paciente), cada paquete se asigna al paciente
///     (PatientPackage con SaleId) y los pagos se registran en la caja abierta de quien cobra.
///   · Los pagos nunca superan el saldo (422). Se aplican a las líneas en el orden en que se
///     digitaron: lo que cae en un paquete lleva también su PatientPackageId, así la ficha del
///     paciente ve el abono en el paquete y la venta lo ve en su saldo, sin contarlo dos veces.
///   · Anular revierte todo en una transacción y es idempotente: devuelve el stock al mismo lote,
///     retira los paquetes (409 si alguno ya tiene sesiones realizadas) y anula los pagos; el
///     efectivo cobrado en una caja ya cerrada sale como egreso de la caja abierta de quien anula.
///   · Facturación electrónica: InvoiceService (501 si la clínica no la configuró). Anular una venta
///     facturada emite antes la nota crédito; si el proveedor la rechaza, la venta no se anula.
/// </summary>
public class SaleService
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;
    private readonly ProductLotService _lots;
    private readonly PatientPackageService _packages;
    private readonly CashService _cash;
    private readonly AttachmentService _attachments;
    private readonly InvoiceService _invoices;

    public SaleService(AppDbContext db, ProductLotService lots, PatientPackageService packages, CashService cash,
        AttachmentService attachments, InvoiceService invoices, IClock clock)
    {
        _clock = clock;
        _db = db;
        _lots = lots;
        _packages = packages;
        _cash = cash;
        _attachments = attachments;
        _invoices = invoices;
    }

    // ── Cotizar ─────────────────────────────────────────────────────

    /// <summary>Calcula líneas y totales sin guardar nada. La web la usa mientras se arma la venta.</summary>
    public async Task<SaleQuoteDto> QuoteAsync(QuoteSaleRequest request, CancellationToken ct = default)
    {
        var lineas = await BuildLinesAsync(request.Lineas, patientId: null, validarPaciente: false, ct);
        var dto = new SaleQuoteDto
        {
            Lineas = lineas.Select(ToLineDto).ToList(),
            Subtotal = lineas.Sum(l => l.Subtotal),
            Descuento = lineas.Sum(l => l.Descuento),
            Impuestos = lineas.Sum(l => l.Iva),
            Total = lineas.Sum(l => l.Total)
        };
        return dto;
    }

    // ── Crear / editar / confirmar ──────────────────────────────────

    public async Task<SaleDto> CreateAsync(SaveSaleRequest request, Guid userId, Guid? userBranchId, CancellationToken ct = default)
    {
        if (!request.Confirmar && request.Pagos.Count > 0)
            throw ApiException.BadRequest("Un borrador no recibe pagos: confírmalo para cobrar.");

        var branchId = await BranchLookup.ResolveAsync(_db, request.BranchId, userBranchId, ct);
        await ValidatePatientAsync(request.PatientId, ct);
        var lineas = await BuildLinesAsync(request.Lineas, request.PatientId, validarPaciente: true, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var sale = new Sale
        {
            Numero = await NextNumberAsync(ct),
            BranchId = branchId,
            PatientId = request.PatientId,
            ClienteNombre = request.PatientId.HasValue ? null : Limpio(request.ClienteNombre),
            ClienteDocumento = request.PatientId.HasValue ? null : Limpio(request.ClienteDocumento),
            Observacion = Limpio(request.Observacion),
            Fecha = _clock.Now,
            Estado = SaleStatus.Borrador,
            UsuarioId = userId
        };
        _db.Sales.Add(sale);
        AttachLines(sale, lineas);

        if (request.Confirmar)
            await ConfirmInternalAsync(sale, request.Pagos, userId, ct);

        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(sale.Id, ct);
    }

    /// <summary>Reemplaza las líneas y los datos de un borrador (y lo confirma si se pide).</summary>
    public async Task<SaleDto> UpdateDraftAsync(Guid id, SaveSaleRequest request, Guid userId, Guid? userBranchId, CancellationToken ct = default)
    {
        var sale = await _db.Sales.Include(s => s.Lineas).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");
        if (sale.Estado != SaleStatus.Borrador)
            throw ApiException.Conflict("Solo se puede editar una venta en borrador.");
        if (!request.Confirmar && request.Pagos.Count > 0)
            throw ApiException.BadRequest("Un borrador no recibe pagos: confírmalo para cobrar.");

        sale.BranchId = await BranchLookup.ResolveAsync(_db, request.BranchId, userBranchId, ct);
        await ValidatePatientAsync(request.PatientId, ct);
        var lineas = await BuildLinesAsync(request.Lineas, request.PatientId, validarPaciente: true, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        sale.PatientId = request.PatientId;
        sale.ClienteNombre = request.PatientId.HasValue ? null : Limpio(request.ClienteNombre);
        sale.ClienteDocumento = request.PatientId.HasValue ? null : Limpio(request.ClienteDocumento);
        sale.Observacion = Limpio(request.Observacion);
        foreach (var anterior in sale.Lineas) anterior.IsDeleted = true;
        AttachLines(sale, lineas);

        if (request.Confirmar)
            await ConfirmInternalAsync(sale, request.Pagos, userId, ct);

        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(sale.Id, ct);
    }

    public async Task<SaleDto> ConfirmAsync(Guid id, ConfirmSaleRequest request, Guid userId, CancellationToken ct = default)
    {
        var sale = await _db.Sales.Include(s => s.Lineas).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");
        if (sale.Estado != SaleStatus.Borrador)
            throw ApiException.Conflict(sale.Estado == SaleStatus.Confirmada ? "La venta ya está confirmada." : "La venta está anulada.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ConfirmInternalAsync(sale, request.Pagos, userId, ct);
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(sale.Id, ct);
    }

    /// <summary>Abono a una venta confirmada con saldo (cartera).</summary>
    public async Task<SaleDto> AddPaymentAsync(Guid id, SalePaymentRequest request, Guid userId, CancellationToken ct = default)
    {
        var sale = await _db.Sales.Include(s => s.Lineas).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");
        if (sale.Estado != SaleStatus.Confirmada)
            throw ApiException.Conflict("Solo se registran pagos en una venta confirmada.");

        var pagado = await _db.Payments.Where(p => p.SaleId == id).SumAsync(p => p.Monto, ct);
        var saldo = sale.Total - pagado;
        if (request.Monto > saldo)
            throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                $"El pago supera el saldo pendiente ({SaleMath.Pesos(Math.Max(saldo, 0))})");

        var cajaId = await _cash.OpenSessionIdAsync(userId, ct);
        await AllocatePaymentAsync(sale, request, userId, cajaId, ct);
        // La RowVersion de la venta convierte dos abonos simultáneos en un 409 en vez de un saldo negativo
        _db.Entry(sale).State = EntityState.Modified;
        await SaveAsync(ct);
        return await GetAsync(sale.Id, ct);
    }

    /// <summary>Un borrador se puede descartar sin dejar rastro (no tuvo efectos).</summary>
    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var sale = await _db.Sales.Include(s => s.Lineas).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");
        if (sale.Estado != SaleStatus.Borrador)
            throw ApiException.Conflict("Una venta confirmada no se elimina: se anula.");
        sale.IsDeleted = true;
        foreach (var linea in sale.Lineas) linea.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    // ── Anular ──────────────────────────────────────────────────────

    public async Task<VoidSaleResultDto> VoidAsync(Guid id, VoidSaleRequest request, Guid userId, CancellationToken ct = default)
    {
        var sale = await _db.Sales.Include(s => s.Lineas).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");

        // Idempotente: anular dos veces no revierte dos veces
        if (sale.Estado == SaleStatus.Anulada)
            return new VoidSaleResultDto { Venta = await GetAsync(id, ct), YaEstabaAnulada = true };

        if (string.IsNullOrWhiteSpace(request.Motivo))
            throw ApiException.BadRequest("Indica el motivo de la anulación.");
        if (sale.FacturaEstado is ElectronicInvoiceStatus.Emitida or ElectronicInvoiceStatus.Pendiente)
        {
            // La nota crédito sale ANTES de revertir: primero lo que haría fallar la anulación
            // (paquetes con sesiones), para no acreditar ante la DIAN una venta que sigue viva.
            var paquetes = LiveLines(sale).Where(l => l.PatientPackageId.HasValue).Select(l => l.PatientPackageId!.Value).ToList();
            var usados = await _db.PatientPackages.Where(p => paquetes.Contains(p.Id) && p.SesionesCompletadas > 0)
                .Select(p => p.PackageNombre + " ya tiene " + p.SesionesCompletadas + " sesión(es) realizada(s)").ToListAsync(ct);
            if (usados.Count > 0)
                throw ApiException.Conflict($"No se puede anular: {string.Join(", ", usados)}.");
            await _invoices.AnnulForSaleAsync(sale.Id, request.Motivo.Trim(), userId, ct);
            _db.ChangeTracker.Clear();
            sale = await _db.Sales.Include(s => s.Lineas).FirstAsync(s => s.Id == id, ct);
        }

        var resultado = new VoidSaleResultDto();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (sale.Estado == SaleStatus.Confirmada)
        {
            resultado.PaquetesRevertidos = await RevertPackagesAsync(sale, ct);
            resultado.MovimientosRevertidos = await RevertInventoryAsync(sale, userId, ct);
            (resultado.PagosAnulados, resultado.DevolucionEfectivo, resultado.DevolucionFueraDeCaja) =
                await VoidPaymentsAsync(sale, userId, ct);
        }

        sale.Estado = SaleStatus.Anulada;
        sale.FechaAnulacion = _clock.Now;
        sale.AnuladaPorId = userId;
        sale.MotivoAnulacion = request.Motivo.Trim();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra anulación (o un abono) llegó primero. Si fue una anulación, el resultado es el mismo.
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var estado = await _db.Sales.Where(s => s.Id == id).Select(s => s.Estado).FirstAsync(ct);
            if (estado == SaleStatus.Anulada)
                return new VoidSaleResultDto { Venta = await GetAsync(id, ct), YaEstabaAnulada = true };
            throw ApiException.Conflict("La venta cambió mientras la anulabas. Revísala e inténtalo de nuevo.");
        }

        await tx.CommitAsync(ct);
        resultado.Venta = await GetAsync(id, ct);
        return resultado;
    }

    // ── Consultas ───────────────────────────────────────────────────

    public async Task<SaleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var sale = await _db.Sales.AsNoTracking()
            .Include(s => s.Lineas)
            .Include(s => s.Branch)
            .Include(s => s.Patient)
            .Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");

        var tenantId = _db.CurrentTenantId;
        // Con los anulados (tachados en el detalle); sin los borrados por error
        var pagos = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.SaleId == id && (!p.IsDeleted || p.FechaAnulacion != null))
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

        var userIds = pagos.Where(p => p.RegistradoPorId.HasValue).Select(p => p.RegistradoPorId!.Value)
            .Append(sale.AnuladaPorId ?? Guid.Empty).Distinct().ToList();
        var usuarios = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.Nombre + " " + u.Apellido).Trim(), ct);

        var dto = ToDto(sale, pagos.Where(p => p.FechaAnulacion == null).Sum(p => p.Monto));
        dto.Lineas = sale.Lineas.Where(l => !l.IsDeleted).OrderBy(l => l.Orden).Select(ToLineDto).ToList();
        dto.AnuladaPor = sale.AnuladaPorId.HasValue ? usuarios.GetValueOrDefault(sale.AnuladaPorId.Value) : null;
        dto.Pagos = pagos.Select(p => new SalePaymentDto
        {
            Id = p.Id,
            Monto = p.Monto,
            FechaPago = p.FechaPago,
            RegistradoEl = _clock.ToLocal(p.CreatedAt),
            MetodoPago = p.MetodoPago.ToString(),
            Referencia = p.Referencia,
            Observacion = p.Observacion,
            RegistradoPor = p.RegistradoPorId.HasValue ? usuarios.GetValueOrDefault(p.RegistradoPorId.Value) : null,
            ComprobanteId = p.ComprobanteId,
            PatientPackageId = p.PatientPackageId,
            AplicadoA = p.PatientPackageId is { } ppId
                ? sale.Lineas.FirstOrDefault(l => l.PatientPackageId == ppId)?.Descripcion
                : null,
            Anulado = p.FechaAnulacion != null
        }).ToList();
        return dto;
    }

    public async Task<PagedResponse<SaleDto>> ListAsync(SaleListFilter filtro, CancellationToken ct = default)
    {
        var query = _db.Sales.AsNoTracking();

        if (filtro.Desde.HasValue)
            query = query.Where(s => s.Fecha >= filtro.Desde.Value.ToDateTime(TimeOnly.MinValue));
        if (filtro.Hasta.HasValue)
            query = query.Where(s => s.Fecha < filtro.Hasta.Value.ToDateTime(TimeOnly.MinValue).AddDays(1));
        if (filtro.Estado.HasValue)
            query = query.Where(s => s.Estado == filtro.Estado.Value);
        if (filtro.BranchId.HasValue)
            query = query.Where(s => s.BranchId == filtro.BranchId.Value);
        if (filtro.PatientId.HasValue)
            query = query.Where(s => s.PatientId == filtro.PatientId.Value);
        if (filtro.ConSaldo == true)
            query = query.Where(s => s.Estado == SaleStatus.Confirmada && s.Total > s.Pagos.Sum(p => p.Monto));
        if (!string.IsNullOrWhiteSpace(filtro.Search))
        {
            var search = filtro.Search.Trim().ToLower();
            var numero = int.TryParse(search.TrimStart('#'), out var n) ? n : -1;
            query = query.Where(s =>
                s.Numero == numero
                || (s.Patient != null && (s.Patient.Nombre + " " + s.Patient.Apellido).ToLower().Contains(search))
                || (s.Patient != null && s.Patient.Cedula.Contains(search))
                || (s.ClienteNombre != null && s.ClienteNombre.ToLower().Contains(search))
                || (s.ClienteDocumento != null && s.ClienteDocumento.Contains(search)));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(filtro.Page, 1);
        var pageSize = Math.Clamp(filtro.PageSize, 1, 100);

        var rows = await query
            .OrderByDescending(s => s.Fecha).ThenByDescending(s => s.Numero)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                Sale = s,
                BranchNombre = s.Branch.Nombre,
                Paciente = s.Patient != null ? s.Patient.Nombre + " " + s.Patient.Apellido : null,
                Cedula = s.Patient != null ? s.Patient.Cedula : null,
                Vendedor = s.Usuario.Nombre + " " + s.Usuario.Apellido,
                Pagado = s.Pagos.Sum(p => p.Monto),
                Tipos = s.Lineas.Where(l => !l.IsDeleted).Select(l => l.Tipo).ToList()
            })
            .ToListAsync(ct);

        return new PagedResponse<SaleDto>
        {
            Items = rows.Select(r =>
            {
                r.Sale.Branch = new Branch { Nombre = r.BranchNombre };
                r.Sale.Usuario = new User { Nombre = r.Vendedor };
                var dto = ToDto(r.Sale, r.Pagado, r.Paciente, r.Cedula);
                dto.Resumen = Resumen(r.Tipos);
                return dto;
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    /// <summary>Buscador de "Nueva venta": productos con precio (no insumos de cabina), procedimientos y paquetes activos.</summary>
    public async Task<List<SaleCatalogItemDto>> CatalogAsync(string? search, CancellationToken ct = default)
    {
        var iva = await TenantTaxAsync(ct);
        var texto = search?.Trim().ToLower() ?? string.Empty;
        const int porTipo = 8;

        var productos = await _db.Products.AsNoTracking()
            .Where(p => p.Activo && p.TipoProducto != ProductType.InsumoCabina && p.PrecioVenta > 0
                && (texto == "" || p.Nombre.ToLower().Contains(texto) || (p.Referencia != null && p.Referencia.ToLower().Contains(texto))))
            .OrderBy(p => p.Nombre).Take(porTipo)
            .Select(p => new SaleCatalogItemDto
            {
                Tipo = nameof(SaleLineType.Producto), Id = p.Id, Nombre = p.Nombre, Detalle = p.Referencia,
                Precio = p.PrecioVenta, IvaPorcentaje = p.IvaPorcentaje ?? iva.Productos,
                Stock = p.StockActual, UnidadMedida = p.UnidadMedida, ImagenId = p.ImagenId
            })
            .ToListAsync(ct);

        var procedimientos = await _db.Procedures.AsNoTracking()
            .Where(p => p.Activo && (texto == "" || p.Nombre.ToLower().Contains(texto)))
            .OrderBy(p => p.Nombre).Take(porTipo)
            .Select(p => new SaleCatalogItemDto
            {
                Tipo = nameof(SaleLineType.Procedimiento), Id = p.Id, Nombre = p.Nombre,
                Detalle = p.DuracionMinutos + " min · " + p.AreaCorporal,
                Precio = p.PrecioBase, IvaPorcentaje = p.IvaPorcentaje ?? iva.Servicios, ImagenId = p.ImagenId
            })
            .ToListAsync(ct);

        var paquetes = await _db.Packages.AsNoTracking()
            .Where(p => p.Activo && (texto == "" || p.Nombre.ToLower().Contains(texto)))
            .OrderBy(p => p.Nombre).Take(porTipo)
            .Select(p => new SaleCatalogItemDto
            {
                Tipo = nameof(SaleLineType.Paquete), Id = p.Id, Nombre = p.Nombre,
                Detalle = p.SesionesTotales + " sesiones · " + p.VigenciaDias + " días",
                Precio = p.PrecioTotal, IvaPorcentaje = p.IvaPorcentaje ?? iva.Servicios
            })
            .ToListAsync(ct);

        return [.. productos, .. procedimientos, .. paquetes];
    }

    // ── Facturación electrónica ─────────────────────────────────────

    /// <summary>Emite la factura electrónica de la venta (InvoiceService). 501 si la clínica no la configuró.</summary>
    public async Task<SaleDto> IssueInvoiceAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await _invoices.IssueForSaleAsync(id, userId, ct);
        return await GetAsync(id, ct);
    }

    // ── Internos: líneas ────────────────────────────────────────────

    private sealed record TenantTax(decimal Productos, decimal Servicios);

    private async Task<TenantTax> TenantTaxAsync(CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        var iva = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantTax(t.IvaProductos, t.IvaServicios))
            .FirstOrDefaultAsync(ct);
        return iva ?? new TenantTax(19m, 0m);
    }

    /// <summary>Resuelve cada línea contra el catálogo y calcula base, IVA y total. No guarda.</summary>
    private async Task<List<SaleLine>> BuildLinesAsync(List<SaleLineRequest> requests, Guid? patientId, bool validarPaciente, CancellationToken ct)
    {
        if (requests.Count == 0)
            throw ApiException.BadRequest("La venta necesita al menos una línea.");

        var iva = await TenantTaxAsync(ct);
        var ids = requests.Where(r => r.ReferenciaId.HasValue).Select(r => r.ReferenciaId!.Value).Distinct().ToList();
        var productos = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var procedimientos = await _db.Procedures.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var paquetes = await _db.Packages.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lineas = new List<SaleLine>();
        var orden = 1;
        foreach (var r in requests)
        {
            if (r.Cantidad <= 0)
                throw ApiException.BadRequest("La cantidad de cada línea debe ser mayor a cero.");
            if (r.Descuento < 0)
                throw ApiException.BadRequest("El descuento no puede ser negativo.");

            string descripcion;
            decimal precio;
            decimal ivaPct;
            switch (r.Tipo)
            {
                case SaleLineType.Producto:
                {
                    var p = r.ReferenciaId is { } pid && productos.TryGetValue(pid, out var prod) ? prod
                        : throw ApiException.BadRequest("Producto no encontrado.");
                    if (!p.Activo)
                        throw ApiException.BadRequest($"{p.Nombre} está inactivo.");
                    if (p.TipoProducto == ProductType.InsumoCabina)
                        throw ApiException.BadRequest($"{p.Nombre} es un insumo de cabina: no se vende.");
                    descripcion = p.Nombre;
                    precio = r.PrecioUnitario ?? p.PrecioVenta;
                    ivaPct = p.IvaPorcentaje ?? iva.Productos;
                    break;
                }
                case SaleLineType.Procedimiento:
                {
                    var p = r.ReferenciaId is { } pid && procedimientos.TryGetValue(pid, out var proc) ? proc
                        : throw ApiException.BadRequest("Procedimiento no encontrado.");
                    if (!p.Activo)
                        throw ApiException.BadRequest($"{p.Nombre} está inactivo.");
                    if (r.Cantidad != Math.Floor(r.Cantidad))
                        throw ApiException.BadRequest("Un procedimiento se vende por sesiones enteras.");
                    descripcion = p.Nombre;
                    precio = r.PrecioUnitario ?? p.PrecioBase;
                    ivaPct = p.IvaPorcentaje ?? iva.Servicios;
                    break;
                }
                case SaleLineType.Paquete:
                {
                    var p = r.ReferenciaId is { } pid && paquetes.TryGetValue(pid, out var paq) ? paq
                        : throw ApiException.BadRequest("Paquete no encontrado.");
                    if (!p.Activo)
                        throw ApiException.BadRequest($"{p.Nombre} está inactivo.");
                    if (r.Cantidad != 1)
                        throw ApiException.BadRequest("Un paquete se vende de a uno: agrega una línea por cada paquete.");
                    if (validarPaciente && patientId is null)
                        throw ApiException.BadRequest("Para vender un paquete elige el paciente: el paquete queda en su ficha.");
                    descripcion = p.Nombre;
                    precio = r.PrecioUnitario ?? p.PrecioTotal;
                    ivaPct = p.IvaPorcentaje ?? iva.Servicios;
                    break;
                }
                default:
                {
                    if (string.IsNullOrWhiteSpace(r.Descripcion))
                        throw ApiException.BadRequest("Describe el concepto de la línea \"Otro\".");
                    if (r.PrecioUnitario is null)
                        throw ApiException.BadRequest("Indica el precio de la línea \"Otro\".");
                    descripcion = r.Descripcion.Trim();
                    precio = r.PrecioUnitario.Value;
                    ivaPct = r.IvaPorcentaje ?? 0m;
                    break;
                }
            }

            var bruto = SaleMath.Round(r.Cantidad * precio);
            if (r.Descuento > bruto)
                throw ApiException.BadRequest($"El descuento de \"{descripcion}\" supera el valor de la línea.");

            var (subtotal, montoIva, total) = SaleMath.Line(r.Cantidad, precio, r.Descuento, ivaPct);
            lineas.Add(new SaleLine
            {
                Orden = orden++,
                Tipo = r.Tipo,
                ReferenciaId = r.Tipo == SaleLineType.Otro ? null : r.ReferenciaId,
                Descripcion = descripcion.Length > 250 ? descripcion[..250] : descripcion,
                Cantidad = r.Cantidad,
                PrecioUnitario = precio,
                Descuento = SaleMath.Round(r.Descuento),
                IvaPorcentaje = ivaPct,
                Subtotal = subtotal,
                Iva = montoIva,
                Total = total
            });
        }
        return lineas;
    }

    private void AttachLines(Sale sale, List<SaleLine> lineas)
    {
        foreach (var linea in lineas)
        {
            linea.SaleId = sale.Id;
            _db.SaleLines.Add(linea);
        }
        sale.Subtotal = lineas.Sum(l => l.Subtotal);
        sale.Descuento = lineas.Sum(l => l.Descuento);
        sale.Impuestos = lineas.Sum(l => l.Iva);
        sale.Total = lineas.Sum(l => l.Total);
    }

    // ── Internos: confirmar ─────────────────────────────────────────

    private async Task ConfirmInternalAsync(Sale sale, List<SalePaymentRequest> pagos, Guid userId, CancellationToken ct)
    {
        var lineas = LiveLines(sale);
        if (pagos.Any(p => p.Monto <= 0))
            throw ApiException.BadRequest("Cada pago debe ser mayor a cero.");
        var totalPagos = pagos.Sum(p => SaleMath.Round(p.Monto));
        if (totalPagos > sale.Total)
            throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                $"Los pagos ({SaleMath.Pesos(totalPagos)}) superan el total de la venta ({SaleMath.Pesos(sale.Total)}).");
        if (lineas.Any(l => l.Tipo == SaleLineType.Paquete) && sale.PatientId is null)
            throw ApiException.BadRequest("Para vender un paquete elige el paciente: el paquete queda en su ficha.");

        var ahora = _clock.Now;
        var hoy = DateOnly.FromDateTime(ahora);
        var referencia = $"Venta #{sale.Numero}";
        var productosConLotes = new HashSet<Guid>();

        foreach (var linea in lineas)
        {
            switch (linea.Tipo)
            {
                case SaleLineType.Producto:
                {
                    var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == linea.ReferenciaId, ct)
                        ?? throw ApiException.BadRequest($"El producto de \"{linea.Descripcion}\" ya no existe.");
                    var tieneLotes = await _db.ProductLots.AnyAsync(l => l.ProductId == product.Id, ct);
                    if (tieneLotes)
                    {
                        // FEFO: un movimiento por lote, para saber de cuál salió lo que se vendió
                        foreach (var (lote, cantidad) in await _lots.TakeFefoAsync(product, linea.Cantidad, ct))
                            AddMovement(product.Id, lote.Id, cantidad, MovementType.Salida, sale, userId, referencia, ahora);
                        productosConLotes.Add(product.Id);
                    }
                    else
                    {
                        if (product.StockActual < linea.Cantidad)
                            throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                                $"No hay stock suficiente de {product.Nombre}: quedan {product.StockActual:0.##} {product.UnidadMedida}.");
                        product.StockActual -= linea.Cantidad;
                        AddMovement(product.Id, null, linea.Cantidad, MovementType.Salida, sale, userId, referencia, ahora);
                    }
                    break;
                }
                case SaleLineType.Paquete:
                {
                    // El precio acordado del paquete es lo que se cobró por él en la venta (IVA incluido)
                    var asignado = await _packages.AssignAsync(sale.PatientId!.Value, linea.ReferenciaId!.Value, linea.Total, hoy, ct);
                    asignado.SaleId = sale.Id;
                    linea.PatientPackageId = asignado.Id;
                    break;
                }
            }
        }

        foreach (var productId in productosConLotes)
            await _lots.RecalculateStockAsync(productId, ct);

        sale.Estado = SaleStatus.Confirmada;
        sale.Fecha = ahora;

        var cajaId = pagos.Count > 0 ? await _cash.OpenSessionIdAsync(userId, ct) : null;
        foreach (var pago in pagos)
            await AllocatePaymentAsync(sale, pago, userId, cajaId, ct);
    }

    private void AddMovement(Guid productId, Guid? lotId, decimal cantidad, MovementType tipo, Sale sale, Guid userId, string referencia, DateTime fecha) =>
        _db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = productId,
            ProductLotId = lotId,
            Cantidad = cantidad,
            TipoMovimiento = tipo,
            Referencia = referencia,
            PatientId = sale.PatientId,
            SaleId = sale.Id,
            UserId = userId,
            FechaMovimiento = fecha
        });

    /// <summary>
    /// Registra un pago de la venta repartiéndolo entre sus líneas en orden. Lo que cae en un paquete
    /// es un Payment con PatientPackageId (la ficha del paciente lo ve); lo demás, uno sin él.
    /// </summary>
    private async Task AllocatePaymentAsync(Sale sale, SalePaymentRequest pago, Guid userId, Guid? cajaId, CancellationToken ct)
    {
        var existentes = await _db.Payments.AsNoTracking()
            .Where(p => p.SaleId == sale.Id)
            .Select(p => new { p.PatientPackageId, p.Monto })
            .ToListAsync(ct);
        // Los pagos agregados en esta misma unidad de trabajo aún no están en la base
        var nuevos = _db.ChangeTracker.Entries<Payment>()
            .Where(e => e.State == EntityState.Added && e.Entity.SaleId == sale.Id)
            .Select(e => new { e.Entity.PatientPackageId, e.Entity.Monto });
        var aplicados = existentes.Concat(nuevos).ToList();

        // Pendiente por "bolsa": uno por paquete y uno para el resto de la venta, en orden de línea
        var sinPaquetePagado = aplicados.Where(p => p.PatientPackageId is null).Sum(p => p.Monto);
        var bolsas = new List<(int Orden, Guid? PackageId, decimal Pendiente)>();
        decimal restoPendiente = 0;
        int? ordenResto = null;
        foreach (var linea in LiveLines(sale))
        {
            if (linea.Tipo == SaleLineType.Paquete && linea.PatientPackageId is { } ppId)
            {
                var pagadoPaquete = aplicados.Where(p => p.PatientPackageId == ppId).Sum(p => p.Monto);
                bolsas.Add((linea.Orden, ppId, Math.Max(0, linea.Total - pagadoPaquete)));
            }
            else
            {
                var cubierto = Math.Min(linea.Total, sinPaquetePagado);
                sinPaquetePagado -= cubierto;
                var pendiente = linea.Total - cubierto;
                if (pendiente > 0)
                {
                    restoPendiente += pendiente;
                    ordenResto ??= linea.Orden;
                }
            }
        }
        if (restoPendiente > 0)
            bolsas.Add((ordenResto!.Value, null, restoPendiente));

        var restante = SaleMath.Round(pago.Monto);
        var hoy = _clock.Today;
        var tramos = new List<Payment>();
        foreach (var bolsa in bolsas.OrderBy(b => b.Orden))
        {
            if (restante <= 0) break;
            var toma = Math.Min(restante, bolsa.Pendiente);
            if (toma <= 0) continue;
            restante -= toma;

            var payment = new Payment
            {
                SaleId = sale.Id,
                PatientPackageId = bolsa.PackageId,
                PatientId = sale.PatientId,
                CashSessionId = cajaId,
                Monto = toma,
                FechaPago = hoy,
                MetodoPago = pago.MetodoPago,
                Referencia = Limpio(pago.Referencia),
                Observacion = Limpio(pago.Observacion),
                RegistradoPorId = userId == Guid.Empty ? null : userId
            };
            _db.Payments.Add(payment);
            tramos.Add(payment);
        }

        if (restante > 0)
            throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                $"El pago supera el saldo pendiente de la venta por {SaleMath.Pesos(restante)}.");

        if (pago.ComprobanteId.HasValue && tramos.Count > 0)
        {
            // El soporte queda a nombre del primer tramo; los demás tramos del mismo pago lo referencian
            await _attachments.AssignImageAsync(AttachmentEntityType.Payment, tramos[0].Id, pago.ComprobanteId, null, ct);
            foreach (var tramo in tramos)
                tramo.ComprobanteId = pago.ComprobanteId;
        }
    }

    // ── Internos: anular ────────────────────────────────────────────

    private async Task<int> RevertPackagesAsync(Sale sale, CancellationToken ct)
    {
        var ids = LiveLines(sale).Where(l => l.PatientPackageId.HasValue).Select(l => l.PatientPackageId!.Value).ToList();
        if (ids.Count == 0) return 0;

        var paquetes = await _db.PatientPackages.Include(p => p.Sessions).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var usados = paquetes.Where(p => p.SesionesCompletadas > 0).ToList();
        if (usados.Count > 0)
            throw ApiException.Conflict(
                $"No se puede anular: {string.Join(", ", usados.Select(p => $"{p.PackageNombre} ya tiene {p.SesionesCompletadas} sesión(es) realizada(s)"))}.");

        var sessionIds = paquetes.SelectMany(p => p.Sessions).Select(s => s.Id).ToList();
        var citas = await _db.Appointments
            .Where(a => a.PatientPackageSessionId != null && sessionIds.Contains(a.PatientPackageSessionId.Value))
            .ToListAsync(ct);
        foreach (var cita in citas) cita.PatientPackageSessionId = null;

        foreach (var paquete in paquetes)
        {
            foreach (var session in paquete.Sessions) session.IsDeleted = true;
            paquete.IsDeleted = true;
        }
        return paquetes.Count;
    }

    private async Task<int> RevertInventoryAsync(Sale sale, Guid userId, CancellationToken ct)
    {
        var salidas = await _db.InventoryMovements
            .Where(m => m.SaleId == sale.Id && m.TipoMovimiento == MovementType.Salida)
            .ToListAsync(ct);
        if (salidas.Count == 0) return 0;

        var ahora = _clock.Now;
        var referencia = $"Anulación venta #{sale.Numero}";
        var lotIds = salidas.Where(m => m.ProductLotId.HasValue).Select(m => m.ProductLotId!.Value).Distinct().ToList();
        var lotes = await _db.ProductLots.Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var productIds = salidas.Select(m => m.ProductId).Distinct().ToList();
        var productos = await _db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == _db.CurrentTenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var salida in salidas)
        {
            // Vuelve al mismo lote de donde salió: la trazabilidad del lote no se rompe
            if (salida.ProductLotId is { } lotId && lotes.TryGetValue(lotId, out var lote))
                lote.CantidadDisponible += salida.Cantidad;
            else if (productos.TryGetValue(salida.ProductId, out var producto))
                producto.StockActual += salida.Cantidad;

            AddMovement(salida.ProductId, salida.ProductLotId, salida.Cantidad, MovementType.Entrada, sale, userId, referencia, ahora);
        }

        foreach (var productId in salidas.Where(m => m.ProductLotId.HasValue).Select(m => m.ProductId).Distinct())
            await _lots.RecalculateStockAsync(productId, ct);

        return salidas.Count;
    }

    private async Task<(int Anulados, decimal Devuelto, decimal FueraDeCaja)> VoidPaymentsAsync(Sale sale, Guid userId, CancellationToken ct)
    {
        var pagos = await _db.Payments.Where(p => p.SaleId == sale.Id).ToListAsync(ct);
        if (pagos.Count == 0) return (0, 0, 0);

        var sessionIds = pagos.Where(p => p.CashSessionId.HasValue).Select(p => p.CashSessionId!.Value).Distinct().ToList();
        var abiertas = await _db.CashSessions
            .Where(c => sessionIds.Contains(c.Id) && c.Estado == CashSessionStatus.Abierta)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var ahora = _clock.Now;
        decimal aDevolver = 0;
        foreach (var pago in pagos)
        {
            pago.FechaAnulacion = ahora;
            pago.IsDeleted = true;
            // Un pago de una caja todavía abierta simplemente deja de sumar en ella. El efectivo de
            // una caja ya cerrada (o de antes de las cajas) se devuelve desde la caja de quien anula.
            if (pago.MetodoPago == PaymentMethod.Efectivo && !(pago.CashSessionId is { } cid && abiertas.Contains(cid)))
                aDevolver += pago.Monto;
        }

        decimal devuelto = 0, fuera = 0;
        if (aDevolver > 0)
        {
            if (await _cash.AddRefundAsync(userId, aDevolver, $"Devolución venta #{sale.Numero}", sale.Id, ct))
                devuelto = aDevolver;
            else
                fuera = aDevolver;
        }
        return (pagos.Count, devuelto, fuera);
    }

    // ── Internos: varios ────────────────────────────────────────────

    private static IEnumerable<SaleLine> LiveLines(Sale sale) =>
        sale.Lineas.Where(l => !l.IsDeleted).OrderBy(l => l.Orden);

    private async Task ValidatePatientAsync(Guid? patientId, CancellationToken ct)
    {
        if (patientId.HasValue && !await _db.Patients.AnyAsync(p => p.Id == patientId.Value, ct))
            throw ApiException.BadRequest("Paciente no encontrado.");
    }

    /// <summary>
    /// Siguiente consecutivo de la clínica. UPDLOCK + HOLDLOCK dentro de la transacción serializan
    /// dos ventas simultáneas: la segunda espera a que la primera confirme y toma el número siguiente.
    /// </summary>
    private async Task<int> NextNumberAsync(CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        var ultimo = await _db.Database
            .SqlQuery<int>($"SELECT ISNULL(MAX(Numero), 0) AS Value FROM Sales WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId}")
            .SingleAsync(ct);
        return ultimo + 1;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("Otra persona acaba de modificar esta venta (o el stock de sus productos). Revísala e inténtalo de nuevo.");
        }
    }

    private static SaleDto ToDto(Sale s, decimal pagado, string? pacienteNombre = null, string? pacienteCedula = null)
    {
        var paciente = pacienteNombre ?? (s.Patient is { } p ? $"{p.Nombre} {p.Apellido}" : null);
        var saldo = s.Estado == SaleStatus.Confirmada ? Math.Max(0, s.Total - pagado) : 0;
        var (_, estadoPago) = PatientPackageService.PaymentSummary(s.Total, pagado);
        return new SaleDto
        {
            Id = s.Id,
            Numero = s.Numero,
            Fecha = s.Fecha,
            Estado = s.Estado.ToString(),
            BranchId = s.BranchId,
            BranchNombre = s.Branch?.Nombre ?? string.Empty,
            PatientId = s.PatientId,
            Cliente = paciente ?? s.ClienteNombre ?? "Cliente de mostrador",
            ClienteDocumento = s.PatientId.HasValue ? pacienteCedula ?? s.Patient?.Cedula : s.ClienteDocumento,
            EsMostrador = s.PatientId is null,
            Subtotal = s.Subtotal,
            Descuento = s.Descuento,
            Impuestos = s.Impuestos,
            Total = s.Total,
            TotalPagado = s.Estado == SaleStatus.Anulada ? 0 : pagado,
            Saldo = saldo,
            EstadoPago = s.Estado == SaleStatus.Confirmada ? estadoPago.ToString() : string.Empty,
            Vendedor = s.Usuario is null ? string.Empty : $"{s.Usuario.Nombre} {s.Usuario.Apellido}".Trim(),
            Observacion = s.Observacion,
            FechaAnulacion = s.FechaAnulacion,
            MotivoAnulacion = s.MotivoAnulacion,
            FacturaEstado = s.FacturaEstado.ToString(),
            FacturaNumero = s.FacturaNumero,
            FacturaCufe = s.FacturaCufe,
            FacturaFecha = s.FacturaFecha,
            FacturaMensaje = s.FacturaMensaje,
            FacturaPdfId = s.FacturaPdfId,
            FacturaXmlId = s.FacturaXmlId,
            Resumen = Resumen(s.Lineas.Where(l => !l.IsDeleted).Select(l => l.Tipo))
        };
    }

    private static SaleLineDto ToLineDto(SaleLine l) => new()
    {
        Id = l.Id,
        Orden = l.Orden,
        Tipo = l.Tipo.ToString(),
        ReferenciaId = l.ReferenciaId,
        Descripcion = l.Descripcion,
        Cantidad = l.Cantidad,
        PrecioUnitario = l.PrecioUnitario,
        Descuento = l.Descuento,
        IvaPorcentaje = l.IvaPorcentaje,
        Subtotal = l.Subtotal,
        Iva = l.Iva,
        Total = l.Total,
        PatientPackageId = l.PatientPackageId
    };

    /// <summary>"2 productos · 1 procedimiento".</summary>
    private static string Resumen(IEnumerable<SaleLineType> tipos)
    {
        static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";
        return string.Join(" · ", tipos.GroupBy(t => t).OrderBy(g => g.Key).Select(g => g.Key switch
        {
            SaleLineType.Producto => Plural(g.Count(), "producto", "productos"),
            SaleLineType.Procedimiento => Plural(g.Count(), "procedimiento", "procedimientos"),
            SaleLineType.Paquete => Plural(g.Count(), "paquete", "paquetes"),
            _ => Plural(g.Count(), "otro", "otros")
        }));
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

public class SaleListFilter
{
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
    public SaleStatus? Estado { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? PatientId { get; set; }
    public bool? ConSaldo { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

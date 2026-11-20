using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Invoicing;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Invoicing;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Facturación electrónica ante la DIAN a través del proveedor (IElectronicInvoiceProvider: Factus).
///
///   · Se factura una venta confirmada o un pago de paquete registrado desde la ficha (sin venta).
///     El pago de una venta no se factura suelto: va en la factura de la venta (con el saldo a crédito).
///   · El documento se arma, se guarda como ElectronicInvoice Pendiente (con su referencia y el JSON
///     del documento) y SOLO entonces se envía: si el proceso muere a mitad, el job lo reenvía con
///     la misma referencia y el proveedor devuelve el documento ya validado, sin duplicar.
///   · Emitida → número, CUFE, fecha y el PDF y XML como adjuntos (Kind Factura) de la venta o del
///     pago; ElectronicInvoice.PatientId la liga al paciente. La venta copia estado, número y CUFE.
///   · Rechazada → el motivo del proveedor. Emitir de nuevo usa referencia nueva (y descarta la
///     anterior en el proveedor, que si no bloquea los siguientes documentos).
///   · Nota crédito: anulación (toda la factura) o devolución (líneas y cantidades). Anular una venta
///     facturada, o borrar un pago facturado, la emite sola. Una factura queda Anulada cuando una
///     nota de anulación se emite o cuando sus notas ya suman su total.
/// </summary>
public class InvoiceService
{
    public const string Anulacion = "Anulacion";
    public const string Devolucion = "Devolucion";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly InvoicingSettingsService _settings;
    private readonly IElectronicInvoiceProvider _provider;
    private readonly AttachmentService _attachments;
    private readonly FactusCatalogService _catalogs;
    private readonly IClock _clock;
    private readonly ILogger<InvoiceService> _log;

    public InvoiceService(AppDbContext db, InvoicingSettingsService settings, IElectronicInvoiceProvider provider,
        AttachmentService attachments, FactusCatalogService catalogs, IClock clock, ILogger<InvoiceService> log)
    {
        _db = db;
        _settings = settings;
        _provider = provider;
        _attachments = attachments;
        _catalogs = catalogs;
        _clock = clock;
        _log = log;
    }

    public static string XlsxContentType => Xlsx;

    // ── Emitir ──────────────────────────────────────────────────────

    public Task<ElectronicInvoiceDto> IssueAsync(IssueInvoiceRequest request, Guid userId, CancellationToken ct = default)
    {
        if (request.SaleId.HasValue == request.PaymentId.HasValue)
            throw ApiException.BadRequest("Indica la venta o el pago a facturar (uno de los dos).");
        return request.SaleId is Guid saleId ? IssueForSaleAsync(saleId, userId, ct) : IssueForPaymentAsync(request.PaymentId!.Value, userId, ct);
    }

    public async Task<ElectronicInvoiceDto> IssueForSaleAsync(Guid saleId, Guid userId, CancellationToken ct = default)
    {
        var (settings, conexion) = await _settings.RequireActiveAsync(ct);
        var sale = await _db.Sales.Include(s => s.Lineas).Include(s => s.Patient)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw ApiException.NotFound("Venta no encontrada.");
        if (sale.Estado != SaleStatus.Confirmada)
            throw ApiException.Conflict("Solo se factura una venta confirmada.");
        var anterior = await BlockIfInvoicedAsync(_db.ElectronicInvoices.Where(f => f.SaleId == saleId), ct);

        var pagos = await _db.Payments.AsNoTracking().Where(p => p.SaleId == saleId).ToListAsync(ct);
        var pagado = pagos.Sum(p => p.Monto);
        var lineas = sale.Lineas.Where(l => !l.IsDeleted).OrderBy(l => l.Orden).ToList();
        var codigos = await LineCodesAsync(lineas, ct);
        var doc = new ElectronicInvoiceDocument
        {
            Referencia = NewReference($"V{sale.Numero}"),
            RangoNumeracionId = Range(settings, sale.BranchId, ElectronicDocumentType.Factura),
            Fecha = _clock.Now,
            Observacion = string.IsNullOrWhiteSpace(sale.Observacion) ? $"Venta #{sale.Numero}" : $"Venta #{sale.Numero}. {sale.Observacion}",
            Cliente = sale.Patient is { } p ? PatientCustomer(p) : WalkInCustomer(sale.ClienteNombre, sale.ClienteDocumento),
            Lineas = lineas.Select(l => new ElectronicInvoiceLine
            {
                Codigo = codigos.GetValueOrDefault(l.Id, "OTRO"),
                Descripcion = l.Descripcion,
                Cantidad = l.Cantidad,
                PrecioUnitario = l.PrecioUnitario,
                Descuento = l.Descuento,
                Base = l.Subtotal,
                IvaPorcentaje = l.IvaPorcentaje,
                Iva = l.Iva,
                Total = l.Total,
                Tipo = l.Tipo.ToString(),
            }).ToList(),
            Pagos = pagos.GroupBy(x => x.MetodoPago)
                .Select(g => new ElectronicInvoicePayment { Metodo = g.Key.ToString(), Monto = g.Sum(x => x.Monto) }).ToList(),
            Subtotal = sale.Subtotal,
            Impuestos = sale.Impuestos,
            Total = sale.Total,
            Saldo = Math.Max(sale.Total - pagado, 0),
            Vencimiento = _clock.Today.AddDays(30),
        };
        doc.EnviarCorreo = settings.EnviarCorreoAlEmitir && !string.IsNullOrWhiteSpace(doc.Cliente.Email);

        var factura = NewRecord(ElectronicDocumentType.Factura, doc, sale.BranchId, userId);
        factura.SaleId = sale.Id;
        factura.PatientId = sale.PatientId;
        return await CreateAndSendAsync(factura, doc, conexion, anterior, userId, ct);
    }

    public async Task<ElectronicInvoiceDto> IssueForPaymentAsync(Guid paymentId, Guid userId, CancellationToken ct = default)
    {
        var (settings, conexion) = await _settings.RequireActiveAsync(ct);
        var pago = await _db.Payments.Include(p => p.PatientPackage).ThenInclude(pp => pp!.Patient)
            .Include(p => p.CashSession)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw ApiException.NotFound("Pago no encontrado.");
        if (pago.SaleId is Guid saleId)
        {
            var numero = await _db.Sales.Where(s => s.Id == saleId).Select(s => s.Numero).FirstOrDefaultAsync(ct);
            throw ApiException.Conflict($"Este pago es de la venta #{numero}: se factura desde la venta.");
        }
        if (pago.PatientPackage is not { } paquete)
            throw ApiException.Conflict("Solo se facturan pagos de paquetes o ventas.");
        var anterior = await BlockIfInvoicedAsync(_db.ElectronicInvoices.Where(f => f.PaymentId == paymentId), ct);

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _db.CurrentTenantId, ct);
        var branchId = pago.CashSession?.BranchId
            ?? await _db.Branches.OrderBy(b => b.CreatedAt).Select(b => b.Id).FirstAsync(ct);
        // El abono ya trae el IVA de servicios de la clínica: se separa la base.
        var iva = tenant.IvaServicios;
        var baseGravable = SaleMath.Round(pago.Monto / (1 + iva / 100m));
        var valorIva = pago.Monto - baseGravable;
        var doc = new ElectronicInvoiceDocument
        {
            Referencia = NewReference("P"),
            RangoNumeracionId = Range(settings, branchId, ElectronicDocumentType.Factura),
            Fecha = _clock.Now,
            Observacion = $"Abono al paquete {paquete.PackageNombre}" + (string.IsNullOrWhiteSpace(pago.Referencia) ? "" : $" · Ref. {pago.Referencia}"),
            Cliente = PatientCustomer(paquete.Patient),
            Lineas =
            [
                new ElectronicInvoiceLine
                {
                    Codigo = "PAQ-" + paquete.PackageId.ToString("N")[..8].ToUpperInvariant(),
                    Descripcion = $"Abono paquete {paquete.PackageNombre}",
                    Cantidad = 1,
                    PrecioUnitario = baseGravable,
                    Base = baseGravable,
                    IvaPorcentaje = iva,
                    Iva = valorIva,
                    Total = pago.Monto,
                    Tipo = "Abono",
                },
            ],
            Pagos = [new ElectronicInvoicePayment { Metodo = pago.MetodoPago.ToString(), Monto = pago.Monto }],
            Subtotal = baseGravable,
            Impuestos = valorIva,
            Total = pago.Monto,
        };
        doc.EnviarCorreo = settings.EnviarCorreoAlEmitir && !string.IsNullOrWhiteSpace(doc.Cliente.Email);

        var factura = NewRecord(ElectronicDocumentType.Factura, doc, branchId, userId);
        factura.PaymentId = pago.Id;
        factura.PatientId = paquete.PatientId;
        return await CreateAndSendAsync(factura, doc, conexion, anterior, userId, ct);
    }

    /// <summary>
    /// 409 si ya hay una factura viva (emitida o en proceso). Devuelve la última rechazada, cuya
    /// referencia hay que descartar en el proveedor antes de emitir otra.
    /// </summary>
    private static async Task<ElectronicInvoice?> BlockIfInvoicedAsync(IQueryable<ElectronicInvoice> facturas, CancellationToken ct)
    {
        var lista = await facturas.Where(f => f.TipoDocumento == ElectronicDocumentType.Factura)
            .OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
        if (lista.Any(f => f.Estado == ElectronicInvoiceStatus.Emitida))
            throw ApiException.Conflict("Ya tiene factura electrónica emitida.");
        if (lista.Any(f => f.Estado == ElectronicInvoiceStatus.Pendiente))
            throw ApiException.Conflict("Ya hay una factura en proceso con el proveedor: usa Reintentar.");
        return lista.FirstOrDefault(f => f.Estado == ElectronicInvoiceStatus.Rechazada);
    }

    // ── Reintentar ──────────────────────────────────────────────────

    /// <summary>
    /// Pendiente → reenvía el mismo documento con la misma referencia. Emitida sin PDF/XML → los
    /// descarga. Rechazada → emite de nuevo con los datos actuales y referencia nueva.
    /// </summary>
    public async Task<ElectronicInvoiceDto> RetryAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var (_, conexion) = await _settings.RequireActiveAsync(ct);
        var factura = await _db.ElectronicInvoices.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw ApiException.NotFound("Factura no encontrada.");
        switch (factura.Estado)
        {
            case ElectronicInvoiceStatus.Pendiente:
                await SendAsync(factura, Document(factura), conexion, userId, ct);
                return await GetAsync(id, ct);
            case ElectronicInvoiceStatus.Emitida or ElectronicInvoiceStatus.Anulada when factura.PdfId is null || factura.XmlId is null:
                await EnsureFilesAsync(factura, conexion, userId, ct);
                await _db.SaveChangesAsync(ct);
                return await GetAsync(id, ct);
            case ElectronicInvoiceStatus.Rechazada when factura.TipoDocumento == ElectronicDocumentType.Factura:
                if (factura.SaleId is Guid saleId) return await IssueForSaleAsync(saleId, userId, ct);
                if (factura.PaymentId is Guid paymentId) return await IssueForPaymentAsync(paymentId, userId, ct);
                throw ApiException.Conflict("La factura no tiene venta ni pago de origen.");
            case ElectronicInvoiceStatus.Rechazada:
                return await RetryRejectedCreditNoteAsync(factura, conexion, userId, ct);
            default:
                throw ApiException.Conflict("No hay nada que reintentar: el documento ya está emitido.");
        }
    }

    private async Task<ElectronicInvoiceDto> RetryRejectedCreditNoteAsync(ElectronicInvoice nota, InvoicingConnection conexion, Guid userId, CancellationToken ct)
    {
        var origen = await _db.ElectronicInvoices.FirstAsync(f => f.Id == nota.FacturaOrigenId, ct);
        if (origen.Estado != ElectronicInvoiceStatus.Emitida)
            throw ApiException.Conflict("La factura que corrige ya no está emitida.");
        var settings = await _settings.LoadAsync(ct);
        var doc = Document(nota);
        doc.Referencia = NewReference("NC");
        doc.RangoNumeracionId = Range(settings!, origen.BranchId, ElectronicDocumentType.NotaCredito);
        doc.Fecha = _clock.Now;
        doc.Cliente = Document(origen).Cliente;
        var nueva = NewRecord(ElectronicDocumentType.NotaCredito, doc, origen.BranchId, userId);
        CopyOrigin(nueva, origen, nota.ConceptoCorreccion, nota.Motivo);
        return await CreateAndSendAsync(nueva, doc, conexion, nota, userId, ct);
    }

    /// <summary>Job facturas-reintento: las pendientes y las emitidas sin PDF/XML de la clínica actual.</summary>
    public async Task<(int Revisadas, int Emitidas, int Rechazadas)> RetryPendingAsync(CancellationToken ct)
    {
        InvoicingConnection conexion;
        try
        {
            (_, conexion) = await _settings.RequireActiveAsync(ct);
        }
        catch (ApiException)
        {
            return (0, 0, 0); // la clínica deshabilitó la facturación: sus pendientes esperan
        }
        var sistema = await _db.Users.Where(u => u.Rol == UserRole.SuperAdmin).OrderBy(u => u.CreatedAt).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var pendientes = await _db.ElectronicInvoices
            .Where(f => f.Estado == ElectronicInvoiceStatus.Pendiente
                || (f.Estado == ElectronicInvoiceStatus.Emitida && (f.PdfId == null || f.XmlId == null)))
            .OrderBy(f => f.CreatedAt).Take(50).ToListAsync(ct);
        int emitidas = 0, rechazadas = 0;
        foreach (var f in pendientes)
        {
            try
            {
                if (f.Estado == ElectronicInvoiceStatus.Pendiente)
                {
                    await SendAsync(f, Document(f), conexion, f.EmitidaPorId == Guid.Empty ? sistema : f.EmitidaPorId, ct);
                    if (f.Estado == ElectronicInvoiceStatus.Emitida) emitidas++;
                    if (f.Estado == ElectronicInvoiceStatus.Rechazada) rechazadas++;
                }
                else
                {
                    await EnsureFilesAsync(f, conexion, f.EmitidaPorId, ct);
                    await _db.SaveChangesAsync(ct);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Reintento de la factura {Id} falló", f.Id);
            }
        }
        return (pendientes.Count, emitidas, rechazadas);
    }

    // ── Notas crédito ───────────────────────────────────────────────

    public async Task<ElectronicInvoiceDto> CreditNoteAsync(Guid invoiceId, CreditNoteRequest request, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Motivo))
            throw ApiException.BadRequest("Indica el motivo de la nota crédito.");
        var (settings, conexion) = await _settings.RequireActiveAsync(ct);
        var origen = await _db.ElectronicInvoices.FirstOrDefaultAsync(f => f.Id == invoiceId, ct)
            ?? throw ApiException.NotFound("Factura no encontrada.");
        var nota = await BuildCreditNoteAsync(origen, settings, request.Tipo, request.Lineas, request.Motivo.Trim(), userId, ct);
        return await CreateAndSendAsync(nota.Registro, nota.Documento, conexion, null, userId, ct);
    }

    /// <summary>
    /// Anular una venta: si tiene factura emitida, nota crédito por lo que falte acreditar. Se llama
    /// ANTES de revertir la venta. 409 si la nota se rechaza (la venta no se anula), y si hay una
    /// factura aún en proceso que no se pudo resolver.
    /// </summary>
    public async Task AnnulForSaleAsync(Guid saleId, string motivo, Guid userId, CancellationToken ct = default)
    {
        await AnnulAsync(_db.ElectronicInvoices.Where(f => f.SaleId == saleId), $"Anulación de la venta: {motivo}", userId, ct);
    }

    /// <summary>Borrar pagos de paquete que se facturaron: nota crédito de cada factura.</summary>
    public async Task AnnulForPaymentsAsync(IReadOnlyCollection<Guid> paymentIds, string motivo, Guid userId, CancellationToken ct = default)
    {
        if (paymentIds.Count == 0) return;
        await AnnulAsync(_db.ElectronicInvoices.Where(f => f.PaymentId != null && paymentIds.Contains(f.PaymentId.Value)), motivo, userId, ct);
    }

    private async Task AnnulAsync(IQueryable<ElectronicInvoice> query, string motivo, Guid userId, CancellationToken ct)
    {
        var facturas = await query.Where(f => f.TipoDocumento == ElectronicDocumentType.Factura
                && (f.Estado == ElectronicInvoiceStatus.Emitida || f.Estado == ElectronicInvoiceStatus.Pendiente))
            .ToListAsync(ct);
        if (facturas.Count == 0) return;

        var (settings, conexion) = await _settings.RequireActiveAsync(ct);
        foreach (var factura in facturas)
        {
            if (factura.Estado == ElectronicInvoiceStatus.Pendiente)
            {
                // Resolverla primero: si el proveedor la había emitido, hay que acreditarla.
                await SendAsync(factura, Document(factura), conexion, userId, ct);
                if (factura.Estado == ElectronicInvoiceStatus.Pendiente)
                    throw ApiException.Conflict($"La factura electrónica sigue en proceso con {_provider.Nombre} ({factura.Mensaje}). Inténtalo en unos minutos.");
                if (factura.Estado == ElectronicInvoiceStatus.Rechazada) continue;
            }
            var restante = await RemainingAsync(factura, ct);
            if (restante.All(l => l.Cantidad <= 0)) continue;

            var notas = await _db.ElectronicInvoices.AnyAsync(f => f.FacturaOrigenId == factura.Id
                && f.Estado != ElectronicInvoiceStatus.Rechazada, ct);
            var tipo = notas ? Devolucion : Anulacion;
            var lineas = restante.Where(l => l.Cantidad > 0).Select(l => new CreditNoteLineRequest { Indice = l.Indice, Cantidad = l.Cantidad }).ToList();
            var nota = await BuildCreditNoteAsync(factura, settings, tipo, lineas, motivo, userId, ct);
            await CreateAndSendAsync(nota.Registro, nota.Documento, conexion, null, userId, ct);
            if (nota.Registro.Estado == ElectronicInvoiceStatus.Rechazada)
                throw ApiException.Conflict($"{_provider.Nombre} rechazó la nota crédito de la factura {factura.Numero}: {nota.Registro.Mensaje}");
        }
    }

    private sealed record CreditNoteDraft(ElectronicInvoice Registro, ElectronicInvoiceDocument Documento);

    private async Task<CreditNoteDraft> BuildCreditNoteAsync(ElectronicInvoice origen, InvoicingSettings settings, string tipo,
        List<CreditNoteLineRequest> pedidas, string motivo, Guid userId, CancellationToken ct)
    {
        if (origen.TipoDocumento != ElectronicDocumentType.Factura)
            throw ApiException.Conflict("La nota crédito se hace sobre una factura.");
        if (origen.Estado != ElectronicInvoiceStatus.Emitida)
            throw ApiException.Conflict("Solo una factura emitida admite nota crédito.");
        if (await _db.ElectronicInvoices.AnyAsync(f => f.FacturaOrigenId == origen.Id && f.Estado == ElectronicInvoiceStatus.Pendiente, ct))
            throw ApiException.Conflict("Esta factura ya tiene una nota crédito en proceso: reinténtala primero.");

        var docOrigen = Document(origen);
        var restante = await RemainingAsync(origen, ct);
        var eq = _catalogs.Equivalences.PorDefecto;
        List<(ElectronicInvoiceLine Linea, int Indice, decimal Cantidad)> seleccion;
        string concepto;
        if (string.Equals(tipo, Anulacion, StringComparison.OrdinalIgnoreCase))
        {
            if (restante.Any(r => r.Cantidad != docOrigen.Lineas[r.Indice].Cantidad))
                throw ApiException.Conflict("La factura ya tiene devoluciones: acredita lo que queda con una devolución.");
            concepto = eq.CorreccionAnulacion;
            seleccion = docOrigen.Lineas.Select((l, i) => (l, i, l.Cantidad)).ToList();
        }
        else if (string.Equals(tipo, Devolucion, StringComparison.OrdinalIgnoreCase))
        {
            concepto = eq.CorreccionDevolucion;
            seleccion = [];
            foreach (var p in pedidas.Where(p => p.Cantidad > 0).GroupBy(p => p.Indice))
            {
                if (p.Key < 0 || p.Key >= docOrigen.Lineas.Count) throw ApiException.BadRequest("Línea de la factura inválida.");
                var cantidad = p.Sum(x => x.Cantidad);
                var disponible = restante.First(r => r.Indice == p.Key).Cantidad;
                if (cantidad > disponible)
                    throw ApiException.BadRequest($"«{docOrigen.Lineas[p.Key].Descripcion}»: se pueden devolver {disponible:0.##} como máximo.");
                seleccion.Add((docOrigen.Lineas[p.Key], p.Key, cantidad));
            }
            if (seleccion.Count == 0) throw ApiException.BadRequest("Elige al menos una línea y la cantidad a devolver.");
        }
        else throw ApiException.BadRequest("Tipo de nota crédito inválido: Anulacion o Devolucion.");

        var lineas = seleccion.Select(s => Prorate(s.Linea, s.Cantidad, s.Indice)).ToList();
        var total = lineas.Sum(l => l.Total);
        var metodo = docOrigen.Pagos.OrderByDescending(p => p.Monto).Select(p => p.Metodo).FirstOrDefault() ?? nameof(PaymentMethod.Efectivo);
        var doc = new ElectronicInvoiceDocument
        {
            Referencia = NewReference("NC"),
            RangoNumeracionId = Range(settings, origen.BranchId, ElectronicDocumentType.NotaCredito),
            Fecha = _clock.Now,
            Observacion = motivo,
            Cliente = docOrigen.Cliente,
            Lineas = lineas,
            Pagos = [new ElectronicInvoicePayment { Metodo = metodo, Monto = total }],
            Subtotal = lineas.Sum(l => l.Base),
            Impuestos = lineas.Sum(l => l.Iva),
            Total = total,
            FacturaNumero = origen.Numero,
            ConceptoCorreccion = concepto,
        };
        var registro = NewRecord(ElectronicDocumentType.NotaCredito, doc, origen.BranchId, userId);
        CopyOrigin(registro, origen, concepto, motivo);
        return new CreditNoteDraft(registro, doc);
    }

    private static void CopyOrigin(ElectronicInvoice nota, ElectronicInvoice origen, string? concepto, string? motivo)
    {
        nota.FacturaOrigenId = origen.Id;
        nota.SaleId = origen.SaleId;
        nota.PaymentId = origen.PaymentId;
        nota.PatientId = origen.PatientId;
        nota.ConceptoCorreccion = concepto;
        nota.Motivo = motivo is { Length: > 500 } ? motivo[..500] : motivo;
    }

    /// <summary>Una línea de la factura por una cantidad menor: descuento proporcional, base e IVA recalculados.</summary>
    private static ElectronicInvoiceLine Prorate(ElectronicInvoiceLine l, decimal cantidad, int indice)
    {
        var descuento = cantidad == l.Cantidad ? l.Descuento : SaleMath.Round(l.Descuento * cantidad / l.Cantidad);
        var (subtotal, iva, total) = cantidad == l.Cantidad ? (l.Base, l.Iva, l.Total) : SaleMath.Line(cantidad, l.PrecioUnitario, descuento, l.IvaPorcentaje);
        return new ElectronicInvoiceLine
        {
            Codigo = l.Codigo, Descripcion = l.Descripcion, Cantidad = cantidad, PrecioUnitario = l.PrecioUnitario,
            Descuento = descuento, Base = subtotal, IvaPorcentaje = l.IvaPorcentaje, Iva = iva, Total = total,
            Tipo = l.Tipo, IndiceOrigen = indice,
        };
    }

    private sealed record Remaining(int Indice, decimal Cantidad);

    /// <summary>Por línea de la factura, lo que aún no salió en notas crédito vivas (emitidas o en proceso).</summary>
    private async Task<List<Remaining>> RemainingAsync(ElectronicInvoice factura, CancellationToken ct)
    {
        var doc = Document(factura);
        var devuelto = await CreditedAsync(factura.Id, ct);
        return doc.Lineas.Select((l, i) => new Remaining(i, l.Cantidad - devuelto.GetValueOrDefault(i))).ToList();
    }

    private async Task<Dictionary<int, decimal>> CreditedAsync(Guid facturaId, CancellationToken ct)
    {
        var notas = await _db.ElectronicInvoices.AsNoTracking()
            .Where(f => f.FacturaOrigenId == facturaId && f.Estado != ElectronicInvoiceStatus.Rechazada)
            .Select(f => f.Documento).ToListAsync(ct);
        return notas.Select(Deserialize).SelectMany(d => d.Lineas)
            .Where(l => l.IndiceOrigen.HasValue)
            .GroupBy(l => l.IndiceOrigen!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad));
    }

    // ── Envío y resultado ───────────────────────────────────────────

    private ElectronicInvoice NewRecord(ElectronicDocumentType tipo, ElectronicInvoiceDocument doc, Guid branchId, Guid userId) => new()
    {
        TipoDocumento = tipo,
        Estado = ElectronicInvoiceStatus.Pendiente,
        BranchId = branchId,
        Referencia = doc.Referencia,
        RangoNumeracionId = doc.RangoNumeracionId,
        ClienteNombre = Cut(doc.Cliente.ConsumidorFinal ? _catalogs.Equivalences.ConsumidorFinal.Nombre : doc.Cliente.Nombre, 200),
        ClienteDocumento = Cut(doc.Cliente.ConsumidorFinal ? _catalogs.Equivalences.ConsumidorFinal.Documento : doc.Cliente.Documento, 30),
        ClienteEmail = doc.Cliente.Email,
        Subtotal = doc.Subtotal,
        Impuestos = doc.Impuestos,
        Total = doc.Total,
        Documento = JsonSerializer.Serialize(doc, Json),
        EmitidaPorId = userId,
    };

    private async Task<ElectronicInvoiceDto> CreateAndSendAsync(ElectronicInvoice registro, ElectronicInvoiceDocument doc,
        InvoicingConnection conexion, ElectronicInvoice? rechazadaAnterior, Guid userId, CancellationToken ct)
    {
        if (rechazadaAnterior is not null)
        {
            // La que el proveedor registró sin validar bloquea los siguientes documentos: se descarta.
            try
            {
                await _provider.DescartarAsync(conexion, Kind(rechazadaAnterior.TipoDocumento), rechazadaAnterior.Referencia, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "No se pudo descartar la referencia {Referencia} en el proveedor", rechazadaAnterior.Referencia);
            }
        }
        _db.ElectronicInvoices.Add(registro);
        if (registro.SaleId is Guid saleId && registro.TipoDocumento == ElectronicDocumentType.Factura)
            await SyncSaleAsync(saleId, registro, ct);
        // Guardada antes de enviar: si el proceso muere, el job la reenvía con la misma referencia.
        await _db.SaveChangesAsync(ct);
        await SendAsync(registro, doc, conexion, userId, ct);
        return await GetAsync(registro.Id, ct);
    }

    private async Task SendAsync(ElectronicInvoice registro, ElectronicInvoiceDocument doc, InvoicingConnection conexion, Guid userId, CancellationToken ct)
    {
        registro.Intentos++;
        registro.UltimoIntento = _clock.Now;
        ElectronicInvoiceResult resultado;
        try
        {
            resultado = registro.TipoDocumento == ElectronicDocumentType.Factura
                ? await _provider.EmitirAsync(conexion, doc, ct)
                : await _provider.NotaCreditoAsync(conexion, doc, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.LogError(e, "El proveedor falló con el documento {Referencia}", registro.Referencia);
            resultado = new ElectronicInvoiceResult { Estado = ElectronicResultStatus.Pendiente, Mensaje = e.Message };
        }

        switch (resultado.Estado)
        {
            case ElectronicResultStatus.Emitida:
                registro.Estado = ElectronicInvoiceStatus.Emitida;
                registro.Numero = resultado.Numero;
                registro.Cufe = resultado.Cufe;
                registro.FechaEmision = resultado.Fecha ?? _clock.Now;
                registro.Advertencias = Cut(resultado.Advertencias, 4000);
                registro.Mensaje = null;
                if (doc.EnviarCorreo && !string.IsNullOrWhiteSpace(doc.Cliente.Email))
                {
                    registro.CorreoEnviadoA = doc.Cliente.Email;
                    registro.FechaCorreo = _clock.Now;
                }
                await EnsureFilesAsync(registro, conexion, userId, ct);
                if (registro.TipoDocumento == ElectronicDocumentType.NotaCredito)
                    await ApplyCreditNoteAsync(registro, ct);
                break;
            case ElectronicResultStatus.Rechazada:
                registro.Estado = ElectronicInvoiceStatus.Rechazada;
                registro.Mensaje = Cut(resultado.Mensaje ?? "Rechazada por el proveedor.", 4000);
                break;
            default:
                registro.Mensaje = Cut(resultado.Mensaje ?? "Sin respuesta del proveedor.", 4000);
                break;
        }
        if (registro.SaleId is Guid saleId && registro.TipoDocumento == ElectronicDocumentType.Factura)
            await SyncSaleAsync(saleId, registro, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>La factura queda Anulada con una nota de anulación, o cuando sus notas ya suman su total.</summary>
    private async Task ApplyCreditNoteAsync(ElectronicInvoice nota, CancellationToken ct)
    {
        if (nota.FacturaOrigenId is not Guid origenId) return;
        var origen = await _db.ElectronicInvoices.FirstAsync(f => f.Id == origenId, ct);
        var acreditado = await _db.ElectronicInvoices
            .Where(f => f.FacturaOrigenId == origenId && f.Id != nota.Id && f.Estado == ElectronicInvoiceStatus.Emitida)
            .SumAsync(f => f.Total, ct) + nota.Total;
        if (nota.ConceptoCorreccion == _catalogs.Equivalences.PorDefecto.CorreccionAnulacion || acreditado >= origen.Total)
        {
            origen.Estado = ElectronicInvoiceStatus.Anulada;
            if (origen.SaleId is Guid saleId) await SyncSaleAsync(saleId, origen, ct);
        }
    }

    /// <summary>PDF y XML como adjuntos de la venta (o del pago). Si falla, queda emitida y el job los baja luego.</summary>
    private async Task EnsureFilesAsync(ElectronicInvoice f, InvoicingConnection conexion, Guid userId, CancellationToken ct)
    {
        if ((f.PdfId is not null && f.XmlId is not null) || f.Numero is null) return;
        try
        {
            var archivos = await _provider.DescargarAsync(conexion, Kind(f.TipoDocumento), f.Numero, ct);
            var (tipo, entityId) = f.SaleId is Guid s ? (AttachmentEntityType.Sale, s) : (AttachmentEntityType.Payment, f.PaymentId ?? f.Id);
            var nombre = (f.TipoDocumento == ElectronicDocumentType.Factura ? "factura-" : "nota-credito-") + f.Numero;
            if (f.PdfId is null && archivos.Pdf is { Length: > 0 } pdf)
                f.PdfId = (await _attachments.SaveGeneratedFileAsync(pdf, nombre + ".pdf", "application/pdf", ".pdf",
                    tipo, entityId, AttachmentKind.Factura, userId, ct)).Id;
            if (f.XmlId is null && archivos.Xml is { Length: > 0 } xml)
                f.XmlId = (await _attachments.SaveGeneratedFileAsync(xml, nombre + ".xml", "application/xml", ".xml",
                    tipo, entityId, AttachmentKind.Factura, userId, ct)).Id;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.LogWarning(e, "No se pudieron descargar el PDF/XML de {Numero}", f.Numero);
            f.Mensaje = Cut($"Emitida. El PDF y el XML se descargarán en el próximo reintento ({e.Message}).", 4000);
        }
    }

    /// <summary>La venta refleja su factura vigente (la última creada).</summary>
    private async Task SyncSaleAsync(Guid saleId, ElectronicInvoice factura, CancellationToken ct)
    {
        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == saleId, ct);
        if (sale is null) return;
        sale.FacturaEstado = factura.Estado;
        sale.FacturaNumero = factura.Numero;
        sale.FacturaCufe = factura.Cufe;
        sale.FacturaFecha = factura.FechaEmision;
        sale.FacturaMensaje = Cut(factura.Mensaje, 1000);
        sale.FacturaPdfId = factura.PdfId;
        sale.FacturaXmlId = factura.XmlId;
    }

    // ── Correo ──────────────────────────────────────────────────────

    public async Task<ElectronicInvoiceDto> SendEmailAsync(Guid id, SendInvoiceEmailRequest request, CancellationToken ct = default)
    {
        var (_, conexion) = await _settings.RequireActiveAsync(ct);
        var f = await _db.ElectronicInvoices.Include(x => x.Patient).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("Factura no encontrada.");
        if (f.Numero is null || f.Estado is not (ElectronicInvoiceStatus.Emitida or ElectronicInvoiceStatus.Anulada))
            throw ApiException.Conflict("Solo se reenvía un documento emitido.");
        var correo = (string.IsNullOrWhiteSpace(request.Correo) ? f.Patient?.Email ?? f.ClienteEmail : request.Correo)?.Trim();
        if (string.IsNullOrWhiteSpace(correo) || !correo.Contains('@'))
            throw ApiException.BadRequest("El cliente no tiene correo: escribe uno.");
        try
        {
            await _provider.EnviarCorreoAsync(conexion, Kind(f.TipoDocumento), f.Numero, correo, ct);
        }
        catch (Exception e) when (e is FactusException or FactusAuthException or HttpRequestException)
        {
            throw new ApiException(StatusCodes.Status502BadGateway, $"{_provider.Nombre} no pudo enviar el correo: {e.Message}");
        }
        f.CorreoEnviadoA = correo;
        f.FechaCorreo = _clock.Now;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ── Consultas ───────────────────────────────────────────────────

    public async Task<ElectronicInvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var f = await _db.ElectronicInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("Factura no encontrada.");
        var dto = (await ToDtosAsync([f], ct))[0];
        var doc = Deserialize(f.Documento);
        var devuelto = f.TipoDocumento == ElectronicDocumentType.Factura ? await CreditedAsync(f.Id, ct) : [];
        dto.Lineas = doc.Lineas.Select((l, i) => new ElectronicInvoiceLineDto
        {
            Indice = i, Codigo = l.Codigo, Descripcion = l.Descripcion, Cantidad = l.Cantidad, PrecioUnitario = l.PrecioUnitario,
            Descuento = l.Descuento, IvaPorcentaje = l.IvaPorcentaje, Total = l.Total, CantidadDevuelta = devuelto.GetValueOrDefault(i),
        }).ToList();
        return dto;
    }

    public async Task<PagedResponse<ElectronicInvoiceDto>> ListAsync(InvoiceListFilter filter, CancellationToken ct = default)
    {
        var q = Filter(filter);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var page = Math.Max(filter.Page, 1);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(f => f.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<ElectronicInvoiceDto> { Items = await ToDtosAsync(rows, ct), Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<(byte[] Contenido, string Nombre)> ExportAsync(InvoiceListFilter filter, CancellationToken ct = default)
    {
        var rows = await Filter(filter).OrderByDescending(f => f.CreatedAt).Take(5000).ToListAsync(ct);
        var dtos = await ToDtosAsync(rows, ct);
        var clinica = await _db.Tenants.Where(t => t.Id == _db.CurrentTenantId).Select(t => t.Nombre).FirstOrDefaultAsync(ct) ?? "Clínica";
        var rango = filter.Desde is null && filter.Hasta is null ? "Todo el período" : $"{filter.Desde:dd/MM/yyyy} a {filter.Hasta:dd/MM/yyyy}";
        var contenido = ExcelTable.Build("Facturas", $"Facturación electrónica · {clinica}", rango,
            ["Documento", "Número", "Estado", "Emisión", "Venta", "Cliente", "Identificación", "Sede", "Factura origen", "CUFE/CUDE", "Motivo / mensaje", "Subtotal", "IVA", "Total"],
            dtos.Select(d => new object?[]
            {
                d.TipoDocumento == nameof(ElectronicDocumentType.NotaCredito) ? "Nota crédito" : "Factura",
                d.Numero, d.Estado, d.FechaEmision, d.VentaNumero is int n ? $"#{n}" : d.PaymentId is null ? null : "Abono",
                d.ClienteNombre, d.ClienteDocumento, d.BranchNombre, d.FacturaOrigenNumero, d.Cufe,
                d.Motivo ?? d.Mensaje, d.Subtotal, d.Impuestos, d.Total,
            }),
            moneyFromColumn: 12);
        return (contenido, $"facturas-{_clock.Today:yyyy-MM-dd}.xlsx");
    }

    private IQueryable<ElectronicInvoice> Filter(InvoiceListFilter f)
    {
        var q = _db.ElectronicInvoices.AsNoTracking();
        if (f.Desde is DateOnly desde)
        {
            var d = _clock.ToUtc(desde.ToDateTime(TimeOnly.MinValue));
            q = q.Where(x => x.CreatedAt >= d);
        }
        if (f.Hasta is DateOnly hasta)
        {
            var h = _clock.ToUtc(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue));
            q = q.Where(x => x.CreatedAt < h);
        }
        if (!string.IsNullOrWhiteSpace(f.Estado))
        {
            if (!Enum.TryParse<ElectronicInvoiceStatus>(f.Estado, true, out var estado)) throw ApiException.BadRequest("Estado inválido.");
            q = q.Where(x => x.Estado == estado);
        }
        if (!string.IsNullOrWhiteSpace(f.TipoDocumento))
        {
            if (!Enum.TryParse<ElectronicDocumentType>(f.TipoDocumento, true, out var tipo)) throw ApiException.BadRequest("Tipo de documento inválido.");
            q = q.Where(x => x.TipoDocumento == tipo);
        }
        if (f.BranchId is Guid b) q = q.Where(x => x.BranchId == b);
        if (f.PatientId is Guid p) q = q.Where(x => x.PatientId == p);
        if (f.SaleId is Guid s) q = q.Where(x => x.SaleId == s);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var t = f.Search.Trim();
            q = q.Where(x => (x.Numero != null && x.Numero.Contains(t)) || x.ClienteNombre.Contains(t)
                || x.ClienteDocumento.Contains(t) || x.Referencia.Contains(t));
        }
        return q;
    }

    private async Task<List<ElectronicInvoiceDto>> ToDtosAsync(List<ElectronicInvoice> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var saleIds = rows.Where(r => r.SaleId.HasValue).Select(r => r.SaleId!.Value).Distinct().ToList();
        var ventas = await _db.Sales.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == _db.CurrentTenantId && saleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Numero, ct);
        var sedes = await _db.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.TenantId == _db.CurrentTenantId).ToDictionaryAsync(b => b.Id, b => b.Nombre, ct);
        var userIds = rows.Select(r => r.EmitidaPorId).Distinct().ToList();
        var usuarios = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == _db.CurrentTenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.Nombre + " " + u.Apellido).Trim(), ct);
        var ids = rows.Select(r => r.Id).ToList();
        var origenIds = rows.Where(r => r.FacturaOrigenId.HasValue).Select(r => r.FacturaOrigenId!.Value).Distinct().ToList();
        var origenes = await _db.ElectronicInvoices.AsNoTracking().Where(f => origenIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Numero, ct);
        var notas = await _db.ElectronicInvoices.AsNoTracking()
            .Where(f => f.FacturaOrigenId != null && ids.Contains(f.FacturaOrigenId.Value) && f.Estado != ElectronicInvoiceStatus.Rechazada)
            .GroupBy(f => f.FacturaOrigenId!.Value)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Total) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var conceptos = _catalogs.Catalogs.CreditNoteCorrections;

        return rows.Select(f => new ElectronicInvoiceDto
        {
            Id = f.Id,
            TipoDocumento = f.TipoDocumento.ToString(),
            Estado = f.Estado.ToString(),
            Referencia = f.Referencia,
            Numero = f.Numero,
            Cufe = f.Cufe,
            FechaEmision = f.FechaEmision,
            Creada = f.CreatedAt,
            SaleId = f.SaleId,
            VentaNumero = f.SaleId is Guid s && ventas.TryGetValue(s, out var n) ? n : null,
            PaymentId = f.PaymentId,
            PatientId = f.PatientId,
            BranchId = f.BranchId,
            BranchNombre = sedes.GetValueOrDefault(f.BranchId, string.Empty),
            ClienteNombre = f.ClienteNombre,
            ClienteDocumento = f.ClienteDocumento,
            ClienteEmail = f.ClienteEmail,
            Subtotal = f.Subtotal,
            Impuestos = f.Impuestos,
            Total = f.Total,
            Mensaje = f.Mensaje,
            Advertencias = f.Advertencias,
            FacturaOrigenId = f.FacturaOrigenId,
            FacturaOrigenNumero = f.FacturaOrigenId is Guid o ? origenes.GetValueOrDefault(o) : null,
            ConceptoCorreccion = f.ConceptoCorreccion,
            ConceptoCorreccionNombre = _catalogs.Name(conceptos, f.ConceptoCorreccion),
            Motivo = f.Motivo,
            TotalNotasCredito = notas.GetValueOrDefault(f.Id),
            PdfId = f.PdfId,
            XmlId = f.XmlId,
            Intentos = f.Intentos,
            UltimoIntento = f.UltimoIntento,
            CorreoEnviadoA = f.CorreoEnviadoA,
            FechaCorreo = f.FechaCorreo,
            EmitidaPor = usuarios.GetValueOrDefault(f.EmitidaPorId, string.Empty),
        }).ToList();
    }

    // ── Armado ──────────────────────────────────────────────────────

    private static ElectronicInvoiceCustomer PatientCustomer(Patient p) => new()
    {
        TipoDocumento = p.TipoDocumento?.ToString() ?? "CC",
        Documento = p.Cedula,
        Nombre = $"{p.Nombre} {p.Apellido}".Trim(),
        Email = p.Email,
        Telefono = p.Telefono,
        Municipio = p.MunicipioResidencia,
    };

    /// <summary>Cliente de mostrador: sin documento es consumidor final; con guion se toma como NIT.</summary>
    private static ElectronicInvoiceCustomer WalkInCustomer(string? nombre, string? documento)
    {
        var doc = documento?.Trim();
        if (string.IsNullOrEmpty(doc))
            return new ElectronicInvoiceCustomer { ConsumidorFinal = true, Nombre = string.IsNullOrWhiteSpace(nombre) ? string.Empty : nombre.Trim() };
        var nit = doc.Contains('-');
        return new ElectronicInvoiceCustomer
        {
            TipoDocumento = nit ? "NIT" : "CC",
            Documento = doc,
            Nombre = string.IsNullOrWhiteSpace(nombre) ? "Cliente" : nombre.Trim(),
            PersonaJuridica = nit,
        };
    }

    /// <summary>code_reference de cada línea: referencia del producto, CUPS del procedimiento o un código corto.</summary>
    private async Task<Dictionary<Guid, string>> LineCodesAsync(List<SaleLine> lineas, CancellationToken ct)
    {
        var refs = lineas.Where(l => l.ReferenciaId.HasValue).Select(l => l.ReferenciaId!.Value).Distinct().ToList();
        var productos = await _db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => refs.Contains(p.Id) && p.TenantId == _db.CurrentTenantId).ToDictionaryAsync(p => p.Id, p => p.Referencia, ct);
        var procedimientos = await _db.Procedures.IgnoreQueryFilters().AsNoTracking()
            .Where(p => refs.Contains(p.Id) && p.TenantId == _db.CurrentTenantId).ToDictionaryAsync(p => p.Id, p => p.CodigoCups, ct);
        return lineas.ToDictionary(l => l.Id, l =>
        {
            var corto = l.ReferenciaId?.ToString("N")[..8].ToUpperInvariant();
            return l.Tipo switch
            {
                SaleLineType.Producto => productos.GetValueOrDefault(l.ReferenciaId ?? Guid.Empty) is { Length: > 0 } r ? r : $"PROD-{corto}",
                SaleLineType.Procedimiento => procedimientos.GetValueOrDefault(l.ReferenciaId ?? Guid.Empty) is { Length: > 0 } c ? c : $"PROC-{corto}",
                SaleLineType.Paquete => $"PAQ-{corto}",
                _ => "OTRO",
            };
        });
    }

    private static int? Range(InvoicingSettings settings, Guid branchId, ElectronicDocumentType tipo)
    {
        var sede = settings.Sedes.FirstOrDefault(s => s.BranchId == branchId && !s.IsDeleted);
        // Sin rango propio, el de otra sede que lo tenga (un solo consecutivo para la clínica); sin
        // ninguno, el proveedor usa el único rango activo de la cuenta.
        int? Pick(InvoicingBranchSettings? s) => tipo == ElectronicDocumentType.Factura ? s?.RangoFacturaId : s?.RangoNotaCreditoId;
        return Pick(sede) ?? settings.Sedes.Where(s => !s.IsDeleted).Select(Pick).FirstOrDefault(x => x.HasValue);
    }

    private static string NewReference(string origen) => $"NMC-{origen}-{Guid.NewGuid().ToString("N")[..16]}";

    private static ElectronicDocumentKind Kind(ElectronicDocumentType t) =>
        t == ElectronicDocumentType.Factura ? ElectronicDocumentKind.Factura : ElectronicDocumentKind.NotaCredito;

    private static ElectronicInvoiceDocument Document(ElectronicInvoice f) => Deserialize(f.Documento);

    private static ElectronicInvoiceDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<ElectronicInvoiceDocument>(json, Json) ?? new ElectronicInvoiceDocument();


    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(s))]
    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

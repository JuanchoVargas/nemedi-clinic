using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Cash;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Caja: apertura, movimientos, arqueo y cierre por sede y usuario.
///
/// Reglas:
///   · Un usuario tiene como máximo una caja abierta (índice único filtrado). Abrir otra → 409.
///   · Todo pago que el usuario registre mientras su caja está abierta queda en ella (ventas y abonos
///     de paquetes), sea cual sea el método. Solo el efectivo cuenta para el arqueo.
///   · Los ingresos y egresos manuales los registra el dueño de la caja (o el SuperAdmin).
///   · Cerrar congela los totales y la diferencia (contado − esperado). Una caja cerrada no cambia.
///   · Ver cajas: Admin y SuperAdmin ven todas las de la clínica (recepción supervisa); operar una
///     caja ajena solo el SuperAdmin (p. ej. cerrar la que alguien dejó abierta).
/// </summary>
public class CashService
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;

    public CashService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Caja abierta del usuario, o null. La usan los pagos para quedar en ella.</summary>
    public Task<Guid?> OpenSessionIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.CashSessions
            .Where(c => c.UsuarioId == userId && c.Estado == CashSessionStatus.Abierta)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<CashSessionDto?> CurrentAsync(Guid userId, string? role, CancellationToken ct = default)
    {
        var id = await OpenSessionIdAsync(userId, ct);
        return id is null ? null : await GetAsync(id.Value, userId, role, ct);
    }

    public async Task<CashSessionDto> OpenAsync(OpenCashRequest request, Guid userId, Guid? userBranchId, string? role, CancellationToken ct = default)
    {
        if (await OpenSessionIdAsync(userId, ct) is not null)
            throw ApiException.Conflict("Ya tienes una caja abierta. Ciérrala antes de abrir otra.");

        var branchId = await BranchLookup.ResolveAsync(_db, request.BranchId, userBranchId, ct);

        var session = new CashSession
        {
            BranchId = branchId,
            UsuarioId = userId,
            FechaApertura = _clock.Now,
            MontoApertura = SaleMath.Round(request.MontoApertura),
            ObservacionApertura = Limpio(request.Observacion),
            Estado = CashSessionStatus.Abierta
        };
        _db.CashSessions.Add(session);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos clics simultáneos: el índice único deja entrar solo una caja abierta por usuario
            throw ApiException.Conflict("Ya tienes una caja abierta. Ciérrala antes de abrir otra.");
        }

        return await GetAsync(session.Id, userId, role, ct);
    }

    public async Task<List<CashSessionDto>> ListAsync(
        DateOnly? desde, DateOnly? hasta, Guid? branchId, Guid? usuarioId, Guid userId, string? role, CancellationToken ct = default)
    {
        var query = _db.CashSessions.AsNoTracking();
        if (desde.HasValue)
            query = query.Where(c => c.FechaApertura >= desde.Value.ToDateTime(TimeOnly.MinValue));
        if (hasta.HasValue)
            query = query.Where(c => c.FechaApertura < hasta.Value.ToDateTime(TimeOnly.MinValue).AddDays(1));
        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);
        if (usuarioId.HasValue)
            query = query.Where(c => c.UsuarioId == usuarioId.Value);

        var sessions = await query
            .Include(c => c.Branch)
            .Include(c => c.Usuario)
            .OrderByDescending(c => c.FechaApertura)
            .Take(200)
            .ToListAsync(ct);

        var result = new List<CashSessionDto>();
        foreach (var session in sessions)
            result.Add(await ToDtoAsync(session, userId, role, withMovements: false, ct));
        return result;
    }

    public async Task<CashSessionDto> GetAsync(Guid id, Guid userId, string? role, CancellationToken ct = default)
    {
        var session = await _db.CashSessions.AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiException.NotFound("Caja no encontrada.");
        return await ToDtoAsync(session, userId, role, withMovements: true, ct);
    }

    public async Task<CashSessionDto> AddMovementAsync(Guid id, CashMovementRequest request, Guid userId, string? role, CancellationToken ct = default)
    {
        var session = await LoadOperableAsync(id, userId, role, ct);
        if (string.IsNullOrWhiteSpace(request.Concepto))
            throw ApiException.BadRequest("Indica el concepto.");
        if (request.Monto <= 0)
            throw ApiException.BadRequest("El monto debe ser mayor a cero.");

        if (request.Tipo == CashMovementType.Egreso)
        {
            // Un egreso sale del cajón: no puede sacar más efectivo del que hay
            var resumen = await SummaryAsync(session, ct);
            if (request.Monto > resumen.EfectivoEsperado)
                throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                    $"El egreso supera el efectivo en caja ({SaleMath.Pesos(resumen.EfectivoEsperado)}).");
        }

        _db.CashMovements.Add(new CashMovement
        {
            CashSessionId = session.Id,
            Tipo = request.Tipo,
            Concepto = request.Concepto.Trim(),
            Monto = SaleMath.Round(request.Monto),
            Fecha = _clock.Now,
            UsuarioId = userId
        });
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, userId, role, ct);
    }

    /// <summary>Arqueo y cierre: congela los totales y la diferencia contra lo esperado.</summary>
    public async Task<CashSessionDto> CloseAsync(Guid id, CloseCashRequest request, Guid userId, string? role, CancellationToken ct = default)
    {
        var session = await LoadOperableAsync(id, userId, role, ct);
        var resumen = await SummaryAsync(session, ct);
        var contado = SaleMath.Round(request.EfectivoContado);

        session.Estado = CashSessionStatus.Cerrada;
        session.FechaCierre = _clock.Now;
        session.CerradaPorId = userId;
        session.ObservacionCierre = Limpio(request.Observacion);
        session.TotalEfectivo = resumen.TotalEfectivo;
        session.TotalTransferencia = resumen.TotalTransferencia;
        session.TotalTarjeta = resumen.TotalTarjeta;
        session.TotalOtro = resumen.TotalOtro;
        session.TotalIngresos = resumen.TotalIngresos;
        session.TotalEgresos = resumen.TotalEgresos;
        session.EfectivoEsperado = resumen.EfectivoEsperado;
        session.EfectivoContado = contado;
        session.Diferencia = contado - resumen.EfectivoEsperado;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("La caja cambió mientras la cerrabas (otro pago o movimiento). Revisa el arqueo e inténtalo de nuevo.");
        }
        return await GetAsync(id, userId, role, ct);
    }

    /// <summary>
    /// Devolución en efectivo de una venta anulada cuyo cobro quedó en una caja ya cerrada (o en
    /// ninguna): sale como egreso de la caja abierta de quien anula. No guarda. Devuelve false si
    /// quien anula no tiene caja abierta.
    /// </summary>
    public async Task<bool> AddRefundAsync(Guid userId, decimal monto, string concepto, Guid saleId, CancellationToken ct = default)
    {
        var openId = await OpenSessionIdAsync(userId, ct);
        if (openId is null)
            return false;

        _db.CashMovements.Add(new CashMovement
        {
            CashSessionId = openId.Value,
            Tipo = CashMovementType.Egreso,
            Concepto = concepto,
            Monto = monto,
            Fecha = _clock.Now,
            UsuarioId = userId,
            SaleId = saleId
        });
        return true;
    }

    // ── Excel ────────────────────────────────────────────────────────

    /// <summary>Arqueo de una caja: hoja de resumen y hoja de movimientos.</summary>
    public static byte[] ToExcel(CashSessionDto caja, string clinica)
    {
        using var libro = new XLWorkbook();

        var resumen = libro.Worksheets.Add("Arqueo");
        resumen.Cell(1, 1).Value = $"{clinica} · arqueo de caja";
        resumen.Cell(2, 1).Value = $"{caja.BranchNombre} · {caja.Usuario} · abierta {caja.FechaApertura:dd/MM/yyyy HH:mm}"
            + (caja.FechaCierre is { } cierre ? $" · cerrada {cierre:dd/MM/yyyy HH:mm}" : " · ABIERTA");
        resumen.Range(1, 1, 1, 2).Merge().Style.Font.SetBold().Font.SetFontSize(13);
        resumen.Range(2, 1, 2, 2).Merge().Style.Font.SetItalic();

        var filas = new (string Concepto, decimal? Valor)[]
        {
            ("Base de apertura", caja.Resumen.MontoApertura),
            ("Pagos en efectivo", caja.Resumen.TotalEfectivo),
            ("Ingresos manuales", caja.Resumen.TotalIngresos),
            ("Egresos", -caja.Resumen.TotalEgresos),
            ("Efectivo esperado", caja.Resumen.EfectivoEsperado),
            ("Efectivo contado", caja.EfectivoContado),
            ("Diferencia", caja.Diferencia),
            ("", null),
            ("Transferencias", caja.Resumen.TotalTransferencia),
            ("Tarjeta", caja.Resumen.TotalTarjeta),
            ("Otros medios", caja.Resumen.TotalOtro),
            ("Total recaudado (todos los medios)", caja.Resumen.TotalPagos),
        };
        var fila = 4;
        foreach (var (concepto, valor) in filas)
        {
            resumen.Cell(fila, 1).Value = concepto;
            if (valor.HasValue)
            {
                resumen.Cell(fila, 2).Value = valor.Value;
                resumen.Cell(fila, 2).Style.NumberFormat.Format = "$ #,##0";
            }
            fila++;
        }
        resumen.Cell(8, 1).Style.Font.SetBold();
        resumen.Cell(10, 1).Style.Font.SetBold();
        if (!string.IsNullOrWhiteSpace(caja.ObservacionCierre))
            resumen.Cell(fila + 1, 1).Value = $"Observación de cierre: {caja.ObservacionCierre}";
        resumen.Columns(1, 2).AdjustToContents();

        var hoja = libro.Worksheets.Add("Movimientos");
        string[] encabezados = ["Fecha", "Tipo", "Concepto", "Medio", "Referencia", "Paciente", "Usuario", "Monto", "Estado"];
        for (var i = 0; i < encabezados.Length; i++)
            hoja.Cell(1, i + 1).Value = encabezados[i];
        hoja.Row(1).Style.Font.SetBold();
        hoja.SheetView.FreezeRows(1);
        fila = 2;
        foreach (var m in caja.Movimientos)
        {
            hoja.Cell(fila, 1).Value = m.Fecha;
            hoja.Cell(fila, 1).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            hoja.Cell(fila, 2).Value = m.Tipo;
            hoja.Cell(fila, 3).Value = m.Concepto;
            hoja.Cell(fila, 4).Value = m.MetodoPago ?? "Efectivo";
            hoja.Cell(fila, 5).Value = m.Referencia ?? "";
            hoja.Cell(fila, 6).Value = m.PacienteNombre ?? "";
            hoja.Cell(fila, 7).Value = m.Usuario ?? "";
            hoja.Cell(fila, 8).Value = m.Tipo == "Egreso" ? -m.Monto : m.Monto;
            hoja.Cell(fila, 8).Style.NumberFormat.Format = "$ #,##0";
            hoja.Cell(fila, 9).Value = m.Anulado ? "Anulado" : "";
            fila++;
        }
        hoja.Columns(1, encabezados.Length).AdjustToContents();

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Listado de cajas de un rango, una fila por caja.</summary>
    public static byte[] ListToExcel(List<CashSessionDto> cajas, string clinica, string rango)
    {
        string[] encabezados =
        [
            "Apertura", "Cierre", "Sede", "Usuario", "Estado", "Base", "Efectivo", "Transferencias", "Tarjeta", "Otros",
            "Ingresos", "Egresos", "Esperado", "Contado", "Diferencia"
        ];
        var filas = cajas.Select(c => new object?[]
        {
            c.FechaApertura, c.FechaCierre, c.BranchNombre, c.Usuario, c.Estado, c.Resumen.MontoApertura,
            c.Resumen.TotalEfectivo, c.Resumen.TotalTransferencia, c.Resumen.TotalTarjeta, c.Resumen.TotalOtro,
            c.Resumen.TotalIngresos, c.Resumen.TotalEgresos, c.Resumen.EfectivoEsperado, c.EfectivoContado, c.Diferencia
        });
        return ExcelTable.Build("Cajas", $"{clinica} · cajas", rango, encabezados, filas, moneyFromColumn: 6);
    }

    // ── Internos ────────────────────────────────────────────────────

    private async Task<CashSession> LoadOperableAsync(Guid id, Guid userId, string? role, CancellationToken ct)
    {
        var session = await _db.CashSessions.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ApiException.NotFound("Caja no encontrada.");
        if (session.Estado == CashSessionStatus.Cerrada)
            throw ApiException.Conflict("La caja ya está cerrada.");
        if (session.UsuarioId != userId && role != nameof(UserRole.SuperAdmin))
            throw new ApiException(StatusCodes.Status403Forbidden, "Solo quien abrió la caja (o el dueño de la clínica) puede operarla.");
        return session;
    }

    private async Task<CashSummaryDto> SummaryAsync(CashSession session, CancellationToken ct)
    {
        if (session.Estado == CashSessionStatus.Cerrada)
        {
            // Congelado al cerrar: nada de lo que pase después cambia un arqueo cerrado
            var efectivo = session.TotalEfectivo ?? 0;
            var transferencia = session.TotalTransferencia ?? 0;
            var tarjeta = session.TotalTarjeta ?? 0;
            var otro = session.TotalOtro ?? 0;
            return new CashSummaryDto
            {
                MontoApertura = session.MontoApertura,
                TotalEfectivo = efectivo,
                TotalTransferencia = transferencia,
                TotalTarjeta = tarjeta,
                TotalOtro = otro,
                TotalPagos = efectivo + transferencia + tarjeta + otro,
                TotalIngresos = session.TotalIngresos ?? 0,
                TotalEgresos = session.TotalEgresos ?? 0,
                EfectivoEsperado = session.EfectivoEsperado ?? 0,
                CantidadPagos = await _db.Payments.CountAsync(p => p.CashSessionId == session.Id, ct)
            };
        }

        // Los pagos anulados quedan IsDeleted: el filtro global ya los saca de la suma
        var porMetodo = await _db.Payments.AsNoTracking()
            .Where(p => p.CashSessionId == session.Id)
            .GroupBy(p => p.MetodoPago)
            .Select(g => new { Metodo = g.Key, Total = g.Sum(p => p.Monto), Cantidad = g.Count() })
            .ToListAsync(ct);
        var manuales = await _db.CashMovements.AsNoTracking()
            .Where(m => m.CashSessionId == session.Id)
            .GroupBy(m => m.Tipo)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(m => m.Monto) })
            .ToListAsync(ct);

        decimal Metodo(PaymentMethod metodo) => porMetodo.Where(x => x.Metodo == metodo).Sum(x => x.Total);
        var ingresos = manuales.Where(x => x.Tipo == CashMovementType.Ingreso).Sum(x => x.Total);
        var egresos = manuales.Where(x => x.Tipo == CashMovementType.Egreso).Sum(x => x.Total);

        return new CashSummaryDto
        {
            MontoApertura = session.MontoApertura,
            TotalEfectivo = Metodo(PaymentMethod.Efectivo),
            TotalTransferencia = Metodo(PaymentMethod.Transferencia),
            TotalTarjeta = Metodo(PaymentMethod.Tarjeta),
            TotalOtro = Metodo(PaymentMethod.Otro),
            TotalPagos = porMetodo.Sum(x => x.Total),
            TotalIngresos = ingresos,
            TotalEgresos = egresos,
            EfectivoEsperado = session.MontoApertura + Metodo(PaymentMethod.Efectivo) + ingresos - egresos,
            CantidadPagos = porMetodo.Sum(x => x.Cantidad)
        };
    }

    private async Task<CashSessionDto> ToDtoAsync(CashSession session, Guid userId, string? role, bool withMovements, CancellationToken ct)
    {
        var cerradaPor = session.CerradaPorId is { } cid
            ? await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == cid && u.TenantId == _db.CurrentTenantId)
                .Select(u => u.Nombre + " " + u.Apellido).FirstOrDefaultAsync(ct)
            : null;

        var dto = new CashSessionDto
        {
            Id = session.Id,
            BranchId = session.BranchId,
            BranchNombre = session.Branch?.Nombre ?? string.Empty,
            UsuarioId = session.UsuarioId,
            Usuario = session.Usuario is null ? string.Empty : $"{session.Usuario.Nombre} {session.Usuario.Apellido}".Trim(),
            Estado = session.Estado.ToString(),
            FechaApertura = session.FechaApertura,
            ObservacionApertura = session.ObservacionApertura,
            FechaCierre = session.FechaCierre,
            CerradaPor = cerradaPor,
            ObservacionCierre = session.ObservacionCierre,
            EfectivoContado = session.EfectivoContado,
            Diferencia = session.Diferencia,
            AbiertaDesdeOtroDia = session.Estado == CashSessionStatus.Abierta && session.FechaApertura.Date < _clock.Now.Date,
            PuedeOperar = session.Estado == CashSessionStatus.Abierta
                && (session.UsuarioId == userId || role == nameof(UserRole.SuperAdmin)),
            Resumen = await SummaryAsync(session, ct)
        };

        if (withMovements)
            dto.Movimientos = await MovementsAsync(session.Id, ct);
        return dto;
    }

    /// <summary>Pagos (incluidos los anulados, que se muestran tachados) + movimientos manuales, del más reciente al más antiguo.</summary>
    private async Task<List<CashMovementDto>> MovementsAsync(Guid sessionId, CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        // Sin filtro global para ver los anulados; el tenant se filtra a mano. Un pago borrado por
        // error (IsDeleted sin FechaAnulacion) no se muestra: nunca existió para la caja.
        var pagos = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.CashSessionId == sessionId && (!p.IsDeleted || p.FechaAnulacion != null))
            .Select(p => new
            {
                p.Id, p.Monto, p.MetodoPago, p.Referencia, p.CreatedAt, p.FechaAnulacion, p.SaleId, p.PatientId,
                p.PatientPackageId, p.RegistradoPorId
            })
            .ToListAsync(ct);

        var saleIds = pagos.Where(p => p.SaleId.HasValue).Select(p => p.SaleId!.Value).Distinct().ToList();
        var ventas = await _db.Sales.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && saleIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Numero, ct);

        var packageIds = pagos.Where(p => p.PatientPackageId.HasValue).Select(p => p.PatientPackageId!.Value).Distinct().ToList();
        var paquetes = await _db.PatientPackages.IgnoreQueryFilters().AsNoTracking()
            .Where(pp => pp.TenantId == tenantId && packageIds.Contains(pp.Id))
            .ToDictionaryAsync(pp => pp.Id, pp => pp.PackageNombre, ct);

        var patientIds = pagos.Where(p => p.PatientId.HasValue).Select(p => p.PatientId!.Value).Distinct().ToList();
        var pacientes = await _db.Patients.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nombre + " " + p.Apellido, ct);

        var manuales = await _db.CashMovements.AsNoTracking()
            .Where(m => m.CashSessionId == sessionId)
            .ToListAsync(ct);

        var userIds = pagos.Where(p => p.RegistradoPorId.HasValue).Select(p => p.RegistradoPorId!.Value)
            .Concat(manuales.Select(m => m.UsuarioId)).Distinct().ToList();
        var usuarios = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.Nombre + " " + u.Apellido).Trim(), ct);

        var manualSaleIds = manuales.Where(m => m.SaleId.HasValue).Select(m => m.SaleId!.Value).Except(ventas.Keys).ToList();
        if (manualSaleIds.Count > 0)
        {
            var extra = await _db.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == tenantId && manualSaleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Numero, ct);
            foreach (var (k, v) in extra) ventas[k] = v;
        }

        var lista = pagos.Select(p =>
        {
            string concepto;
            if (p.SaleId is { } sid && ventas.TryGetValue(sid, out var numero))
                concepto = p.PatientPackageId is { } ppid && paquetes.TryGetValue(ppid, out var paq)
                    ? $"Venta #{numero} · {paq}"
                    : $"Venta #{numero}";
            else if (p.PatientPackageId is { } ppid2 && paquetes.TryGetValue(ppid2, out var paquete))
                concepto = $"Abono paquete {paquete}";
            else
                concepto = "Pago";

            return new CashMovementDto
            {
                Id = p.Id,
                Tipo = "Pago",
                Concepto = concepto,
                Monto = p.Monto,
                MetodoPago = p.MetodoPago.ToString(),
                Referencia = p.Referencia,
                Fecha = _clock.ToLocal(p.CreatedAt),
                Usuario = p.RegistradoPorId.HasValue ? usuarios.GetValueOrDefault(p.RegistradoPorId.Value) : null,
                SaleId = p.SaleId,
                SaleNumero = p.SaleId.HasValue && ventas.TryGetValue(p.SaleId.Value, out var n) ? n : null,
                PatientId = p.PatientId,
                PacienteNombre = p.PatientId.HasValue ? pacientes.GetValueOrDefault(p.PatientId.Value) : null,
                Anulado = p.FechaAnulacion != null
            };
        }).ToList();

        lista.AddRange(manuales.Select(m => new CashMovementDto
        {
            Id = m.Id,
            Tipo = m.Tipo.ToString(),
            Concepto = m.Concepto,
            Monto = m.Monto,
            Fecha = m.Fecha,
            Usuario = usuarios.GetValueOrDefault(m.UsuarioId),
            SaleId = m.SaleId,
            SaleNumero = m.SaleId.HasValue && ventas.TryGetValue(m.SaleId.Value, out var numero) ? numero : null
        }));

        return lista.OrderByDescending(m => m.Fecha).ToList();
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

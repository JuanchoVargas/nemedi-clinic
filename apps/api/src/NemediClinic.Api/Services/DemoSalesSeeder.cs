using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Datos demo del módulo financiero (solo Development, lo llama DevController.SeedDemo):
///   · Dos productos de venta al público con su lote (protector solar y sérum).
///   · 20 ventas del mes: productos, servicios y mixtas, de pacientes y de mostrador, con
///     descuentos, varios medios de pago, tres con saldo (cartera) y una anulada.
///   · Una caja cerrada de ayer (con un faltante de $5.000) y la caja de hoy abierta con
///     movimientos, las dos del dueño; los pagos de ventas de ayer y de hoy quedan en ellas.
///
/// Idempotente por la observación de las ventas. No descuenta stock: el lote de cada producto
/// nace con lo vendido + lo que queda, así el semáforo muestra lo que debe y la historia cuadra.
/// ReanchorAsync mueve todo para que "hoy" siga siendo hoy (la caja abierta cae en el día).
/// </summary>
public class DemoSalesSeeder
{
    private readonly IClock _clock;
    public const string SaleMarker = "[demo] venta";
    private const string CashTodayMarker = "[demo] caja de hoy";
    private const string CashYesterdayMarker = "[demo] caja de ayer";
    private const string EntryMarker = "Compra de productos de venta (datos demo)";

    private readonly AppDbContext _db;

    public DemoSalesSeeder(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> SeedAsync(Guid userId, CancellationToken ct = default)
    {
        if (await _db.Sales.IgnoreQueryFilters().AnyAsync(s => s.TenantId == _db.CurrentTenantId && s.Observacion == SaleMarker, ct))
            return 0;

        var branch = await _db.Branches.OrderBy(b => b.CreatedAt).FirstAsync(ct);
        var pacientes = await _db.Patients.Where(p => p.Cedula.StartsWith("100000000")).OrderBy(p => p.Cedula).ToListAsync(ct);
        var procs = await _db.Procedures.Where(p => p.Activo).ToDictionaryAsync(p => p.Nombre, ct);
        if (pacientes.Count < 6 || procs.Count < 3)
            return 0;
        Procedure Proc(string inicio) => procs.Values.First(p => p.Nombre.StartsWith(inicio));

        var hoy = _clock.Now.Date;
        // Las ventas caben en el mes en curso (a comienzos de mes se aprietan en los primeros días)
        var diasAtras = Math.Min(hoy.Day - 1, 24);
        DateTime Dia(int i, int hora) => hoy.AddDays(-(int)Math.Round((19 - i) * diasAtras / 19.0)).AddHours(hora);

        // ── Productos de venta al público (con su lote, para que el FEFO tenga de dónde salir) ──
        var protector = await RetailProductAsync("Protector solar FPS 50 · 50 ml", "PROT-50", 89_000m, min: 6, "NSOC98765-21CO",
            vence: DateOnly.FromDateTime(hoy.AddDays(420)), quedan: 24, ct);
        var serum = await RetailProductAsync("Sérum de ácido hialurónico 30 ml", "SER-HA30", 120_000m, min: 5, "NSOC54321-22CO",
            vence: DateOnly.FromDateTime(hoy.AddDays(75)), quedan: 9, ct);
        await _db.SaveChangesAsync(ct); // los lotes tienen que existir para las salidas de abajo

        var tenantId = _db.CurrentTenantId;
        var iva = await _db.Tenants.Where(t => t.Id == tenantId).Select(t => new { t.IvaProductos, t.IvaServicios }).FirstAsync(ct);

        // ── Las 20 ventas: (día, hora, paciente o null, líneas, pagos) ──
        // Línea: (producto?, procedimiento?, cantidad, descuento). Pago: (monto, método) con monto 0 = el total.
        var plan = new (int Paciente, (Product? Prod, Procedure? Proc, decimal Cant, decimal Desc)[] Lineas, (decimal Monto, PaymentMethod Metodo)[] Pagos)[]
        {
            (0, [(protector, null, 1, 0)], [(0, PaymentMethod.Efectivo)]),
            (1, [(null, Proc("Limpieza"), 1, 0)], [(0, PaymentMethod.Tarjeta)]),
            (-1, [(serum, null, 1, 0)], [(0, PaymentMethod.Efectivo)]),
            (2, [(null, Proc("Peeling"), 1, 20_000), (protector, null, 1, 0)], [(0, PaymentMethod.Transferencia)]),
            (3, [(null, Proc("Hidratación"), 1, 0)], [(100_000, PaymentMethod.Efectivo)]),       // cartera
            (4, [(serum, null, 2, 0)], [(0, PaymentMethod.Tarjeta)]),
            (5, [(null, Proc("Masaje"), 2, 0)], [(0, PaymentMethod.Efectivo)]),
            (-1, [(protector, null, 2, 10_000)], [(0, PaymentMethod.Tarjeta)]),
            (6, [(null, Proc("Radiofrecuencia"), 1, 0), (serum, null, 1, 0)], [(150_000, PaymentMethod.Efectivo), (0, PaymentMethod.Tarjeta)]),
            (7, [(null, Proc("Limpieza"), 1, 0)], [(0, PaymentMethod.Transferencia)]),
            (0, [(serum, null, 1, 0)], [(0, PaymentMethod.Efectivo)]),
            (1, [(null, Proc("Peeling"), 1, 0)], []),                                              // cartera, sin abono
            (2, [(protector, null, 1, 0)], [(0, PaymentMethod.Efectivo)]),
            (-1, [(null, Proc("Hidratación"), 1, 0)], [(0, PaymentMethod.Tarjeta)]),              // anulada
            (3, [(null, Proc("Masaje"), 1, 0), (protector, null, 1, 0)], [(0, PaymentMethod.Transferencia)]),
            (4, [(null, Proc("Limpieza"), 1, 0)], [(0, PaymentMethod.Efectivo)]),                 // ayer
            (5, [(serum, null, 1, 0), (protector, null, 1, 0)], [(0, PaymentMethod.Tarjeta)]),   // ayer
            (6, [(null, Proc("Radiofrecuencia"), 1, 30_000)], [(0, PaymentMethod.Efectivo)]),     // hoy
            (-1, [(protector, null, 1, 0)], [(0, PaymentMethod.Efectivo)]),                       // hoy
            (7, [(null, Proc("Hidratación"), 1, 0)], [(80_000, PaymentMethod.Transferencia)]),    // hoy, cartera
        };
        const int anulada = 13;

        // Cajas: ayer (cerrada) y hoy (abierta), del mismo usuario
        var cajaAyer = new CashSession
        {
            // Nace cerrada (una sola caja abierta por usuario); CloseYesterdayAsync congela sus totales
            BranchId = branch.Id, UsuarioId = userId, Estado = CashSessionStatus.Cerrada,
            FechaApertura = hoy.AddDays(-1).AddHours(7).AddMinutes(30), MontoApertura = 200_000m, ObservacionApertura = CashYesterdayMarker
        };
        var cajaHoy = new CashSession
        {
            BranchId = branch.Id, UsuarioId = userId, Estado = CashSessionStatus.Abierta,
            FechaApertura = hoy.AddHours(7).AddMinutes(30), MontoApertura = 150_000m, ObservacionApertura = CashTodayMarker
        };
        _db.CashSessions.AddRange(cajaAyer, cajaHoy);

        var ultimo = await _db.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).MaxAsync(s => (int?)s.Numero, ct) ?? 0;
        var horaPago = new List<(Payment Pago, DateTime Cuando)>();
        var vendido = new Dictionary<Guid, decimal>();

        for (var i = 0; i < plan.Length; i++)
        {
            var (paciente, lineas, pagos) = plan[i];
            // Las tres últimas son de hoy y las dos anteriores de ayer: caen en las cajas
            var fecha = i >= 17 ? hoy.AddHours(9 + (i - 17) * 2) : i >= 15 ? hoy.AddDays(-1).AddHours(10 + (i - 15) * 3) : Dia(i, 9 + i % 8);
            var patient = paciente >= 0 ? pacientes[paciente % pacientes.Count] : null;
            var sale = new Sale
            {
                Numero = ultimo + i + 1,
                BranchId = branch.Id,
                PatientId = patient?.Id,
                ClienteNombre = patient is null ? (i % 2 == 0 ? "Cliente de mostrador" : "Natalia Gómez") : null,
                Fecha = fecha,
                Estado = SaleStatus.Confirmada,
                UsuarioId = userId,
                Observacion = SaleMarker
            };
            _db.Sales.Add(sale);

            var orden = 1;
            foreach (var (prod, proc, cant, desc) in lineas)
            {
                var precio = prod?.PrecioVenta ?? proc!.PrecioBase;
                var pct = prod is not null ? prod.IvaPorcentaje ?? iva.IvaProductos : proc!.IvaPorcentaje ?? iva.IvaServicios;
                var (subtotal, montoIva, total) = SaleMath.Line(cant, precio, desc, pct);
                _db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id, Orden = orden++,
                    Tipo = prod is not null ? SaleLineType.Producto : SaleLineType.Procedimiento,
                    ReferenciaId = prod?.Id ?? proc!.Id, Descripcion = prod?.Nombre ?? proc!.Nombre,
                    Cantidad = cant, PrecioUnitario = precio, Descuento = desc, IvaPorcentaje = pct,
                    Subtotal = subtotal, Iva = montoIva, Total = total
                });
                sale.Subtotal += subtotal;
                sale.Descuento += desc;
                sale.Impuestos += montoIva;
                sale.Total += total;
                if (prod is not null)
                    vendido[prod.Id] = vendido.GetValueOrDefault(prod.Id) + cant;
            }

            // Pagos: 0 = lo que falte
            var caja = fecha.Date == hoy ? cajaHoy : fecha.Date == hoy.AddDays(-1) ? cajaAyer : null;
            var pagado = 0m;
            foreach (var (monto, metodo) in pagos)
            {
                var valor = monto == 0 ? sale.Total - pagado : monto;
                pagado += valor;
                var pago = new Payment
                {
                    SaleId = sale.Id, PatientId = patient?.Id, CashSessionId = caja?.Id, Monto = valor,
                    FechaPago = DateOnly.FromDateTime(fecha), MetodoPago = metodo, RegistradoPorId = userId,
                    Referencia = metodo switch
                    {
                        PaymentMethod.Transferencia => $"TRF-{9000 + i * 37}",
                        PaymentMethod.Tarjeta => $"VCH-{400100 + i * 13}",
                        _ => null
                    }
                };
                _db.Payments.Add(pago);
                horaPago.Add((pago, fecha.AddMinutes(5)));
            }

            if (i == anulada)
            {
                sale.Estado = SaleStatus.Anulada;
                sale.FechaAnulacion = fecha.AddHours(2);
                sale.AnuladaPorId = userId;
                sale.MotivoAnulacion = "La paciente cambió el tratamiento por otro.";
                foreach (var p in horaPago.Where(h => h.Pago.SaleId == sale.Id))
                {
                    p.Pago.IsDeleted = true;
                    p.Pago.FechaAnulacion = sale.FechaAnulacion;
                }
            }
        }

        // ── Inventario: una salida por venta y producto desde el único lote de cada producto ──
        var lotes = await _db.ProductLots.Where(l => l.ProductId == protector.Id || l.ProductId == serum.Id)
            .ToDictionaryAsync(l => l.ProductId, ct);
        var ventasNuevas = _db.ChangeTracker.Entries<Sale>().Select(e => e.Entity).ToList();
        foreach (var linea in _db.ChangeTracker.Entries<SaleLine>().Select(e => e.Entity).Where(l => l.Tipo == SaleLineType.Producto).ToList())
        {
            var sale = ventasNuevas.First(s => s.Id == linea.SaleId);
            _db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = linea.ReferenciaId!.Value, ProductLotId = lotes[linea.ReferenciaId.Value].Id, Cantidad = linea.Cantidad,
                TipoMovimiento = MovementType.Salida, Referencia = $"Venta #{sale.Numero}", PatientId = sale.PatientId,
                SaleId = sale.Id, UserId = userId, FechaMovimiento = sale.Fecha
            });
        }
        // El lote nació con lo vendido + lo que queda: la historia cuadra sin tocar la existencia
        foreach (var (productId, cantidad) in vendido)
        {
            lotes[productId].CantidadInicial += cantidad;
            var entrada = await _db.InventoryEntries.FirstAsync(e => e.ProductLotId == lotes[productId].Id, ct);
            entrada.Cantidad = lotes[productId].CantidadInicial;
            var movimiento = await _db.InventoryMovements.FirstAsync(m => m.ProductLotId == lotes[productId].Id && m.TipoMovimiento == MovementType.Entrada, ct);
            movimiento.Cantidad = lotes[productId].CantidadInicial;
        }

        // ── Movimientos manuales de caja ──
        _db.CashMovements.AddRange(
            new CashMovement { CashSessionId = cajaAyer.Id, Tipo = CashMovementType.Egreso, Concepto = "Compra de insumos de aseo", Monto = 35_000m, Fecha = hoy.AddDays(-1).AddHours(15), UsuarioId = userId },
            new CashMovement { CashSessionId = cajaHoy.Id, Tipo = CashMovementType.Ingreso, Concepto = "Base adicional para cambio", Monto = 50_000m, Fecha = hoy.AddHours(8), UsuarioId = userId },
            new CashMovement { CashSessionId = cajaHoy.Id, Tipo = CashMovementType.Egreso, Concepto = "Domicilio de insumos", Monto = 12_000m, Fecha = hoy.AddHours(11), UsuarioId = userId });

        await _db.SaveChangesAsync(ct);

        // CreatedAt lo pone SaveChanges en "ahora": se ajusta a la hora real del cobro (la caja lo muestra)
        foreach (var (pago, cuando) in horaPago)
        {
            var utc = _clock.ToUtc(cuando);
            await _db.Payments.IgnoreQueryFilters().Where(p => p.Id == pago.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, utc), ct);
        }

        // Cierre de la caja de ayer: totales congelados y un faltante de $5.000
        await CloseYesterdayAsync(cajaAyer, userId, ct);
        return plan.Length;
    }

    /// <summary>Mueve ventas, pagos, cajas y salidas para que la caja demo abierta caiga hoy.</summary>
    public async Task<int> ReanchorAsync(CancellationToken ct = default)
    {
        var cajaHoy = await _db.CashSessions.FirstOrDefaultAsync(c => c.ObservacionApertura == CashTodayMarker, ct);
        if (cajaHoy is null)
            return 0;
        var dias = (_clock.Now.Date - cajaHoy.FechaApertura.Date).Days;
        if (dias == 0)
            return 0;

        var tenantId = _db.CurrentTenantId;
        var saleIds = await _db.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && s.Observacion == SaleMarker).Select(s => s.Id).ToListAsync(ct);
        await _db.Sales.IgnoreQueryFilters().Where(s => saleIds.Contains(s.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Fecha, x => x.Fecha.AddDays(dias))
                .SetProperty(x => x.FechaAnulacion, x => x.FechaAnulacion == null ? null : x.FechaAnulacion.Value.AddDays(dias)), ct);
        await _db.Payments.IgnoreQueryFilters().Where(p => p.SaleId != null && saleIds.Contains(p.SaleId.Value))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FechaPago, x => x.FechaPago.AddDays(dias))
                .SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddDays(dias)), ct);
        await _db.InventoryMovements.Where(m => m.SaleId != null && saleIds.Contains(m.SaleId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaMovimiento, x => x.FechaMovimiento.AddDays(dias)), ct);

        var cajas = await _db.CashSessions
            .Where(c => c.ObservacionApertura == CashTodayMarker || c.ObservacionApertura == CashYesterdayMarker)
            .Select(c => c.Id).ToListAsync(ct);
        await _db.CashSessions.Where(c => cajas.Contains(c.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FechaApertura, x => x.FechaApertura.AddDays(dias))
                .SetProperty(x => x.FechaCierre, x => x.FechaCierre == null ? null : x.FechaCierre.Value.AddDays(dias)), ct);
        await _db.CashMovements.Where(m => cajas.Contains(m.CashSessionId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Fecha, x => x.Fecha.AddDays(dias)), ct);

        // La compra de los productos de venta viaja con las ventas (no con la compra inicial del demo)
        var lotIds = await _db.InventoryEntries.Where(e => e.Observacion == EntryMarker && e.ProductLotId != null)
            .Select(e => e.ProductLotId!.Value).ToListAsync(ct);
        await _db.InventoryEntries.Where(e => e.Observacion == EntryMarker)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaEntrada, x => x.FechaEntrada.AddDays(dias)), ct);
        await _db.InventoryMovements.Where(m => m.Referencia == EntryMarker)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaMovimiento, x => x.FechaMovimiento.AddDays(dias)), ct);
        await _db.ProductLots.Where(l => lotIds.Contains(l.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaIngreso, x => x.FechaIngreso.AddDays(dias)), ct);

        // Si alguien cerró la caja demo de hoy durante una demo, vuelve a quedar abierta (salvo que
        // su dueño ya tenga otra abierta: una sola por usuario)
        var reabrir = await _db.CashSessions.FirstAsync(c => c.Id == cajaHoy.Id, ct);
        if (reabrir.Estado == CashSessionStatus.Cerrada
            && !await _db.CashSessions.AnyAsync(c => c.UsuarioId == reabrir.UsuarioId && c.Estado == CashSessionStatus.Abierta, ct))
        {
            reabrir.Estado = CashSessionStatus.Abierta;
            reabrir.FechaCierre = null;
            reabrir.CerradaPorId = null;
            reabrir.ObservacionCierre = null;
            reabrir.TotalEfectivo = reabrir.TotalTransferencia = reabrir.TotalTarjeta = reabrir.TotalOtro = null;
            reabrir.TotalIngresos = reabrir.TotalEgresos = reabrir.EfectivoEsperado = reabrir.EfectivoContado = reabrir.Diferencia = null;
            await _db.SaveChangesAsync(ct);
        }
        return dias;
    }

    // ── Internos ────────────────────────────────────────────────────

    private async Task<Product> RetailProductAsync(string nombre, string referencia, decimal precio, decimal min, string invima,
        DateOnly vence, decimal quedan, CancellationToken ct)
    {
        var existente = await _db.Products.FirstOrDefaultAsync(p => p.Referencia == referencia, ct);
        if (existente is not null)
            return existente;

        var ingreso = _clock.Now.Date.AddDays(-Math.Min(_clock.Now.Day - 1, 24) - 2).AddHours(9);
        var producto = new Product
        {
            Nombre = nombre, Descripcion = nombre, Referencia = referencia, TipoProducto = ProductType.Venta,
            UnidadMedida = "unidad", StockActual = quedan, StockMinimo = min, StockMaximo = min * 6, Activo = true,
            TipoRegulatorio = RegulatoryType.Cosmetico, RegistroSanitarioInvima = invima, PrecioVenta = precio
        };
        _db.Products.Add(producto);
        var lote = new ProductLot
        {
            ProductId = producto.Id, NumeroLote = $"DEMO-{referencia}", FechaVencimiento = vence,
            CantidadInicial = quedan, CantidadDisponible = quedan, FechaIngreso = ingreso,
            Proveedor = "Dermocosmética Andina SAS", NumeroFactura = $"FV-2026-{referencia}", RegistroSanitario = invima
        };
        _db.ProductLots.Add(lote);
        _db.InventoryEntries.Add(new InventoryEntry
        {
            ProductId = producto.Id, Cantidad = quedan, MotivoEntrada = EntryReason.Compra, Observacion = EntryMarker,
            UserId = (await _db.Users.OrderBy(u => u.CreatedAt).FirstAsync(ct)).Id, FechaEntrada = ingreso, ProductLotId = lote.Id
        });
        _db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = producto.Id, ProductLotId = lote.Id, Cantidad = quedan, TipoMovimiento = MovementType.Entrada,
            Referencia = EntryMarker, UserId = (await _db.Users.OrderBy(u => u.CreatedAt).FirstAsync(ct)).Id, FechaMovimiento = ingreso
        });
        return producto;
    }

    private async Task CloseYesterdayAsync(CashSession caja, Guid userId, CancellationToken ct)
    {
        var pagos = await _db.Payments.Where(p => p.CashSessionId == caja.Id).ToListAsync(ct);
        var manuales = await _db.CashMovements.Where(m => m.CashSessionId == caja.Id).ToListAsync(ct);
        decimal Metodo(PaymentMethod m) => pagos.Where(p => p.MetodoPago == m).Sum(p => p.Monto);
        var ingresos = manuales.Where(m => m.Tipo == CashMovementType.Ingreso).Sum(m => m.Monto);
        var egresos = manuales.Where(m => m.Tipo == CashMovementType.Egreso).Sum(m => m.Monto);
        var esperado = caja.MontoApertura + Metodo(PaymentMethod.Efectivo) + ingresos - egresos;

        caja.Estado = CashSessionStatus.Cerrada;
        caja.FechaCierre = caja.FechaApertura.Date.AddHours(19).AddMinutes(10);
        caja.CerradaPorId = userId;
        caja.TotalEfectivo = Metodo(PaymentMethod.Efectivo);
        caja.TotalTransferencia = Metodo(PaymentMethod.Transferencia);
        caja.TotalTarjeta = Metodo(PaymentMethod.Tarjeta);
        caja.TotalOtro = Metodo(PaymentMethod.Otro);
        caja.TotalIngresos = ingresos;
        caja.TotalEgresos = egresos;
        caja.EfectivoEsperado = esperado;
        caja.EfectivoContado = esperado - 5_000m;
        caja.Diferencia = -5_000m;
        caja.ObservacionCierre = "Faltaron $5.000: se revisa con el recibo del domicilio.";
        await _db.SaveChangesAsync(ct);
    }
}

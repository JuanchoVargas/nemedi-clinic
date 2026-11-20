using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Reports;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Reportes gerenciales (policy Admin), todos con su versión en Excel:
///   · Ventas por período: confirmadas y anuladas, por día, por tipo de línea y por medio de pago.
///   · Procedimientos realizados: citas Completadas por procedimiento, de paquete o sueltas.
///   · Producción por esteticista: citas, horas de cabina, pacientes y valor producido.
///   · Cartera por paciente: lo que se debe hoy, de paquetes asignados sin venta y de ventas.
///     Misma regla que el dashboard: un paquete vendido cuenta dentro de su venta y los paquetes
///     Vencidos no suman.
/// Por defecto el período es el mes en curso.
/// </summary>
public class ReportService
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    private (DateOnly Desde, DateOnly Hasta, DateTime Inicio, DateTime Fin) Rango(DateOnly? desde, DateOnly? hasta)
    {
        var hoy = _clock.Today;
        var to = hasta ?? hoy;
        var from = desde ?? new DateOnly(to.Year, to.Month, 1);
        if (from > to)
            throw ApiException.BadRequest("La fecha inicial no puede ser posterior a la final.");
        return (from, to, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue).AddDays(1));
    }

    // ── Ventas ──────────────────────────────────────────────────────

    public async Task<SalesReportDto> SalesAsync(DateOnly? desde, DateOnly? hasta, Guid? branchId, CancellationToken ct = default)
    {
        var (from, to, inicio, fin) = Rango(desde, hasta);
        var query = _db.Sales.AsNoTracking()
            .Where(s => s.Estado != SaleStatus.Borrador && s.Fecha >= inicio && s.Fecha < fin);
        if (branchId.HasValue)
            query = query.Where(s => s.BranchId == branchId.Value);

        var ventas = await query
            .OrderBy(s => s.Fecha)
            .Select(s => new
            {
                s.Id, s.Numero, s.Fecha, s.Estado, s.Subtotal, s.Impuestos, s.Total,
                Cliente = s.Patient != null ? s.Patient.Nombre + " " + s.Patient.Apellido : s.ClienteNombre ?? "Cliente de mostrador",
                Documento = s.Patient != null ? s.Patient.Cedula : s.ClienteDocumento,
                Vendedor = s.Usuario.Nombre + " " + s.Usuario.Apellido,
                Sede = s.Branch.Nombre,
                Pagado = s.Pagos.Sum(p => p.Monto)
            })
            .ToListAsync(ct);

        var confirmadas = ventas.Where(v => v.Estado == SaleStatus.Confirmada).ToList();
        var ids = confirmadas.Select(v => v.Id).ToList();

        var lineas = await _db.SaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.SaleId))
            .GroupBy(l => l.Tipo)
            .Select(g => new SalesByTypeDto
            {
                Tipo = g.Key.ToString(),
                Cantidad = g.Sum(l => l.Cantidad),
                Subtotal = g.Sum(l => l.Subtotal),
                Iva = g.Sum(l => l.Iva),
                Total = g.Sum(l => l.Total)
            })
            .ToListAsync(ct);

        var metodos = await _db.Payments.AsNoTracking()
            .Where(p => p.SaleId != null && ids.Contains(p.SaleId.Value))
            .GroupBy(p => p.MetodoPago)
            .Select(g => new SalesByMethodDto { Metodo = g.Key.ToString(), Total = g.Sum(p => p.Monto) })
            .ToListAsync(ct);

        return new SalesReportDto
        {
            Desde = from,
            Hasta = to,
            Ventas = confirmadas.Count,
            Anuladas = ventas.Count - confirmadas.Count,
            Subtotal = confirmadas.Sum(v => v.Subtotal),
            Impuestos = confirmadas.Sum(v => v.Impuestos),
            Total = confirmadas.Sum(v => v.Total),
            Pagado = confirmadas.Sum(v => v.Pagado),
            Saldo = confirmadas.Sum(v => Math.Max(0, v.Total - v.Pagado)),
            PorDia = confirmadas
                .GroupBy(v => DateOnly.FromDateTime(v.Fecha))
                .Select(g => new SalesByDayDto { Fecha = g.Key, Ventas = g.Count(), Total = g.Sum(v => v.Total) })
                .ToList(),
            PorTipo = lineas.OrderByDescending(l => l.Total).ToList(),
            PorMetodo = metodos.OrderByDescending(m => m.Total).ToList(),
            Filas = ventas.OrderByDescending(v => v.Fecha).Select(v => new SalesReportRowDto
            {
                Id = v.Id,
                Numero = v.Numero,
                Fecha = v.Fecha,
                Cliente = v.Cliente,
                Documento = v.Documento,
                Estado = v.Estado.ToString(),
                Subtotal = v.Subtotal,
                Impuestos = v.Impuestos,
                Total = v.Total,
                Pagado = v.Estado == SaleStatus.Confirmada ? v.Pagado : 0,
                Saldo = v.Estado == SaleStatus.Confirmada ? Math.Max(0, v.Total - v.Pagado) : 0,
                Vendedor = v.Vendedor.Trim(),
                Sede = v.Sede
            }).ToList()
        };
    }

    // ── Procedimientos realizados ───────────────────────────────────

    public async Task<ProceduresReportDto> ProceduresAsync(DateOnly? desde, DateOnly? hasta, Guid? branchId, CancellationToken ct = default)
    {
        var (from, to, inicio, fin) = Rango(desde, hasta);
        var citas = _db.Appointments.AsNoTracking()
            .Where(a => a.Estado == AppointmentStatus.Completada && a.FechaInicio >= inicio && a.FechaInicio < fin)
            // Lo de un paciente eliminado no cuenta (la navegación no aplica su filtro)
            .Where(a => _db.Patients.Any(p => p.Id == a.PatientId));
        if (branchId.HasValue)
            citas = citas.Where(a => a.BranchId == branchId.Value);

        var filas = await citas
            .Select(a => new
            {
                a.ProcedureId,
                Procedimiento = a.Procedure.Nombre,
                a.Procedure.PrecioBase,
                DePaquete = a.PatientPackageSessionId != null,
                Esteticista = a.Esteticist.Nombre + " " + a.Esteticist.Apellido
            })
            .ToListAsync(ct);

        var dto = new ProceduresReportDto
        {
            Desde = from,
            Hasta = to,
            Filas = filas
                .GroupBy(f => new { f.ProcedureId, f.Procedimiento, f.PrecioBase })
                .Select(g => new ProcedureReportRowDto
                {
                    ProcedureId = g.Key.ProcedureId,
                    Procedimiento = g.Key.Procedimiento,
                    Realizadas = g.Count(),
                    DePaquete = g.Count(x => x.DePaquete),
                    Sueltas = g.Count(x => !x.DePaquete),
                    Valor = g.Key.PrecioBase * g.Count(),
                    Esteticistas = string.Join(" · ", g.GroupBy(x => x.Esteticista.Trim())
                        .OrderByDescending(e => e.Count())
                        .Select(e => $"{e.Key} ({e.Count()})"))
                })
                .OrderByDescending(r => r.Realizadas)
                .ThenBy(r => r.Procedimiento)
                .ToList()
        };
        dto.Total = dto.Filas.Sum(f => f.Realizadas);
        dto.ValorTotal = dto.Filas.Sum(f => f.Valor);
        return dto;
    }

    // ── Producción por esteticista ──────────────────────────────────

    public async Task<ProductionReportDto> ProductionAsync(DateOnly? desde, DateOnly? hasta, Guid? branchId, CancellationToken ct = default)
    {
        var (from, to, inicio, fin) = Rango(desde, hasta);
        var citas = _db.Appointments.AsNoTracking()
            .Where(a => a.FechaInicio >= inicio && a.FechaInicio < fin)
            .Where(a => _db.Patients.Any(p => p.Id == a.PatientId));
        if (branchId.HasValue)
            citas = citas.Where(a => a.BranchId == branchId.Value);

        var filas = await citas
            .Select(a => new
            {
                a.EsteticistId,
                Esteticista = a.Esteticist.Nombre + " " + a.Esteticist.Apellido,
                a.Estado,
                a.PatientId,
                a.FechaInicio,
                a.FechaFin,
                a.Procedure.PrecioBase
            })
            .ToListAsync(ct);

        var notas = await _db.ClinicalNotes.AsNoTracking()
            .Where(n => n.FechaCreacion >= inicio && n.FechaCreacion < fin
                && _db.Patients.Any(p => p.Id == n.ClinicalRecord.PatientId))
            .GroupBy(n => n.EsteticistId)
            .Select(g => new { EsteticistId = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.EsteticistId, x => x.Cantidad, ct);

        return new ProductionReportDto
        {
            Desde = from,
            Hasta = to,
            Filas = filas
                .GroupBy(f => new { f.EsteticistId, Nombre = f.Esteticista.Trim() })
                .Select(g =>
                {
                    var hechas = g.Where(x => x.Estado == AppointmentStatus.Completada).ToList();
                    return new ProductionRowDto
                    {
                        EsteticistId = g.Key.EsteticistId,
                        Esteticista = g.Key.Nombre,
                        Completadas = hechas.Count,
                        Canceladas = g.Count(x => x.Estado == AppointmentStatus.Cancelada),
                        NoAsistio = g.Count(x => x.Estado == AppointmentStatus.NoConfirmo),
                        Horas = Math.Round((decimal)hechas.Sum(x => (x.FechaFin - x.FechaInicio).TotalHours), 1),
                        Pacientes = hechas.Select(x => x.PatientId).Distinct().Count(),
                        ValorProducido = hechas.Sum(x => x.PrecioBase),
                        NotasClinicas = notas.GetValueOrDefault(g.Key.EsteticistId)
                    };
                })
                .OrderByDescending(r => r.ValorProducido)
                .ToList()
        };
    }

    // ── Cartera ─────────────────────────────────────────────────────

    public async Task<CarteraReportDto> CarteraAsync(CancellationToken ct = default)
    {
        var hoy = _clock.Today;

        var paquetes = await _db.PatientPackages.AsNoTracking()
            // Un paciente eliminado no tiene cartera: se descarta explícito (la navegación no aplica su filtro)
            .Where(pp => pp.SaleId == null && pp.Estado != PackageStatus.Vencido && _db.Patients.Any(p => p.Id == pp.PatientId))
            .Select(pp => new
            {
                pp.PatientId,
                Cliente = pp.Patient.Nombre + " " + pp.Patient.Apellido,
                pp.Patient.Cedula,
                pp.Patient.Telefono,
                Desde = pp.FechaInicio,
                Saldo = pp.PrecioAcordado - pp.Payments.Sum(p => p.Monto)
            })
            .Where(x => x.Saldo > 0)
            .ToListAsync(ct);

        var ventas = await _db.Sales.AsNoTracking()
            .Where(s => s.Estado == SaleStatus.Confirmada)
            .Select(s => new
            {
                s.PatientId,
                Cliente = s.Patient != null ? s.Patient.Nombre + " " + s.Patient.Apellido : s.ClienteNombre ?? "Cliente de mostrador",
                Cedula = s.Patient != null ? s.Patient.Cedula : s.ClienteDocumento,
                Telefono = s.Patient != null ? s.Patient.Telefono : null,
                s.Fecha,
                s.Numero,
                Saldo = s.Total - s.Pagos.Sum(p => p.Monto)
            })
            .Where(x => x.Saldo > 0)
            .ToListAsync(ct);

        var documentos = paquetes
            .Select(p => new { Clave = p.PatientId.ToString(), PatientId = (Guid?)p.PatientId, p.Cliente, Doc = (string?)p.Cedula, Tel = (string?)p.Telefono, p.Desde, Paquetes = p.Saldo, Ventas = 0m })
            .Concat(ventas.Select(v => new
            {
                // Una venta de mostrador es su propia fila de cartera
                Clave = v.PatientId?.ToString() ?? $"venta-{v.Numero}",
                v.PatientId,
                Cliente = v.PatientId is null ? $"{v.Cliente} (venta #{v.Numero})" : v.Cliente,
                Doc = v.Cedula,
                Tel = v.Telefono,
                Desde = DateOnly.FromDateTime(v.Fecha),
                Paquetes = 0m,
                Ventas = v.Saldo
            }))
            .ToList();

        var filas = documentos
            .GroupBy(d => d.Clave)
            .Select(g =>
            {
                var primero = g.First();
                var desde = g.Min(d => d.Desde);
                return new CarteraRowDto
                {
                    PatientId = primero.PatientId,
                    Cliente = primero.Cliente,
                    Documento = primero.Doc,
                    Telefono = primero.Tel,
                    SaldoPaquetes = g.Sum(d => d.Paquetes),
                    SaldoVentas = g.Sum(d => d.Ventas),
                    Total = g.Sum(d => d.Paquetes + d.Ventas),
                    Documentos = g.Count(),
                    DesdeCuando = desde,
                    DiasMora = Math.Max(0, hoy.DayNumber - desde.DayNumber)
                };
            })
            .OrderByDescending(f => f.Total)
            .ToList();

        return new CarteraReportDto { Corte = hoy, Total = filas.Sum(f => f.Total), Clientes = filas.Count, Filas = filas };
    }

    // ── Excel ───────────────────────────────────────────────────────

    public static byte[] SalesExcel(SalesReportDto r, string clinica)
    {
        using var libro = new ClosedXML.Excel.XLWorkbook();
        var rango = $"Del {r.Desde:dd/MM/yyyy} al {r.Hasta:dd/MM/yyyy} · {r.Ventas} ventas · total {SaleMath.Pesos(r.Total)}";
        ExcelTable.AddSheet(libro, "Ventas", $"{clinica} · ventas", rango,
            ["Número", "Fecha", "Cliente", "Documento", "Estado", "Subtotal", "IVA", "Total", "Pagado", "Saldo", "Vendedor", "Sede"],
            r.Filas.Select(f => new object?[] { f.Numero, f.Fecha, f.Cliente, f.Documento, f.Estado, f.Subtotal, f.Impuestos, f.Total, f.Pagado, f.Saldo, f.Vendedor, f.Sede }),
            moneyColumns: [6, 7, 8, 9, 10]);
        ExcelTable.AddSheet(libro, "Por día", $"{clinica} · ventas por día", rango,
            ["Fecha", "Ventas", "Total"],
            r.PorDia.Select(d => new object?[] { d.Fecha, d.Ventas, d.Total }), moneyColumns: [3]);
        ExcelTable.AddSheet(libro, "Por tipo", $"{clinica} · ventas por tipo", rango,
            ["Tipo", "Cantidad", "Subtotal", "IVA", "Total"],
            r.PorTipo.Select(t => new object?[] { t.Tipo, t.Cantidad, t.Subtotal, t.Iva, t.Total }), moneyColumns: [3, 4, 5]);
        ExcelTable.AddSheet(libro, "Por medio de pago", $"{clinica} · cobrado por medio de pago", rango,
            ["Medio", "Total"],
            r.PorMetodo.Select(m => new object?[] { m.Metodo, m.Total }), moneyColumns: [2]);
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] ProceduresExcel(ProceduresReportDto r, string clinica) =>
        ExcelTable.Build("Procedimientos", $"{clinica} · procedimientos realizados",
            $"Del {r.Desde:dd/MM/yyyy} al {r.Hasta:dd/MM/yyyy} · {r.Total} realizados",
            ["Procedimiento", "Realizadas", "De paquete", "Sueltas", "Valor (precio base)", "Esteticistas"],
            r.Filas.Select(f => new object?[] { f.Procedimiento, f.Realizadas, f.DePaquete, f.Sueltas, f.Valor, f.Esteticistas }),
            moneyColumns: [5]);

    public static byte[] ProductionExcel(ProductionReportDto r, string clinica) =>
        ExcelTable.Build("Producción", $"{clinica} · producción por esteticista",
            $"Del {r.Desde:dd/MM/yyyy} al {r.Hasta:dd/MM/yyyy}",
            ["Esteticista", "Completadas", "Canceladas", "No asistió", "Horas de cabina", "Pacientes", "Notas clínicas", "Valor producido"],
            r.Filas.Select(f => new object?[] { f.Esteticista, f.Completadas, f.Canceladas, f.NoAsistio, f.Horas, f.Pacientes, f.NotasClinicas, f.ValorProducido }),
            moneyColumns: [8]);

    public static byte[] CarteraExcel(CarteraReportDto r, string clinica) =>
        ExcelTable.Build("Cartera", $"{clinica} · cartera por paciente",
            $"Corte {r.Corte:dd/MM/yyyy} · {r.Clientes} clientes · total {SaleMath.Pesos(r.Total)}",
            ["Cliente", "Documento", "Teléfono", "Saldo paquetes", "Saldo ventas", "Total", "Documentos", "Desde", "Días"],
            r.Filas.Select(f => new object?[] { f.Cliente, f.Documento, f.Telefono, f.SaldoPaquetes, f.SaldoVentas, f.Total, f.Documentos, f.DesdeCuando, f.DiasMora }),
            moneyColumns: [4, 5, 6]);
}

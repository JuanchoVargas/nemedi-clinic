using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Tenants;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Configuración de la clínica: datos, políticas de agenda, IVA por defecto y horario de cada sede.
///
/// Reglas de agenda que aplica (ValidateSlotAsync), al crear y al reagendar una cita:
///   · Anticipación mínima: si la clínica la fija (> 0 h), la cita no puede empezar antes de
///     ahora + esas horas. En 0 no hay restricción (el personal puede registrar citas pasadas).
///   · Horario de la sede: si la sede tiene horario, la cita debe caer completa dentro de una de
///     sus franjas de ese día; un día sin franja está cerrado. Sin horario, no hay restricción.
/// </summary>
public class ClinicSettingsService
{
    private readonly IClock _clock;
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");
    private readonly AppDbContext _db;
    private readonly AttachmentService _attachments;

    public ClinicSettingsService(AppDbContext db, AttachmentService attachments, IClock clock)
    {
        _clock = clock;
        _db = db;
        _attachments = attachments;
    }

    public async Task<ClinicSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var tenantId = _db.CurrentTenantId;
        return await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new ClinicSettingsDto
            {
                Id = t.Id,
                Nombre = t.Nombre,
                NIT = t.NIT,
                Telefono = t.Telefono,
                Email = t.Email,
                Direccion = t.Direccion,
                Ciudad = t.Ciudad,
                LogoId = t.LogoId,
                AnticipacionMinimaHoras = t.AnticipacionMinimaHoras,
                ExigirConsentimiento = t.ExigirConsentimiento,
                IvaProductos = t.IvaProductos,
                IvaServicios = t.IvaServicios,
                EsIps = t.EsIps,
                CodigoHabilitacionReps = t.CodigoHabilitacionReps
            })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Clínica no encontrada.");
    }

    public async Task<ClinicSettingsDto> UpdateAsync(UpdateClinicSettingsRequest request, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _db.CurrentTenantId, ct)
            ?? throw ApiException.NotFound("Clínica no encontrada.");

        var nit = request.NIT.Trim();
        if (nit != tenant.NIT && await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.NIT == nit && t.Id != tenant.Id, ct))
            throw ApiException.Conflict("Ya existe otra clínica con ese NIT.");

        tenant.Nombre = request.Nombre.Trim();
        tenant.NIT = nit;
        tenant.Telefono = request.Telefono.Trim();
        tenant.Email = request.Email.Trim();
        tenant.Direccion = Limpio(request.Direccion);
        tenant.Ciudad = Limpio(request.Ciudad);
        tenant.AnticipacionMinimaHoras = request.AnticipacionMinimaHoras;
        tenant.ExigirConsentimiento = request.ExigirConsentimiento;
        tenant.IvaProductos = request.IvaProductos;
        tenant.IvaServicios = request.IvaServicios;
        var reps = string.IsNullOrWhiteSpace(request.CodigoHabilitacionReps) ? null : request.CodigoHabilitacionReps.Trim();
        RipsRules.ValidarReps(tenant.EsIps, reps);
        tenant.CodigoHabilitacionReps = reps;
        if (request.LogoId.HasValue)
        {
            await _attachments.AssignImageAsync(AttachmentEntityType.Tenant, tenant.Id, request.LogoId, tenant.LogoId, ct);
            tenant.LogoId = request.LogoId;
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    // ── Horario de atención por sede ────────────────────────────────

    public async Task<List<BranchScheduleDayDto>> GetScheduleAsync(Guid branchId, CancellationToken ct = default)
    {
        if (!await _db.Branches.AnyAsync(b => b.Id == branchId, ct))
            throw ApiException.NotFound("Sede no encontrada.");
        return await _db.BranchSchedules.AsNoTracking()
            .Where(h => h.BranchId == branchId)
            .OrderBy(h => h.DiaSemana).ThenBy(h => h.HoraApertura)
            .Select(h => new BranchScheduleDayDto { DiaSemana = h.DiaSemana, HoraApertura = h.HoraApertura, HoraCierre = h.HoraCierre })
            .ToListAsync(ct);
    }

    /// <summary>Reemplaza el horario completo de la sede. Lista vacía = sin restricción de horario.</summary>
    public async Task<List<BranchScheduleDayDto>> SetScheduleAsync(Guid branchId, List<BranchScheduleDayDto> dias, CancellationToken ct = default)
    {
        if (!await _db.Branches.AnyAsync(b => b.Id == branchId, ct))
            throw ApiException.NotFound("Sede no encontrada.");
        foreach (var dia in dias)
        {
            if (dia.DiaSemana is < 0 or > 6)
                throw ApiException.BadRequest("El día de la semana va de 0 (domingo) a 6 (sábado).");
            if (dia.HoraCierre <= dia.HoraApertura)
                throw ApiException.BadRequest($"El {NombreDia(dia.DiaSemana)} cierra antes de abrir.");
        }
        foreach (var grupo in dias.GroupBy(d => d.DiaSemana))
        {
            var franjas = grupo.OrderBy(d => d.HoraApertura).ToList();
            for (var i = 1; i < franjas.Count; i++)
                if (franjas[i].HoraApertura < franjas[i - 1].HoraCierre)
                    throw ApiException.BadRequest($"Las franjas del {NombreDia(grupo.Key)} se cruzan.");
        }

        var actuales = await _db.BranchSchedules.Where(h => h.BranchId == branchId).ToListAsync(ct);
        foreach (var h in actuales) h.IsDeleted = true;
        foreach (var dia in dias)
            _db.BranchSchedules.Add(new BranchSchedule
            {
                BranchId = branchId,
                DiaSemana = dia.DiaSemana,
                HoraApertura = dia.HoraApertura,
                HoraCierre = dia.HoraCierre
            });
        await _db.SaveChangesAsync(ct);
        return await GetScheduleAsync(branchId, ct);
    }

    // ── Políticas de agenda ─────────────────────────────────────────

    /// <summary>Lanza 409 si la cita no respeta la anticipación mínima o el horario de la sede.</summary>
    public async Task ValidateSlotAsync(Guid branchId, DateTime inicio, DateTime fin, CancellationToken ct = default)
    {
        var tenantId = _db.CurrentTenantId;
        var anticipacion = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId).Select(t => t.AnticipacionMinimaHoras).FirstOrDefaultAsync(ct);
        if (anticipacion > 0 && inicio < _clock.Now.AddHours(anticipacion))
            throw new ApiException(StatusCodes.Status409Conflict,
                $"La clínica pide agendar con al menos {anticipacion} h de anticipación.");

        var horario = await _db.BranchSchedules.AsNoTracking().Where(h => h.BranchId == branchId).ToListAsync(ct);
        if (horario.Count == 0)
            return;

        var dia = (int)inicio.DayOfWeek;
        var desde = TimeOnly.FromDateTime(inicio);
        var hasta = TimeOnly.FromDateTime(fin);
        var franjas = horario.Where(h => h.DiaSemana == dia).OrderBy(h => h.HoraApertura).ToList();
        // La cita no puede cruzar la medianoche ni salirse de una franja
        var cabe = fin.Date == inicio.Date && franjas.Any(f => desde >= f.HoraApertura && hasta <= f.HoraCierre);
        if (cabe)
            return;

        var texto = franjas.Count == 0
            ? $"La sede no atiende los {NombreDia(dia)}."
            : $"Fuera del horario de la sede: el {NombreDia(dia)} atiende {string.Join(" y ", franjas.Select(f => $"{f.HoraApertura:HH\\:mm}–{f.HoraCierre:HH\\:mm}"))}.";
        throw new ApiException(StatusCodes.Status409Conflict, texto);
    }

    /// <summary>¿Hay que bloquear el inicio de una cita sin consentimiento? Depende de la clínica y del procedimiento.</summary>
    public async Task<bool> ConsentRequiredAsync(Guid procedureId, CancellationToken ct = default)
    {
        var tenantId = _db.CurrentTenantId;
        var exige = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.ExigirConsentimiento).FirstOrDefaultAsync(ct);
        return exige && await _db.Procedures.AsNoTracking().Where(p => p.Id == procedureId).Select(p => p.RequiereConsentimiento).FirstOrDefaultAsync(ct);
    }

    private static string NombreDia(int dia) => EsCo.DateTimeFormat.GetDayName((DayOfWeek)dia);

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

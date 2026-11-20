using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.ClinicalRecords;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Corrección de una nota clínica.
///
/// La historia clínica no se altera (Resolución 1995 de 1999): una nota se puede corregir solo
/// dentro de las 24 h siguientes a crearla, y solo quien la escribió o recepción/dueño. Pasado ese
/// plazo se responde 409 y la corrección va como una nota aclaratoria nueva. La corrección no
/// toca el consumo de cabina (el inventario ya se movió): solo los textos, la evaluación, la
/// próxima sesión y las fotos nuevas.
/// </summary>
public class ClinicalNoteService
{
    private readonly IClock _clock;
    public static readonly TimeSpan VentanaEdicion = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly AttachmentService _attachments;

    public ClinicalNoteService(AppDbContext db, AttachmentService attachments, IClock clock)
    {
        _clock = clock;
        _db = db;
        _attachments = attachments;
    }

    /// <summary>¿Puede este usuario corregir esta nota ahora? <paramref name="ahora"/> = IClock.Now.</summary>
    public static bool CanEdit(DateTime ahora, DateTime fechaCreacion, Guid autorId, Guid userId, string? role) =>
        ahora - fechaCreacion <= VentanaEdicion
        && (autorId == userId || role is "SuperAdmin" or "Admin");

    public async Task UpdateAsync(Guid patientId, Guid noteId, UpdateClinicalNoteRequest request, Guid userId, string? role, CancellationToken ct = default)
    {
        var note = await _db.ClinicalNotes
            .Include(n => n.ClinicalRecord)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.ClinicalRecord.PatientId == patientId, ct)
            ?? throw ApiException.NotFound("Nota clínica no encontrada.");

        if (note.EsteticistId != userId && role is not ("SuperAdmin" or "Admin"))
            throw new ApiException(StatusCodes.Status403Forbidden, "Solo quien escribió la nota puede corregirla.");
        if (_clock.Now - note.FechaCreacion > VentanaEdicion)
            throw ApiException.Conflict("La nota tiene más de 24 horas: la historia clínica no se modifica. Agrega una nota aclaratoria.");

        note.Procedimiento = request.Procedimiento.Trim();
        note.Observaciones = request.Observaciones.Trim();
        note.ZonaTratada = Limpio(request.ZonaTratada);
        note.Parametros = Limpio(request.Parametros);
        note.IndicacionesPost = Limpio(request.IndicacionesPost);
        note.ProximaSesionSugerida = request.ProximaSesionSugerida;
        note.EvaluacionPaciente = request.EvaluacionPaciente;
        note.FechaEdicion = _clock.Now;

        // Fotos nuevas (pendientes); las existentes se quitan con DELETE /files/{id}
        await _attachments.ClaimForNoteAsync(request.AdjuntoIds, note.Id, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

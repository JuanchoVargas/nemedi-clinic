namespace NemediClinic.Domain.Entities;

/// <summary>
/// Registro de una importación desde archivo: quién, cuándo (CreatedAt), qué entidad, el nombre del
/// archivo y cuántas filas terminaron en cada estado. El archivo mismo NUNCA se guarda: son datos de
/// pacientes y se procesan en memoria. Se crea en la misma transacción que lo importado.
/// </summary>
public class ImportRun : BaseEntity
{
    /// <summary>"patients" (después "products", "procedures", "packages").</summary>
    public string Entidad { get; set; } = string.Empty;
    public string ArchivoNombre { get; set; } = string.Empty;
    public Guid UsuarioId { get; set; }
    /// <summary>Filas de datos del archivo (sin el encabezado ni las vacías).</summary>
    public int FilasTotales { get; set; }
    public int Creadas { get; set; }
    public int Actualizadas { get; set; }
    /// <summary>Duplicadas que se dejaron como estaban (modo Omitir).</summary>
    public int Omitidas { get; set; }
    /// <summary>Filas con error, que no se importaron.</summary>
    public int ConError { get; set; }
    /// <summary>Omitir / Actualizar: qué se hizo con los duplicados.</summary>
    public string ModoDuplicados { get; set; } = string.Empty;
    /// <summary>Lo que no dicen los números: "12 lotes con 340 unidades", "31 sesiones en 8 paquetes".</summary>
    public string? Detalle { get; set; }

    public User? Usuario { get; set; }
}

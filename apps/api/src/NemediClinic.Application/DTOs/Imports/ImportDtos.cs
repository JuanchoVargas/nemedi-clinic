namespace NemediClinic.Application.DTOs.Imports;

public class ImportFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Hint { get; set; }
    public string? Group { get; set; }
}

public class ImportColumnDto
{
    /// <summary>Índice 0-based de la columna en la hoja.</summary>
    public int Index { get; set; }
    /// <summary>"A", "B"…: como la ve la persona en Excel.</summary>
    public string Letter { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<string> Samples { get; set; } = [];
}

/// <summary>Paso 1 → 2: lo que trae el archivo y el mapeo propuesto.</summary>
public class ImportAnalysisDto
{
    public string ArchivoNombre { get; set; } = string.Empty;
    public string Formato { get; set; } = string.Empty;
    /// <summary>Fila de encabezados detectada, 1-based como en Excel.</summary>
    public int FilaEncabezados { get; set; }
    public int FilasDeDatos { get; set; }
    public int MaxFilas { get; set; }
    public List<ImportColumnDto> Columnas { get; set; } = [];
    public List<ImportFieldDto> Campos { get; set; } = [];
    /// <summary>Campo → índice de columna.</summary>
    public Dictionary<string, int> MapeoSugerido { get; set; } = [];
    public List<string> FormatosDeFecha { get; set; } = [];
}

public class ImportPreviewRowDto
{
    public int Fila { get; set; }
    /// <summary>Valida / Error / Duplicada.</summary>
    public string Estado { get; set; } = string.Empty;
    /// <summary>Campo → valor ya limpio, como se guardaría.</summary>
    public Dictionary<string, string> Valores { get; set; } = [];
    public List<string> Errores { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
    /// <summary>Duplicada: el registro que ya existe ("María Pérez").</summary>
    public string? Existente { get; set; }
}

/// <summary>Paso 3: el resumen de TODAS las filas y la muestra (las primeras 50, o las primeras 50 con algo que revisar).</summary>
public class ImportPreviewDto
{
    public int Total { get; set; }
    public int Validas { get; set; }
    public int ConError { get; set; }
    public int Duplicadas { get; set; }
    public int ConAvisos { get; set; }
    /// <summary>Registros (no filas) que se crearían: varias filas pueden ser un producto con varios lotes o un paquete con varias sesiones.</summary>
    public int Nuevos { get; set; }
    /// <summary>Registros que ya existen (se omiten o se actualizan).</summary>
    public int Existentes { get; set; }
    /// <summary>"8 productos con 12 lotes (340 unidades)": lo que no dicen los números.</summary>
    public string? Detalle { get; set; }
    /// <summary>Avisos sobre el archivo entero ("no trae cantidades: el stock se carga aparte").</summary>
    public List<string> AvisosGenerales { get; set; } = [];
    public List<ImportPreviewRowDto> Filas { get; set; } = [];
}

/// <summary>Paso 4: lo que pasó.</summary>
public class ImportResultDto
{
    public Guid Id { get; set; }
    public int Total { get; set; }
    public int Creadas { get; set; }
    public int Actualizadas { get; set; }
    public int Omitidas { get; set; }
    public int ConError { get; set; }
    public string? Detalle { get; set; }
}

public class ImportRunDto
{
    public Guid Id { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public string EntidadNombre { get; set; } = string.Empty;
    public string ArchivoNombre { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public int FilasTotales { get; set; }
    public int Creadas { get; set; }
    public int Actualizadas { get; set; }
    public int Omitidas { get; set; }
    public int ConError { get; set; }
    public string ModoDuplicados { get; set; } = string.Empty;
    public string? Detalle { get; set; }
}

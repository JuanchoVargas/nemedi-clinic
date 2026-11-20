namespace NemediClinic.Domain.Enums;

/// <summary>
/// EnCurso mientras corre. Abandonada: quedó EnCurso más de lo razonable porque el proceso se
/// reinició a mitad (reciclaje del app pool de IIS); la siguiente ejecución la cierra así.
/// </summary>
public enum JobRunEstado { EnCurso, Exitosa, Fallida, Abandonada }

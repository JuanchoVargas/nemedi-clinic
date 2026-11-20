namespace NemediClinic.Application.DTOs.Reports;

// ── Ventas por período ───────────────────────────────────────────────
public class SalesReportDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public int Ventas { get; set; }
    public int Anuladas { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    public decimal Pagado { get; set; }
    public decimal Saldo { get; set; }
    public List<SalesByDayDto> PorDia { get; set; } = [];
    public List<SalesByTypeDto> PorTipo { get; set; } = [];
    public List<SalesByMethodDto> PorMetodo { get; set; } = [];
    public List<SalesReportRowDto> Filas { get; set; } = [];
}

public class SalesByDayDto
{
    public DateOnly Fecha { get; set; }
    public int Ventas { get; set; }
    public decimal Total { get; set; }
}

public class SalesByTypeDto
{
    public string Tipo { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
}

public class SalesByMethodDto
{
    public string Metodo { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class SalesReportRowDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public DateTime Fecha { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string? Documento { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    public decimal Pagado { get; set; }
    public decimal Saldo { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public string Sede { get; set; } = string.Empty;
}

// ── Procedimientos realizados ────────────────────────────────────────
public class ProceduresReportDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public int Total { get; set; }
    public decimal ValorTotal { get; set; }
    public List<ProcedureReportRowDto> Filas { get; set; } = [];
}

public class ProcedureReportRowDto
{
    public Guid ProcedureId { get; set; }
    public string Procedimiento { get; set; } = string.Empty;
    /// <summary>Citas completadas del procedimiento en el período.</summary>
    public int Realizadas { get; set; }
    /// <summary>De esas, las que eran sesión de un paquete.</summary>
    public int DePaquete { get; set; }
    public int Sueltas { get; set; }
    /// <summary>Realizadas × precio base actual del procedimiento.</summary>
    public decimal Valor { get; set; }
    /// <summary>"Laura Pérez (3) · Camila Ruiz (2)".</summary>
    public string Esteticistas { get; set; } = string.Empty;
}

// ── Producción por esteticista ───────────────────────────────────────
public class ProductionReportDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public List<ProductionRowDto> Filas { get; set; } = [];
}

public class ProductionRowDto
{
    public Guid EsteticistId { get; set; }
    public string Esteticista { get; set; } = string.Empty;
    public int Completadas { get; set; }
    public int Canceladas { get; set; }
    public int NoAsistio { get; set; }
    /// <summary>Horas de cabina de las citas completadas.</summary>
    public decimal Horas { get; set; }
    public int Pacientes { get; set; }
    /// <summary>Σ del precio base de los procedimientos que realizó.</summary>
    public decimal ValorProducido { get; set; }
    public int NotasClinicas { get; set; }
}

// ── Cartera por paciente ─────────────────────────────────────────────
public class CarteraReportDto
{
    public DateOnly Corte { get; set; }
    public decimal Total { get; set; }
    public int Clientes { get; set; }
    public List<CarteraRowDto> Filas { get; set; } = [];
}

public class CarteraRowDto
{
    /// <summary>Null en ventas de mostrador.</summary>
    public Guid? PatientId { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string? Documento { get; set; }
    public string? Telefono { get; set; }
    /// <summary>Paquetes asignados sin venta (ficha o valoración).</summary>
    public decimal SaldoPaquetes { get; set; }
    /// <summary>Ventas confirmadas (incluye los paquetes vendidos en ellas).</summary>
    public decimal SaldoVentas { get; set; }
    public decimal Total { get; set; }
    public int Documentos { get; set; }
    public DateOnly DesdeCuando { get; set; }
    public int DiasMora { get; set; }
}

namespace NemediClinic.Domain.Enums;

/// <summary>Borrador no mueve nada; Confirmada descuenta inventario, crea paquetes y recibe pagos; Anulada lo revierte.</summary>
public enum SaleStatus
{
    Borrador = 0,
    Confirmada = 1,
    Anulada = 2
}

public enum SaleLineType
{
    Producto = 0,
    Procedimiento = 1,
    Paquete = 2,
    /// <summary>Cualquier cosa fuera del catálogo: descripción, precio e IVA los escribe quien vende.</summary>
    Otro = 3
}

/// <summary>
/// Estado de la factura electrónica (de una venta y de cada ElectronicInvoice). SinFacturar solo
/// aplica a la venta: un documento electrónico nace Pendiente.
/// </summary>
public enum ElectronicInvoiceStatus
{
    SinFacturar = 0,
    Pendiente = 1,
    Emitida = 2,
    Rechazada = 3,
    Anulada = 4
}

public enum ElectronicDocumentType
{
    Factura = 0,
    NotaCredito = 1
}

/// <summary>Pruebas = sandbox del proveedor (habilitación); Produccion = documentos con validez ante la DIAN.</summary>
public enum InvoicingEnvironment
{
    Pruebas = 0,
    Produccion = 1
}

public enum CashSessionStatus
{
    Abierta = 0,
    Cerrada = 1
}

/// <summary>Movimiento manual de caja. Los ingresos por pagos no son CashMovement: son Payment con CashSessionId.</summary>
public enum CashMovementType
{
    Ingreso = 0,
    Egreso = 1
}

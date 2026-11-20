using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Invoicing;

/// <summary>
/// Traduce el documento neutro de NemediClinic al JSON de Factus (/v2/bills/validate y
/// /v2/credit-notes/validate). Puro: no llama a nadie, así que se prueba sin red.
///
/// Reglas que salen del sandbox, no solo de la documentación:
///   · price es el unitario ANTES de IVA y discount_amount es el descuento de TODA la línea
///     (2 × 10.000 con 3.000 de descuento da base 17.000).
///   · Una línea al 0 % va como excluida ({ rate "0.00", is_excluded true }) si la clínica lo pide.
///   · Se pueden mezclar pagos de contado (forma 1) con el saldo a crédito (forma 2 con due_date).
///   · La nota crédito exige customer con responsibilities aunque la documentación diga lo contrario.
/// </summary>
public sealed class FactusPayloadBuilder(FactusCatalogService catalogs)
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public JsonObject Bill(InvoicingConnection conexion, ElectronicInvoiceDocument doc)
    {
        var body = new JsonObject
        {
            ["reference_code"] = doc.Referencia,
            ["document"] = "01",
            ["operation_type"] = "10",
            ["send_email"] = doc.EnviarCorreo,
        };
        if (doc.RangoNumeracionId is int rango) body["numbering_range_id"] = rango;
        AddCommon(body, conexion, doc);
        return body;
    }

    public JsonObject CreditNote(InvoicingConnection conexion, ElectronicInvoiceDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FacturaNumero))
            throw new ArgumentException("La nota crédito necesita el número de la factura que corrige.");
        var body = new JsonObject
        {
            ["reference_code"] = doc.Referencia,
            ["correction_concept_code"] = doc.ConceptoCorreccion ?? catalogs.Equivalences.PorDefecto.CorreccionAnulacion,
            ["bill_number"] = doc.FacturaNumero,
            ["send_email"] = doc.EnviarCorreo,
        };
        if (doc.RangoNumeracionId is int rango) body["numbering_range_id"] = rango;
        AddCommon(body, conexion, doc);
        return body;
    }

    private void AddCommon(JsonObject body, InvoicingConnection conexion, ElectronicInvoiceDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Observacion))
            body["observation"] = Truncate(doc.Observacion.Trim(), 250);

        var items = new JsonArray();
        foreach (var l in doc.Lineas) items.Add(Item(conexion, l));
        if (items.Count == 0) throw new ArgumentException("El documento no tiene líneas.");

        var pagos = PaymentDetails(conexion, doc);
        body["payment_details"] = pagos.Array;
        // Lo que Factus calcula (base + IVA por línea) contra lo que suman los pagos: la diferencia de
        // redondeo va en cash_rounding_amount (Factus acepta ±500).
        var calculado = doc.Lineas.Sum(l => l.Base + Math.Round(l.Base * l.IvaPorcentaje / 100m, 2, MidpointRounding.AwayFromZero));
        var diferencia = pagos.Suma - calculado;
        if (diferencia != 0 && Math.Abs(diferencia) <= 500) body["cash_rounding_amount"] = Money(diferencia);

        body["customer"] = Customer(doc.Cliente);
        body["items"] = items;
    }

    public JsonObject Customer(ElectronicInvoiceCustomer c)
    {
        var eq = catalogs.Equivalences;
        var d = eq.PorDefecto;
        var tipo = c.TipoDocumento?.Trim().ToUpperInvariant();
        var codigoDocumento = tipo is null ? null : eq.TipoDocumento.GetValueOrDefault(tipo);
        var numero = c.Documento?.Trim() ?? string.Empty;
        string? dv = null;

        if (codigoDocumento == "31")
        {
            // NIT: sin dígito de verificación ni puntos; el DV va aparte si vino con guion.
            var partes = numero.Split('-', 2);
            numero = Digits(partes[0]);
            if (partes.Length == 2 && Digits(partes[1]) is { Length: 1 } v) dv = v;
        }
        else
        {
            numero = Regex.Replace(numero, @"[\s\.]", "");
        }

        var consumidorFinal = c.ConsumidorFinal || codigoDocumento is null || numero.Length == 0;
        var customer = new JsonObject();
        if (consumidorFinal)
        {
            customer["identification_document_code"] = eq.ConsumidorFinal.TipoDocumento;
            customer["identification"] = eq.ConsumidorFinal.Documento;
            customer["names"] = string.IsNullOrWhiteSpace(c.Nombre) ? eq.ConsumidorFinal.Nombre : c.Nombre.Trim();
            customer["legal_organization_code"] = d.PersonaNatural;
        }
        else
        {
            customer["identification_document_code"] = codigoDocumento;
            customer["identification"] = numero;
            if (dv is not null) customer["dv"] = dv;
            if (c.PersonaJuridica)
            {
                customer["legal_organization_code"] = d.PersonaJuridica;
                customer["company"] = c.Nombre.Trim();
            }
            else
            {
                customer["legal_organization_code"] = d.PersonaNatural;
                customer["names"] = c.Nombre.Trim();
            }
        }
        customer["tribute_code"] = d.TributoCliente;
        customer["responsibilities"] = new JsonArray(d.Responsabilidad);
        customer["country_code"] = d.Pais;
        if (!string.IsNullOrWhiteSpace(c.Email) && EmailRegex.IsMatch(c.Email.Trim())) customer["email"] = c.Email.Trim();
        if (!string.IsNullOrWhiteSpace(c.Telefono)) customer["phone"] = Truncate(c.Telefono.Trim(), 20);
        if (!string.IsNullOrWhiteSpace(c.Direccion)) customer["address"] = Truncate(c.Direccion.Trim(), 250);
        // Solo un código DANE que Factus conozca: uno inventado rechaza la factura entera.
        if (catalogs.Exists(catalogs.Catalogs.Municipalities, c.Municipio?.Trim())) customer["municipality_code"] = c.Municipio!.Trim();
        return customer;
    }

    private JsonObject Item(InvoicingConnection conexion, ElectronicInvoiceLine l)
    {
        var d = catalogs.Equivalences.PorDefecto;
        var item = new JsonObject
        {
            ["code_reference"] = Truncate(string.IsNullOrWhiteSpace(l.Codigo) ? l.Tipo : l.Codigo.Trim(), 50),
            ["name"] = Truncate(l.Descripcion.Trim(), 250),
            ["quantity"] = Money(l.Cantidad),
            ["price"] = Money(l.PrecioUnitario),
            ["unit_measure_code"] = Pick(catalogs.Catalogs.UnitMeasures, conexion.UnidadMedida, d.UnidadMedida),
            ["standard_code"] = Pick(catalogs.Catalogs.StandardCodes, conexion.CodigoEstandar, d.CodigoEstandar),
        };
        if (l.Descuento > 0) item["discount_amount"] = Money(l.Descuento);
        else item["discount_rate"] = "0.00";

        var tax = new JsonObject { ["code"] = d.ImpuestoIva, ["rate"] = Money(l.IvaPorcentaje) };
        if (l.IvaPorcentaje == 0 && conexion.IvaCeroExcluido) tax["is_excluded"] = true;
        item["taxes"] = new JsonArray(tax);
        return item;
    }

    private (JsonArray Array, decimal Suma) PaymentDetails(InvoicingConnection conexion, ElectronicInvoiceDocument doc)
    {
        var d = catalogs.Equivalences.PorDefecto;
        var detalles = new JsonArray();
        decimal suma = 0;
        foreach (var grupo in doc.Pagos.Where(p => p.Monto > 0).GroupBy(p => MethodCode(conexion, p.Metodo)))
        {
            var monto = grupo.Sum(p => p.Monto);
            suma += monto;
            detalles.Add(new JsonObject
            {
                ["payment_form"] = d.FormaPagoContado,
                ["payment_method_code"] = grupo.Key,
                ["amount"] = Money(monto),
            });
        }
        var saldo = doc.Saldo > 0 ? doc.Saldo : 0;
        // Sin pagos ni saldo (un documento en 0): contado en efectivo por el total.
        if (detalles.Count == 0 && saldo == 0) saldo = -1;
        if (saldo > 0)
        {
            suma += saldo;
            detalles.Add(new JsonObject
            {
                ["payment_form"] = d.FormaPagoCredito,
                ["payment_method_code"] = MethodCode(conexion, "Otro"),
                ["amount"] = Money(saldo),
                ["due_date"] = (doc.Vencimiento ?? DateOnly.FromDateTime(doc.Fecha).AddDays(30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
        }
        else if (saldo < 0)
        {
            suma = doc.Total;
            detalles.Add(new JsonObject
            {
                ["payment_form"] = d.FormaPagoContado,
                ["payment_method_code"] = MethodCode(conexion, "Efectivo"),
                ["amount"] = Money(doc.Total),
            });
        }
        return (detalles, suma);
    }

    public string MethodCode(InvoicingConnection conexion, string metodo)
    {
        var table = catalogs.Catalogs.PaymentMethods;
        if (conexion.CodigosMetodoPago.TryGetValue(metodo, out var propio) && catalogs.Exists(table, propio)) return propio;
        var eq = catalogs.Equivalences.MetodoPago;
        return eq.GetValueOrDefault(metodo) ?? eq["Otro"];
    }

    private static string Pick(IReadOnlyList<CatalogEntry> table, string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && table.Any(e => e.Code == value) ? value : fallback;

    public static string Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

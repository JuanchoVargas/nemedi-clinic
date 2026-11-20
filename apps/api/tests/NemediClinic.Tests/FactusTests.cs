using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NemediClinic.Api.Services;
using NemediClinic.Application.Interfaces;
using NemediClinic.Infrastructure.Invoicing;
using NemediClinic.Infrastructure.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>Factus sin red: catálogos, armado del JSON, clasificación de respuestas, token y cifrado.</summary>
public class FactusTests
{
    private static readonly FactusCatalogService Catalogs = new();
    private static readonly FactusPayloadBuilder Builder = new(Catalogs);

    private static InvoicingConnection Conexion() => new()
    {
        TenantId = Guid.NewGuid(), ClientId = "cid", ClientSecret = "secret", Usuario = "user@example.com", Contrasena = "pw",
        UnidadMedida = "94", CodigoEstandar = "999", IvaCeroExcluido = true,
    };

    private static ElectronicInvoiceDocument Documento() => new()
    {
        Referencia = "NMC-V1-abc",
        RangoNumeracionId = 389,
        Fecha = new DateTime(2026, 9, 29, 10, 0, 0),
        Cliente = new ElectronicInvoiceCustomer { TipoDocumento = "CC", Documento = "1.000.000.001", Nombre = "Valentina Gómez", Email = "v@example.com", Municipio = "11001" },
        Lineas =
        [
            new() { Codigo = "PROT-50", Descripcion = "Protector", Cantidad = 2, PrecioUnitario = 10_000, Descuento = 3_000, Base = 17_000, IvaPorcentaje = 19, Iva = 3_230, Total = 20_230, Tipo = "Producto" },
            new() { Codigo = "869601", Descripcion = "Limpieza facial", Cantidad = 1, PrecioUnitario = 120_000, Base = 120_000, IvaPorcentaje = 0, Iva = 0, Total = 120_000, Tipo = "Procedimiento" },
        ],
        Pagos = [new() { Metodo = "Efectivo", Monto = 100_000 }, new() { Metodo = "Tarjeta", Monto = 10_000 }],
        Subtotal = 137_000, Impuestos = 3_230, Total = 140_230, Saldo = 30_230,
        Vencimiento = new DateOnly(2026, 10, 29),
    };

    [Fact]
    public void Los_catalogos_cargan_y_las_equivalencias_existen()
    {
        Assert.True(Catalogs.Catalogs.Municipalities.Count > 1000);
        Assert.StartsWith("Bogotá", Catalogs.Name(Catalogs.Catalogs.Municipalities, "11001"));
        Assert.Equal("13", Catalogs.Equivalences.TipoDocumento["CC"]);
        Assert.Equal("10", Catalogs.Equivalences.MetodoPago["Efectivo"]);
    }

    [Fact]
    public void Factura_precio_sin_iva_descuento_de_la_linea_y_excluido_al_cero()
    {
        var body = Builder.Bill(Conexion(), Documento());
        Assert.Equal("NMC-V1-abc", (string?)body["reference_code"]);
        Assert.Equal(389, (int?)body["numbering_range_id"]);

        var items = body["items"]!.AsArray();
        Assert.Equal("10000.00", (string?)items[0]!["price"]);
        Assert.Equal("2.00", (string?)items[0]!["quantity"]);
        Assert.Equal("3000.00", (string?)items[0]!["discount_amount"]);
        Assert.Null(items[0]!["discount_rate"]);
        Assert.Equal("19.00", (string?)items[0]!["taxes"]![0]!["rate"]);
        Assert.Null(items[0]!["taxes"]![0]!["is_excluded"]);
        Assert.Equal("0.00", (string?)items[1]!["discount_rate"]);
        Assert.True((bool?)items[1]!["taxes"]![0]!["is_excluded"]);
    }

    [Fact]
    public void Pagos_por_medio_mas_el_saldo_a_credito_con_vencimiento()
    {
        var pagos = Builder.Bill(Conexion(), Documento())["payment_details"]!.AsArray();
        Assert.Equal(3, pagos.Count);
        Assert.Equal(("1", "10", "100000.00"), ((string)pagos[0]!["payment_form"]!, (string)pagos[0]!["payment_method_code"]!, (string)pagos[0]!["amount"]!));
        Assert.Equal("48", (string?)pagos[1]!["payment_method_code"]);
        Assert.Equal("2", (string?)pagos[2]!["payment_form"]);
        Assert.Equal("30230.00", (string?)pagos[2]!["amount"]);
        Assert.Equal("2026-10-29", (string?)pagos[2]!["due_date"]);
    }

    [Fact]
    public void El_codigo_de_medio_de_pago_de_la_clinica_manda_si_existe_en_la_tabla()
    {
        var c = Conexion();
        c.CodigosMetodoPago["Tarjeta"] = "49";
        Assert.Equal("49", Builder.MethodCode(c, "Tarjeta"));
        c.CodigosMetodoPago["Tarjeta"] = "NO-EXISTE";
        Assert.Equal("48", Builder.MethodCode(c, "Tarjeta"));
    }

    [Fact]
    public void Cliente_cedula_sin_puntos_municipio_valido_y_responsabilidades()
    {
        var c = Builder.Customer(Documento().Cliente);
        Assert.Equal("13", (string?)c["identification_document_code"]);
        Assert.Equal("1000000001", (string?)c["identification"]);
        Assert.Equal("2", (string?)c["legal_organization_code"]);
        Assert.Equal("Valentina Gómez", (string?)c["names"]);
        Assert.Equal("11001", (string?)c["municipality_code"]);
        Assert.Equal("R-99-PN", (string?)c["responsibilities"]![0]);

        var inventado = Builder.Customer(new ElectronicInvoiceCustomer { TipoDocumento = "CC", Documento = "1", Nombre = "X", Municipio = "99999" });
        Assert.Null(inventado["municipality_code"]);
    }

    [Fact]
    public void Nit_con_digito_de_verificacion_va_separado_y_como_persona_juridica()
    {
        var c = Builder.Customer(new ElectronicInvoiceCustomer { TipoDocumento = "NIT", Documento = "900.123.456-7", Nombre = "Spa S.A.S.", PersonaJuridica = true });
        Assert.Equal("31", (string?)c["identification_document_code"]);
        Assert.Equal("900123456", (string?)c["identification"]);
        Assert.Equal("7", (string?)c["dv"]);
        Assert.Equal("1", (string?)c["legal_organization_code"]);
        Assert.Equal("Spa S.A.S.", (string?)c["company"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AS")]
    [InlineData("SI")]
    public void Sin_documento_o_sin_equivalente_es_consumidor_final(string? tipo)
    {
        var c = Builder.Customer(new ElectronicInvoiceCustomer { TipoDocumento = tipo, Documento = tipo is null ? "" : "123", Nombre = "Ana" });
        Assert.Equal("222222222222", (string?)c["identification"]);
        Assert.Equal("13", (string?)c["identification_document_code"]);
    }

    [Fact]
    public void Nota_credito_referencia_la_factura_y_su_concepto()
    {
        var doc = Documento();
        doc.FacturaNumero = "SETP990000001";
        doc.ConceptoCorreccion = "2";
        var body = Builder.CreditNote(Conexion(), doc);
        Assert.Equal("SETP990000001", (string?)body["bill_number"]);
        Assert.Equal("2", (string?)body["correction_concept_code"]);
        Assert.NotNull(body["customer"]!["responsibilities"]);

        doc.FacturaNumero = null;
        Assert.Throws<ArgumentException>(() => Builder.CreditNote(Conexion(), doc));
    }

    [Fact]
    public void Redondeo_de_caja_cuando_los_pagos_no_cuadran_por_centavos()
    {
        var doc = Documento();
        doc.Lineas = [new() { Codigo = "X", Descripcion = "X", Cantidad = 1, PrecioUnitario = 84.03m, Base = 84.03m, IvaPorcentaje = 19, Iva = 15.97m, Total = 100m }];
        doc.Pagos = [new() { Metodo = "Efectivo", Monto = 100m }];
        doc.Saldo = 0;
        var body = Builder.Bill(Conexion(), doc);
        // Factus: 84,03 + 15,97 (redondeado de 15,9657) = 100,00 → no hace falta
        Assert.Null(body["cash_rounding_amount"]);

        doc.Pagos = [new() { Metodo = "Efectivo", Monto = 100.02m }];
        Assert.Equal("0.02", (string?)Builder.Bill(Conexion(), doc)["cash_rounding_amount"]);
    }

    // ── Respuestas ──

    private static FactusInvoiceProvider.FactusResponse Resp(HttpStatusCode status, string json) =>
        new(status, JsonNode.Parse(json), json);

    [Fact]
    public void Validada_es_emitida_con_advertencias_de_la_dian()
    {
        var r = FactusInvoiceProvider.Classify(Resp(HttpStatusCode.Created,
            """{"data":{"number":"SETP1","cufe":"abc","is_validated":true,"validated_at":"29-09-2026 04:40:24 PM","errors":{"FAJ44b":"Regla: FAJ44b, Notificación: x"}}}"""));
        Assert.Equal(ElectronicResultStatus.Emitida, r.Estado);
        Assert.Equal("SETP1", r.Numero);
        Assert.Equal(new DateTime(2026, 9, 29, 16, 40, 24), r.Fecha);
        Assert.Contains("FAJ44b", r.Advertencias);
    }

    [Fact]
    public void La_nota_credito_usa_su_cude_y_no_el_de_la_factura_que_trae_adentro()
    {
        var r = FactusInvoiceProvider.Classify(Resp(HttpStatusCode.Created,
            """{"data":{"number":"CRTE1","cude":"nc","is_validated":true,"errors":["Regla: CBF02, Notificación: y"],"bill":{"number":"SETP1","cufe":"factura"}}}"""));
        Assert.Equal(("CRTE1", "nc"), (r.Numero, r.Cufe));
        Assert.Contains("CBF02", r.Advertencias);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, ElectronicResultStatus.Rechazada)]
    [InlineData(HttpStatusCode.Conflict, ElectronicResultStatus.Rechazada)]
    [InlineData(HttpStatusCode.NotFound, ElectronicResultStatus.Rechazada)]
    [InlineData(HttpStatusCode.InternalServerError, ElectronicResultStatus.Pendiente)]
    [InlineData(HttpStatusCode.TooManyRequests, ElectronicResultStatus.Pendiente)]
    [InlineData(HttpStatusCode.Forbidden, ElectronicResultStatus.Pendiente)]
    public void Errores_de_datos_rechazan_y_los_de_servicio_quedan_pendientes(HttpStatusCode status, ElectronicResultStatus esperado)
    {
        var r = FactusInvoiceProvider.Classify(Resp(status,
            """{"status":"Validation error","data":{"message":"Error de validación","errors":{"customer.identification":["El campo es obligatorio."]}}}"""));
        Assert.Equal(esperado, r.Estado);
        Assert.Contains("customer.identification", r.Mensaje);
    }

    [Fact]
    public void Registrada_sin_validar_es_rechazada_con_los_errores()
    {
        var r = FactusInvoiceProvider.Classify(Resp(HttpStatusCode.OK,
            """{"data":{"number":"SETP2","is_validated":false,"errors":{"FAD06":"Regla: FAD06, Rechazo: NIT inválido"}}}"""));
        Assert.Equal(ElectronicResultStatus.Rechazada, r.Estado);
        Assert.Contains("FAD06", r.Mensaje);
    }

    // ── Token ──

    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath} {body}");
            return responder(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Guarda lo que se escribe en el log, para comprobar que nunca lleva credenciales.</summary>
    private sealed class ListLogger : ILogger<FactusInvoiceProvider>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private static (FactusInvoiceProvider Provider, FakeHandler Handler, FakeTimeProvider Time) Provider(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder, ILogger<FactusInvoiceProvider>? logger = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero));
        var clock = new ZonedClock(time, "America/Bogota");
        var handler = new FakeHandler(responder);
        var provider = new FactusInvoiceProvider(new HttpClient(handler), new FactusTokenCache(clock), Builder, new FactusOptions(),
            logger ?? NullLogger<FactusInvoiceProvider>.Instance);
        return (provider, handler, time);
    }

    private static readonly string Company = """{"data":{"nit":"999000001","company":"Emisor"}}""";
    private static readonly string Ranges = """{"data":{"data":[{"id":389,"document":"Factura de Venta","prefix":"SETP","current":5,"is_expired":false,"is_active":1},{"id":1776,"document":"Nota Crédito","prefix":"CRTE","current":1,"is_expired":false,"is_active":1}]}}""";

    private static Func<HttpRequestMessage, string, HttpResponseMessage> Api()
    {
        var n = 0;
        return (req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/oauth/token" => Json(HttpStatusCode.OK, $$"""{"access_token":"t{{++n}}","refresh_token":"r{{n}}","expires_in":3600}"""),
            "/v2/companies" => Json(HttpStatusCode.OK, Company),
            _ => Json(HttpStatusCode.OK, Ranges),
        };
    }

    [Fact]
    public async Task El_token_se_reusa_y_se_renueva_con_refresh_antes_de_vencer()
    {
        var (provider, handler, time) = Provider(Api());
        var c = Conexion();

        var prueba = await provider.ProbarConexionAsync(c);
        Assert.True(prueba.Exitosa);
        Assert.Equal("999000001", prueba.EmisorNit);
        Assert.Equal(["Factura", "NotaCredito"], prueba.Rangos.Select(r => r.Documento));
        Assert.Single(handler.Calls, x => x.StartsWith("POST /oauth/token"));

        // 50 min después sigue vigente (vence a los 60, se renueva 5 antes)
        time.Advance(TimeSpan.FromMinutes(50));
        await provider.ProbarConexionAsync(c);
        Assert.Single(handler.Calls, x => x.StartsWith("POST /oauth/token"));

        // A los 56 min entra en el margen: se renueva con el refresh token, no con la contraseña
        time.Advance(TimeSpan.FromMinutes(6));
        await provider.ProbarConexionAsync(c);
        var tokens = handler.Calls.Where(x => x.StartsWith("POST /oauth/token")).ToList();
        Assert.Equal(2, tokens.Count);
        Assert.Contains("grant_type=refresh_token", tokens[1]);
        Assert.Contains("refresh_token=r1", tokens[1]);
    }

    [Fact]
    public async Task Un_401_descarta_el_token_y_reintenta_una_vez()
    {
        var primero = true;
        var (provider, handler, _) = Provider((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/oauth/token")
                return Json(HttpStatusCode.OK, """{"access_token":"t","refresh_token":"r","expires_in":3600}""");
            if (req.RequestUri.AbsolutePath == "/v2/companies" && primero)
            {
                primero = false;
                return Json(HttpStatusCode.Unauthorized, """{"message":"Unauthenticated."}""");
            }
            return Json(HttpStatusCode.OK, req.RequestUri.AbsolutePath == "/v2/companies" ? Company : Ranges);
        });
        var prueba = await provider.ProbarConexionAsync(Conexion());
        Assert.True(prueba.Exitosa);
        Assert.Equal(2, handler.Calls.Count(x => x.StartsWith("POST /oauth/token")));
    }

    [Fact]
    public async Task Credenciales_rechazadas_dan_una_prueba_fallida_y_una_factura_pendiente()
    {
        var (provider, _, _) = Provider((req, _) => Json(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}"""));
        var prueba = await provider.ProbarConexionAsync(Conexion());
        Assert.False(prueba.Exitosa);
        Assert.Contains("credenciales", prueba.Mensaje);

        var emision = await provider.EmitirAsync(Conexion(), Documento());
        Assert.Equal(ElectronicResultStatus.Pendiente, emision.Estado);
    }

    [Fact]
    public async Task El_token_se_pide_con_exactamente_los_cinco_campos_y_la_contrasena_intacta()
    {
        var conexion = Conexion();
        conexion.ClientId = "id-cliente-de-prueba";
        conexion.ClientSecret = "secreto+de/prueba=";
        conexion.Usuario = "usuario.sandbox@example.com";
        conexion.Contrasena = " clave%&=+de prueba ";
        HttpRequestMessage? tokenRequest = null;
        string tokenBody = "";
        var (provider, _, _) = Provider((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/oauth/token")
            {
                tokenRequest = req;
                tokenBody = body;
                return Json(HttpStatusCode.OK, """{"access_token":"t","refresh_token":"r","expires_in":3600}""");
            }
            return Json(HttpStatusCode.OK, req.RequestUri.AbsolutePath == "/v2/companies" ? Company : Ranges);
        });

        Assert.True((await provider.ProbarConexionAsync(conexion)).Exitosa);
        Assert.Equal("application/x-www-form-urlencoded", tokenRequest!.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("NemediClinic", tokenRequest.Headers.UserAgent.ToString());
        var campos = tokenBody.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        Assert.Equal(["client_id", "client_secret", "grant_type", "password", "username"], campos.Keys.Order());
        Assert.Equal("password", campos["grant_type"]);
        Assert.Equal("id-cliente-de-prueba", campos["client_id"]);
        Assert.Equal("secreto+de/prueba=", campos["client_secret"]);
        Assert.Equal("usuario.sandbox@example.com", campos["username"]);
        // Ni recortada ni con el "%" perdido: viaja codificada y llega igual
        Assert.Equal(" clave%&=+de prueba ", campos["password"]);
        Assert.Contains("%25", tokenBody);
    }

    [Theory]
    [InlineData(400, """{"error":"invalid_grant","error_description":"The user credentials were incorrect."}""", "el usuario o la contraseña")]
    [InlineData(401, """{"error":"invalid_client","error_description":"Client authentication failed"}""", "client_id o client_secret")]
    public async Task El_rechazo_del_token_dice_el_motivo_de_Factus_y_el_ambiente_sin_filtrar_credenciales(int status, string respuesta, string causa)
    {
        var logger = new ListLogger();
        var (provider, _, _) = Provider((_, _) => Json((HttpStatusCode)status, respuesta), logger);
        var conexion = Conexion();
        conexion.ClientSecret = "Secreto-Ficticio-B";
        conexion.Usuario = "pruebas.factus@example.com";
        conexion.Contrasena = "Clave%de-prueba";

        var prueba = await provider.ProbarConexionAsync(conexion);
        Assert.False(prueba.Exitosa);
        Assert.Contains(causa, prueba.Mensaje);
        Assert.Contains("pruebas (sandbox)", prueba.Mensaje);
        Assert.Contains("api-sandbox.factus.com.co", prueba.Mensaje);
        Assert.Contains($"HTTP {status}", prueba.Mensaje);
        // El log trae la respuesta completa del proveedor, nunca lo que se envió
        var log = Assert.Single(logger.Lines);
        Assert.Contains(respuesta.Split('"')[3], log);
        foreach (var secreto in new[] { conexion.ClientSecret, conexion.Contrasena, conexion.Usuario })
        {
            Assert.DoesNotContain(secreto, log);
            Assert.DoesNotContain(secreto, prueba.Mensaje);
        }
    }

    [Fact]
    public async Task En_produccion_el_mensaje_lo_dice()
    {
        var (provider, handler, _) = Provider((_, _) => Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""));
        var conexion = Conexion();
        conexion.Produccion = true;
        var prueba = await provider.ProbarConexionAsync(conexion);
        Assert.Contains("producción", prueba.Mensaje);
        Assert.Contains("api.factus.com.co", prueba.Mensaje);
        Assert.StartsWith("POST /oauth/token", handler.Calls[0]);
    }

    // ── Credenciales guardadas ──

    [Theory]
    [InlineData("cid", "secret", "user@example.com", "clave%")]
    [InlineData(" 9d2c ", "s+/=\"'\\", "usuario.sandbox@example.com", "  espacios y ñ ü % & = + \" \\ ")]
    public void Las_credenciales_guardadas_se_leen_identicas(string clientId, string clientSecret, string usuario, string contrasena)
    {
        var original = new InvoicingSettingsService.Credentials(clientId, clientSecret, usuario, contrasena);
        const string jwtSecret = "clave-jwt-ficticia-solo-para-esta-prueba-unitaria";
        var cifradas = InvoicingSettingsService.ProtectCredentials(new AesSecretProtector(jwtSecret), original);

        // Otro proceso (un reinicio del API) con el mismo secreto: la clave derivada es la misma
        var leidas = InvoicingSettingsService.UnprotectCredentials(new AesSecretProtector(jwtSecret), cifradas);
        Assert.Equal(original, leidas);
        Assert.Equal(contrasena, leidas!.Contrasena);

        // Con otra clave no salen corruptas: no salen
        Assert.Null(InvoicingSettingsService.UnprotectCredentials(new AesSecretProtector("otro-secreto-distinto-de-al-menos-32"), cifradas));
        Assert.Null(InvoicingSettingsService.UnprotectCredentials(new AesSecretProtector(jwtSecret), cifradas[..^6] + "AAAAAA"));
        Assert.Null(InvoicingSettingsService.UnprotectCredentials(new AesSecretProtector(jwtSecret), null));
    }

    [Fact]
    public async Task Sin_red_la_factura_queda_pendiente()
    {
        var (provider, _, _) = Provider((req, _) =>
            req.RequestUri!.AbsolutePath == "/oauth/token"
                ? Json(HttpStatusCode.OK, """{"access_token":"t","expires_in":3600}""")
                : throw new HttpRequestException("connection reset"));
        var r = await provider.EmitirAsync(Conexion(), Documento());
        Assert.Equal(ElectronicResultStatus.Pendiente, r.Estado);
        Assert.Contains("connection reset", r.Mensaje);
    }

    // ── Cifrado ──

    [Fact]
    public void Las_credenciales_cifradas_no_se_leen_con_otra_clave_ni_alteradas()
    {
        var a = new AesSecretProtector("una-clave-suficientemente-larga-1");
        var cifrado = a.Protect("{\"password\":\"secreto\"}");
        Assert.StartsWith("v1:", cifrado);
        Assert.DoesNotContain("secreto", cifrado);
        Assert.NotEqual(cifrado, a.Protect("{\"password\":\"secreto\"}")); // nonce aleatorio
        Assert.Equal("{\"password\":\"secreto\"}", a.Unprotect(cifrado));

        var otra = new AesSecretProtector("una-clave-suficientemente-larga-2");
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otra.Unprotect(cifrado));
        var alterado = cifrado[..^4] + (cifrado[^4] == 'A' ? "B" : "A") + cifrado[^3..];
        Assert.ThrowsAny<Exception>(() => a.Unprotect(alterado));
    }
}

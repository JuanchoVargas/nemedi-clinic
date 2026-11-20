using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Invoicing;

/// <summary>URLs de Factus (sección Factus de la configuración).</summary>
public sealed class FactusOptions
{
    public string SandboxUrl { get; set; } = "https://api-sandbox.factus.com.co";
    public string ProductionUrl { get; set; } = "https://api.factus.com.co";
}

/// <summary>
/// Factus (API v2; la v1 responde 403 "Versión de API no disponible").
///
/// Cómo clasifica cada respuesta:
///   · 2xx con is_validated → Emitida. Reenviar la misma reference_code devuelve el mismo documento
///     ya validado, por eso un reintento nunca duplica.
///   · 2xx sin validar, 400, 404, 409 y 422 → Rechazada, con los errores de Factus/DIAN como motivo.
///   · 401 → se descarta el token y se reintenta una vez; si sigue, y en 403, 408, 429, 5xx, red o
///     timeout → Pendiente (el job reintenta).
/// </summary>
public sealed class FactusInvoiceProvider(
    HttpClient http, FactusTokenCache tokens, FactusPayloadBuilder builder, FactusOptions options,
    ILogger<FactusInvoiceProvider> logger) : IElectronicInvoiceProvider
{
    public string Nombre => "Factus";

    /// <summary>Algunos WAF rechazan (403) peticiones sin User-Agent; HttpClient no manda uno por defecto.</summary>
    private const string UserAgent = "NemediClinic/1.0 (+https://clinic.example.com)";

    // ── Documentos ──────────────────────────────────────────────

    public Task<ElectronicInvoiceResult> EmitirAsync(InvoicingConnection conexion, ElectronicInvoiceDocument documento, CancellationToken ct = default) =>
        ValidateAsync(conexion, "/v2/bills/validate", builder.Bill(conexion, documento), ct);

    public Task<ElectronicInvoiceResult> NotaCreditoAsync(InvoicingConnection conexion, ElectronicInvoiceDocument documento, CancellationToken ct = default) =>
        ValidateAsync(conexion, "/v2/credit-notes/validate", builder.CreditNote(conexion, documento), ct);

    private async Task<ElectronicInvoiceResult> ValidateAsync(InvoicingConnection conexion, string path, JsonObject body, CancellationToken ct)
    {
        FactusResponse r;
        try
        {
            r = await SendAsync(conexion, HttpMethod.Post, path, body, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FactusAuthException)
        {
            if (e is TaskCanceledException && ct.IsCancellationRequested) throw;
            return Pending(e is FactusAuthException ? e.Message : $"Sin respuesta de Factus: {e.Message}");
        }
        return Classify(r);
    }

    public static ElectronicInvoiceResult Classify(FactusResponse r)
    {
        var status = (int)r.Status;
        if (status is >= 200 and < 300)
        {
            var doc = Document(r.Json);
            if (doc is null) return Pending("Factus respondió sin el documento.");
            var validado = Bool(doc["is_validated"]);
            var (errores, _) = Errors(doc["errors"]);
            if (!validado)
                return new ElectronicInvoiceResult
                {
                    Estado = ElectronicResultStatus.Rechazada,
                    Numero = Str(doc["number"]),
                    Mensaje = errores ?? Str(r.Json?["message"]) ?? "La DIAN no validó el documento.",
                };
            return new ElectronicInvoiceResult
            {
                Estado = ElectronicResultStatus.Emitida,
                Numero = Str(doc["number"]),
                Cufe = Str(doc["cufe"]) ?? Str(doc["cude"]),
                Fecha = ParseDate(Str(doc["validated_at"])),
                Advertencias = errores,
            };
        }
        if (r.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            return new ElectronicInvoiceResult { Estado = ElectronicResultStatus.Rechazada, Mensaje = ErrorMessage(r) };
        return Pending($"Factus respondió {status}: {ErrorMessage(r)}");
    }

    private static ElectronicInvoiceResult Pending(string mensaje) =>
        new() { Estado = ElectronicResultStatus.Pendiente, Mensaje = mensaje };

    public async Task<ElectronicInvoiceResult?> BuscarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string referencia, CancellationToken ct = default)
    {
        var r = await SendAsync(conexion, HttpMethod.Get, $"{Root(tipo)}?filter[reference_code]={Uri.EscapeDataString(referencia)}", null, ct);
        EnsureSuccess(r);
        var match = List(r.Json).FirstOrDefault(d => Str(d["reference_code"]) == referencia);
        var numero = match is null ? null : Str(match["number"]);
        if (numero is null) return null;
        var detalle = await SendAsync(conexion, HttpMethod.Get, $"{Root(tipo)}/{Uri.EscapeDataString(numero)}", null, ct);
        if (detalle.Status == HttpStatusCode.NotFound) return null;
        EnsureSuccess(detalle);
        return Classify(detalle);
    }

    public async Task DescartarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string referencia, CancellationToken ct = default)
    {
        // Si no existe (404) o ya fue validado (409) no hay nada que descartar.
        var r = await SendAsync(conexion, HttpMethod.Delete, $"{Root(tipo)}/destroy/reference/{Uri.EscapeDataString(referencia)}", null, ct);
        if (r.Status is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity) return;
        EnsureSuccess(r);
    }

    public async Task<ElectronicDocumentFiles> DescargarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string numero, CancellationToken ct = default)
    {
        var baseUrl = $"{Root(tipo)}/{Uri.EscapeDataString(numero)}";
        var pdf = await SendAsync(conexion, HttpMethod.Get, $"{baseUrl}/download-pdf", null, ct);
        EnsureSuccess(pdf);
        var xml = await SendAsync(conexion, HttpMethod.Get, $"{baseUrl}/download-xml", null, ct);
        EnsureSuccess(xml);
        return new ElectronicDocumentFiles
        {
            Pdf = Base64(pdf.Json?["data"]?["pdf_base_64_encoded"]),
            PdfNombre = Str(pdf.Json?["data"]?["file_name"]),
            Xml = Base64(xml.Json?["data"]?["xml_base_64_encoded"]),
            XmlNombre = Str(xml.Json?["data"]?["file_name"]),
        };
    }

    public async Task EnviarCorreoAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string numero, string correo, CancellationToken ct = default)
    {
        var r = await SendAsync(conexion, HttpMethod.Post, $"{Root(tipo)}/{Uri.EscapeDataString(numero)}/send-email",
            new JsonObject { ["email"] = correo }, ct);
        EnsureSuccess(r);
    }

    public async Task<InvoicingConnectionTest> ProbarConexionAsync(InvoicingConnection conexion, CancellationToken ct = default)
    {
        try
        {
            var company = await SendAsync(conexion, HttpMethod.Get, "/v2/companies", null, ct);
            EnsureSuccess(company);
            var ranges = await SendAsync(conexion, HttpMethod.Get, "/v2/numbering-ranges?filter[per_page]=100", null, ct);
            EnsureSuccess(ranges);

            var c = company.Json?["data"];
            var nombre = Str(c?["company"]) ?? Str(c?["graphic_representation_name"]) ?? Str(c?["names"]);
            return new InvoicingConnectionTest
            {
                Exitosa = true,
                Mensaje = "Conexión exitosa.",
                EmisorNit = Str(c?["nit"]),
                EmisorNombre = nombre,
                Rangos = List(ranges.Json).Select(Range).Where(x => x is not null).Select(x => x!).ToList(),
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FactusAuthException or FactusException)
        {
            if (e is TaskCanceledException && ct.IsCancellationRequested) throw;
            return new InvoicingConnectionTest { Exitosa = false, Mensaje = e.Message };
        }
    }

    private static InvoicingNumberingRange? Range(JsonNode d)
    {
        var id = Long(d["id"]);
        if (id is null) return null;
        var documento = Str(d["document"]) ?? string.Empty;
        var tipo = documento.Contains("Factura", StringComparison.OrdinalIgnoreCase) ? "Factura"
            : documento.Contains("dito", StringComparison.OrdinalIgnoreCase) ? "NotaCredito" : documento;
        var activo = d["is_active"] is null || Bool(d["is_active"]);
        return new InvoicingNumberingRange
        {
            Id = (int)id.Value,
            Documento = tipo,
            Prefijo = Str(d["prefix"]) ?? string.Empty,
            Desde = Long(d["from"]),
            Hasta = Long(d["to"]),
            Actual = Long(d["current"]) ?? 0,
            Resolucion = Str(d["resolution_number"]),
            Vigente = activo && !Bool(d["is_expired"]),
        };
    }

    private static string Root(ElectronicDocumentKind tipo) =>
        tipo == ElectronicDocumentKind.Factura ? "/v2/bills" : "/v2/credit-notes";

    // ── HTTP y token ────────────────────────────────────────────

    public sealed record FactusResponse(HttpStatusCode Status, JsonNode? Json, string Text);

    private string BaseUrl(InvoicingConnection c) => (c.Produccion ? options.ProductionUrl : options.SandboxUrl).TrimEnd('/');

    private async Task<FactusResponse> SendAsync(InvoicingConnection conexion, HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        var key = FactusTokenCache.Key(conexion);
        for (var intento = 0; ; intento++)
        {
            var token = await TokenAsync(conexion, key, ct);
            using var req = new HttpRequestMessage(method, BaseUrl(conexion) + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
                req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && intento == 0)
            {
                tokens.Invalidate(key);
                continue;
            }
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                throw new FactusAuthException("Factus rechazó el token. Revisa las credenciales de facturación.");
            return new FactusResponse(res.StatusCode, ParseJson(text), text);
        }
    }

    private async Task<string> TokenAsync(InvoicingConnection conexion, string key, CancellationToken ct)
    {
        if (tokens.Valid(key) is { } vigente) return vigente;
        var gate = tokens.Lock(key);
        await gate.WaitAsync(ct);
        try
        {
            if (tokens.Valid(key) is { } otro) return otro;
            // Primero renovar con el refresh token; si ya no sirve, credenciales completas.
            if (tokens.RefreshToken(key) is { } refresh)
            {
                var renovado = await RequestTokenAsync(conexion, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh,
                    ["client_id"] = conexion.ClientId,
                    ["client_secret"] = conexion.ClientSecret,
                }, ct);
                if (renovado.Json is not null) return Store(key, renovado.Json);
            }
            var nuevo = await RequestTokenAsync(conexion, new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = conexion.ClientId,
                ["client_secret"] = conexion.ClientSecret,
                ["username"] = conexion.Usuario,
                ["password"] = conexion.Contrasena,
            }, ct);
            if (nuevo.Json is null) throw new FactusAuthException(nuevo.Rechazo ?? "Factus no aceptó las credenciales.");
            return Store(key, nuevo.Json);
        }
        finally
        {
            gate.Release();
        }
    }

    private string Store(string key, JsonNode json)
    {
        var access = Str(json["access_token"])!;
        tokens.Store(key, access, Str(json["refresh_token"]), (int)(Long(json["expires_in"]) ?? 3600));
        return access;
    }

    private sealed record TokenResponse(JsonNode? Json, string? Rechazo);

    /// <summary>
    /// POST /oauth/token (application/x-www-form-urlencoded, el estándar de OAuth 2; el sandbox también
    /// acepta JSON). FormUrlEncodedContent codifica cada valor: una contraseña con "%" viaja como "%25"
    /// y llega intacta. Si Factus rechaza la concesión (400/401/403) devuelve el motivo que dio, con el
    /// ambiente, y lo registra en el log con el cuerpo de la respuesta sin las credenciales; si no
    /// respondió, lanza.
    /// </summary>
    private async Task<TokenResponse> RequestTokenAsync(InvoicingConnection conexion, Dictionary<string, string> form, CancellationToken ct)
    {
        var url = BaseUrl(conexion) + "/oauth/token";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        var json = ParseJson(text);
        if (res.IsSuccessStatusCode && Str(json?["access_token"]) is not null) return new TokenResponse(json, null);

        var status = (int)res.StatusCode;
        var ambiente = conexion.Produccion ? "producción" : "pruebas (sandbox)";
        var grant = form.GetValueOrDefault("grant_type");
        logger.LogWarning("Factus rechazó el token ({Grant}) en {Url}: HTTP {Status} {Body}",
            grant, url, status, Redact(text, form.Values));
        if (status is not (400 or 401 or 403) && !res.IsSuccessStatusCode)
            throw new HttpRequestException($"Factus ({ambiente}) respondió {status} al autenticar.");

        var error = Str(json?["error"]);
        var detalle = Str(json?["error_description"]) ?? Str(json?["message"]) ?? Str(json?["hint"]);
        var causa = error switch
        {
            "invalid_client" => "client_id o client_secret no son de este ambiente o no son válidos",
            "invalid_grant" => "el usuario o la contraseña no son válidos",
            "invalid_request" => "falta un dato en la petición",
            _ => null,
        };
        var motivo = string.Join(" · ", new[] { error, detalle }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new TokenResponse(null,
            $"Factus ({ambiente}, {new Uri(url).Host}) no aceptó las credenciales: " +
            (causa ?? "revisa client_id, client_secret, usuario y contraseña") +
            $". Respuesta: HTTP {status}{(motivo.Length > 0 ? $" {motivo}" : "")}.");
    }

    /// <summary>Quita de un texto (para el log) cualquier valor enviado: credenciales o refresh token.</summary>
    private static string Redact(string text, IEnumerable<string> secrets)
    {
        var result = text.Length > 2000 ? text[..2000] : text;
        foreach (var s in secrets.Where(s => s.Length >= 4).OrderByDescending(s => s.Length))
            result = result.Replace(s, "***", StringComparison.Ordinal);
        return result;
    }
    private static void EnsureSuccess(FactusResponse r)
    {
        if ((int)r.Status is >= 200 and < 300) return;
        throw new FactusException((int)r.Status, ErrorMessage(r));
    }

    // ── Lectura tolerante del JSON ──────────────────────────────

    private static JsonNode? ParseJson(string text)
    {
        try { return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// El documento viene en data, o en data.bill / data.credit_note según el endpoint. Ojo: la nota
    /// crédito trae en data.bill la factura que corrige, así que manda lo que esté en data mismo.
    /// </summary>
    private static JsonNode? Document(JsonNode? json)
    {
        var data = json?["data"];
        if (data is not JsonObject o) return null;
        if (o.ContainsKey("is_validated") || o.ContainsKey("number")) return o;
        if (o["bill"] is JsonObject bill) return bill;
        if (o["credit_note"] is JsonObject nc) return nc;
        return o;
    }

    /// <summary>Listados: data.data[] (paginado) o data[].</summary>
    private static IEnumerable<JsonNode> List(JsonNode? json)
    {
        var data = json?["data"];
        var arr = data as JsonArray ?? data?["data"] as JsonArray;
        return arr?.Where(n => n is not null).Select(n => n!) ?? [];
    }

    /// <summary>errors viene como objeto {regla: texto} (facturas) o como arreglo de textos (notas).</summary>
    private static (string? Texto, int Cantidad) Errors(JsonNode? errors)
    {
        var lines = new List<string>();
        switch (errors)
        {
            case JsonObject o:
                foreach (var (k, v) in o) lines.AddRange(Flatten(v).Select(t => t.StartsWith("Regla", StringComparison.Ordinal) ? t : $"{k}: {t}"));
                break;
            case JsonArray a:
                lines.AddRange(a.SelectMany(Flatten));
                break;
        }
        return (lines.Count == 0 ? null : string.Join("\n", lines.Distinct()), lines.Count);
    }

    private static IEnumerable<string> Flatten(JsonNode? n) => n switch
    {
        JsonArray a => a.SelectMany(Flatten),
        JsonObject o => o.SelectMany(p => Flatten(p.Value)),
        JsonValue v => Str(v) is { Length: > 0 } s ? [s] : [],
        _ => [],
    };

    public static string ErrorMessage(FactusResponse r)
    {
        var data = r.Json?["data"];
        var (errores, _) = Errors(data?["errors"] ?? r.Json?["errors"]);
        var mensaje = Str(data?["message"]) ?? Str(r.Json?["message"]);
        var texto = string.Join("\n", new[] { mensaje, errores }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (texto.Length > 0) return texto;
        return r.Text.Length > 0 && r.Text.Length < 300 && r.Json is not null ? r.Text : $"Error {(int)r.Status} de Factus.";
    }

    private static string? Str(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>() is { Length: > 0 } s ? s : null,
            JsonValueKind.Number => v.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool Bool(JsonNode? n) => Str(n) is { } s && (s == "true" || s == "1");

    private static long? Long(JsonNode? n) =>
        Str(n) is { } s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (long)d : null;

    private static byte[]? Base64(JsonNode? n) => Str(n) is { } s ? Convert.FromBase64String(s) : null;

    /// <summary>validated_at: "29-09-2026 04:40:24 PM", hora de Colombia.</summary>
    public static DateTime? ParseDate(string? s)
    {
        if (s is null) return null;
        string[] formats = ["dd-MM-yyyy hh:mm:ss tt", "dd-MM-yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];
        return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;
    }
}

/// <summary>Credenciales que Factus no acepta.</summary>
public sealed class FactusAuthException(string message) : Exception(message);

/// <summary>Respuesta de error de Factus en una consulta (descarga, correo, búsqueda).</summary>
public sealed class FactusException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

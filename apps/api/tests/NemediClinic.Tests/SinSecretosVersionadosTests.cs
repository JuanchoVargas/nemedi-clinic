using System.Text.RegularExpressions;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// Ningún secreto se versiona. Revisa los archivos de texto que git versiona o versionaría (los
/// seguidos y los nuevos que .gitignore no excluye; sin git, recorre la carpeta sin bin, obj,
/// node_modules ni salidas de build) y falla si encuentra una llave, una contraseña literal o un
/// correo personal. Los hallazgos se reportan como archivo:línea y tipo, nunca con el valor.
/// Un .env local ignorado por git no cuenta: no se versiona.
///
/// Las contraseñas de desarrollo y de las pruebas e2e se pasan por user-secrets o variables de
/// entorno (ver README); las pruebas que crean usuarios generan su contraseña al vuelo.
/// </summary>
public class SinSecretosVersionadosTests
{
    /// <summary>Archivos de variables o de configuración en texto plano: .env*, YAML, INI, TOML, properties.</summary>
    private static bool EsArchivoDeVariables(string archivo)
    {
        var nombre = Path.GetFileName(archivo);
        if (nombre.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        return nombre.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(nombre).ToLowerInvariant() is ".env" or ".yml" or ".yaml" or ".ini" or ".toml" or ".properties";
    }

    // Patrones que solo se aplican a cierto tipo de archivo.
    private static readonly (string Tipo, Regex Patron, Func<string, bool> AplicaA)[] PatronesPorArchivo =
    [
        // CLAVE=valor o clave: valor sin comillas. Una variable de secreto va vacía, con marcador CHANGE_ME…
        // o tomada de otra variable (${…}); cualquier otro valor es un hallazgo.
        // También la forma de lista de docker-compose (- CLAVE=valor) y los nombres que terminan en _PASS o _KEY.
        ("secreto en archivo de variables", new Regex(
            @"^\s*(-\s+)?(export\s+)?([\w.\-]*(PASSWORD|PASSWD|PWD|SECRET|API_?KEY|PRIVATE_?KEY|ACCESS_?KEY|TOKEN|CONTRASE(N|Ñ)A)[\w.\-]*|[\w.\-]*_(PASS|KEY))\s*[:=][ \t]*(?![ \t]*(#.*)?$)(?![""']{2}\s*$)(?![""']?CHANGE_ME)(?![""']?\$\{)\S",
            RegexOptions.IgnoreCase), EsArchivoDeVariables),
    ];

    private static readonly (string Tipo, Regex Patron)[] Patrones =
    [
        // Los patrones se arman por partes para que los greps de revisión no coincidan con este archivo.
        ("llave privada", new Regex("-----" + @"BEGIN [A-Z ]*PRIVATE KEY-----")),
        ("API key de Google", new Regex("AI" + @"za[0-9A-Za-z_\-]{35}")),
        ("ID de Universal Analytics", new Regex(@"\bU" + @"A-\d{4,}-\d+\b")),
        ("ID de medición de Analytics", new Regex(@"\bG-[A-Z0-9]{8,}\b")),
        ("llave de pagos en vivo", new Regex(@"\b[sr]k_" + @"live_[0-9A-Za-z]{10,}")),
        ("correo personal", new Regex(@"[A-Za-z0-9._%+\-]+@(gmail|hotmail|outlook|yahoo|live)\.[a-z]{2,}", RegexOptions.IgnoreCase)),
        // Contraseña dentro de una cadena de conexión. Se aceptan marcadores (CHANGE_ME…, <…>, ${…}, $(…)).
        ("contraseña en cadena de conexión", new Regex(@";\s*(Password|Pwd)\s*=\s*(?![<$])(?!CHANGE_ME)[^;""'\s<]+", RegexOptions.IgnoreCase)),
        // "AdminPassword": "valor", "ClientSecret": "valor"… en JSON. Vacío o marcador CHANGE_ME_ se acepta.
        ("secreto en configuración JSON", new Regex(@"""\w*(Password|Secret|ApiKey|SigningKey|EncryptionKey)""\s*:\s*""(?!CHANGE_ME_)(?!<)[^""]+""", RegexOptions.IgnoreCase)),
        // password: "valor", ADMIN_PASSWORD = "valor", … ?? "valor" en código. Un texto con espacios es un
        // mensaje y uno sin dígitos es un nombre (de claim, de ruta), no una clave.
        ("contraseña literal en código", new Regex(@"\b\w*(password|contrase(n|ñ)a|_pass\b)\w*\s*(:|=|\?\?)\s*(process\.env\.\w+\s*\?\?\s*)?[""'](?=[^""'\s]*\d)[^""'\s]{6,}[""']", RegexOptions.IgnoreCase)),
        // ${SECRETO:-valor}: valor por defecto no vacío para una variable de secreto (compose, shell).
        ("valor por defecto de un secreto", new Regex(@"\$\{\w*(PASSWORD|PASSWD|PWD|SECRET|API_?KEY|PRIVATE_?KEY|ACCESS_?KEY|TOKEN|CONTRASE(N|Ñ)A|_PASS|_KEY)\w*:?-[^}\s]+\}", RegexOptions.IgnoreCase)),
        // _config["…:ApiKey"] ?? "x", GetEnvironmentVariable("…PASSWORD") ?? "x", import.meta.env.…KEY ?? "x":
        // un respaldo literal para un secreto, sea cual sea su forma.
        ("respaldo literal de un secreto", new Regex(
            @"(\w*config\w*\[[^\]]*(password|secret|key|token|contrase(n|ñ)a)[^\]]*\]|GetEnvironmentVariable\([^)]*(password|secret|key|token|pass)[^)]*\)|import\.meta\.env\.\w*(password|secret|key|token)\w*)\s*\?\?\s*[""'][^""']+[""']",
            RegexOptions.IgnoreCase)),
        // ClientSecret = "valor", apiKey: "valor", JOBS_KEY = … ?? "valor" en C#/TS/JS. Mismo criterio:
        // 8+ caracteres sin espacios y con algún dígito (un nombre o un mensaje no es una llave). El
        // marcador CHANGE_ME… (la clave de desarrollo que Production rechaza) se acepta.
        // También con nombre en español (llave, clave).
        ("secreto literal en código", new Regex(@"\b\w*(secret|api_?key|jobs_?key|signing_?key|encryption_?key|access_?key|private_?key|token|llave|clave)\w*\s*(:|=|\?\?)\s*(process\.env\.\w+\s*\?\?\s*)?[""'](?!CHANGE_ME)(?=[^""'\s]*\d)[^""'\s]{8,}[""']", RegexOptions.IgnoreCase)),
        // Configuración armada en código (AddInMemoryCollection, diccionarios): ["Jwt:Secret"] = "valor" o
        // { "Factus:ClientSecret", "valor" }. Mismo criterio que el anterior.
        ("secreto en configuración en código", new Regex(@"[\[{]\s*[""'][\w:.\-]*(password|secret|api_?key|signing_?key|encryption_?key|access_?key|private_?key|token|contrase(n|ñ)a|llave|clave)[""']\s*(\]\s*=|,)\s*[""'](?!CHANGE_ME)(?=[^""'\s]*\d)[^""'\s]{8,}[""']", RegexOptions.IgnoreCase)),
    ];

    private static readonly HashSet<string> CarpetasExcluidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "dist", ".artifacts", "test-results", "playwright-report", ".vs", ".idea",
    };

    private static readonly HashSet<string> ExtensionesBinarias = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".pdf", ".xlsx", ".woff", ".woff2", ".ttf", ".dll", ".exe",
    };

    [Fact]
    public void El_repositorio_no_versiona_secretos()
    {
        var raiz = RaizDelRepositorio();
        var hallazgos = new List<string>();

        foreach (var archivo in ArchivosDeTexto(raiz))
        {
            var lineas = File.ReadAllLines(archivo);
            var aplicables = Patrones
                .Concat(PatronesPorArchivo.Where(p => p.AplicaA(archivo)).Select(p => (p.Tipo, p.Patron)))
                .ToList();
            for (var i = 0; i < lineas.Length; i++)
            {
                foreach (var (tipo, patron) in aplicables)
                {
                    if (patron.IsMatch(lineas[i]))
                        hallazgos.Add($"{Path.GetRelativePath(raiz, archivo).Replace('\\', '/')}:{i + 1} ({tipo})");
                }
            }
        }

        Assert.True(hallazgos.Count == 0,
            "Hay secretos versionados (se muestra solo archivo:línea y tipo):\n" + string.Join("\n", hallazgos));
    }

    /// <summary>Cada patrón detecta una muestra, armada en tiempo de ejecución para no versionarla.</summary>
    [Theory]
    [MemberData(nameof(Muestras))]
    public void Cada_patron_detecta_su_muestra(string tipo, string muestra)
    {
        var patron = Patrones.FirstOrDefault(p => p.Tipo == tipo).Patron
                     ?? PatronesPorArchivo.FirstOrDefault(p => p.Tipo == tipo).Patron;
        Assert.True(patron is not null, $"No hay patrón para «{tipo}»");
        Assert.Matches(patron, muestra);
    }

    [Theory]
    [InlineData("deploy/.env.example", true)]
    [InlineData(".env", true)]
    [InlineData("apps/web/.env.local", true)]
    [InlineData("deploy/docker-compose.yml", true)]
    [InlineData("docs/openapi/auth.yaml", true)]
    [InlineData("pnpm-lock.yaml", false)]
    [InlineData("apps/api/Program.cs", false)]
    public void El_patron_de_variables_se_aplica_a_env_y_yaml(string archivo, bool aplica)
    {
        Assert.Equal(aplica, EsArchivoDeVariables(archivo));
    }

    [Theory]
    [InlineData("Jwt__Secret=")]
    [InlineData("Smtp__Password=   # opcional")]
    [InlineData("MSSQL_SA_PASSWORD=CHANGE_ME_contraseña_de_sa")]
    [InlineData("MSSQL_SA_PASSWORD=\"\"")]
    [InlineData("      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD:?Define MSSQL_SA_PASSWORD}")]
    [InlineData("      Jwt__Secret: ${Jwt__Secret:?Define Jwt__Secret en deploy/.env}")]
    [InlineData("components:")]
    [InlineData("      securitySchemes:")]
    public void Las_variables_vacias_o_con_marcador_no_son_hallazgos(string linea)
    {
        var patron = PatronesPorArchivo.Single(p => p.Tipo == "secreto en archivo de variables").Patron;
        Assert.DoesNotMatch(patron, linea);
    }

    public static TheoryData<string, string> Muestras()
    {
        var x = new string('x', 35);
        return new TheoryData<string, string>
        {
            { "llave privada", "-----" + "BEGIN " + "RSA PRIVATE KEY-----" },
            { "API key de Google", "AI" + "za" + x },
            { "ID de Universal Analytics", "U" + "A-1234567-1" },
            { "ID de medición de Analytics", "G" + "-ABCD1234EF" },
            { "llave de pagos en vivo", "sk" + "_live_" + "abcdef123456" },
            { "correo personal", "alguien" + "@" + "gmail.com" },
            { "contraseña en cadena de conexión", "Server=db;User " + "Id=sa;" + "Password" + "=Valor123" },
            { "secreto en configuración JSON", "\"Admin" + "Password\": \"Valor123!\"" },
            { "contraseña literal en código", "const ADMIN_" + "PASSWORD = process.env.X ?? \"Valor123!\";" },
            { "secreto en archivo de variables", "MSSQL_SA_PASS" + "WORD=" + "Valor123" },
            { "secreto en archivo de variables", "  client_" + "secret: abc123def" },
            { "secreto en archivo de variables", "export API_" + "KEY=abc123def" },
            { "secreto literal en código", "ClientSe" + "cret = \"abc123def456\";" },
            { "secreto literal en código", "const JOBS_" + "KEY = process.env.X ?? \"dev-key-123456\";" },
            { "secreto literal en código", "apiK" + "ey: 'abcd1234efgh'," },
            { "secreto en archivo de variables", "      - SMTP_PASS" + "WORD=" + "Valor123" },
            { "secreto en archivo de variables", "DB_PA" + "SS=" + "Valor123" },
            { "secreto en archivo de variables", "MAPS_K" + "EY=" + "abcdef" },
            { "secreto en archivo de variables", "FACTUS_CONTRA" + "SENA=" + "Valor123" },
            { "valor por defecto de un secreto", "Jwt__Sec" + "ret: ${Jwt__Secret:-" + "valor-por-defecto}" },
            { "valor por defecto de un secreto", "-P \"${MSSQL_SA_PASS" + "WORD:-" + "Valor123}\"" },
            { "respaldo literal de un secreto", "var k = _config[\"Jobs:Api" + "Key\"] ?? \"respaldo\";" },
            { "respaldo literal de un secreto", "var s = builder.Configuration[\"Jwt:Sec" + "ret\"] ?? \"respaldo\";" },
            { "respaldo literal de un secreto", "Environment.GetEnvironmentVariable(\"DB_PASS" + "WORD\") ?? \"respaldo\"" },
            { "respaldo literal de un secreto", "const k = import.meta.env.VITE_MAPS_K" + "EY ?? \"respaldo\";" },
            { "contraseña literal en código", "conexion.Contra" + "sena = \"Valor123!\";" },
            { "secreto en configuración en código", "new Dictionary<string, string?> { [\"Jwt:Sec" + "ret\"] = \"abc123def456\" }" },
            { "secreto en configuración en código", "{ \"Factus:ClientSec" + "ret\", \"abc123def456\" }," },
            { "secreto en configuración en código", "config[\"Jobs:Api" + "Key\"] = 'abc123def456';" },
            { "secreto literal en código", "private const string Lla" + "ve = \"abc123def456\";" },
            { "secreto literal en código", "const cla" + "veApi = \"abc123def456\";" },
        };
    }

    [Theory]
    [InlineData("Server=mssql;User " + "Id=sa;Pass" + "word=<contraseña-de-sa>")]
    [InlineData("\"Secret\": \"CHANGE_ME_IN_PRODUCTION_MIN_32_CHARS!!\"")]
    [InlineData("\"AdminPassword\": \"\"")]
    [InlineData("password: \"\",")]
    [InlineData("password: z.string().min(1, \"La contraseña es obligatoria\"),")]
    [InlineData("autoComplete={type === \"password\" ? \"new-password\" : \"off\"}")]
    [InlineData("const password = tempPassword();")]
    [InlineData("public const string PasswordChangeClaim = \"must_change_password\";")]
    [InlineData("const string DevJwtSecret = \"CHANGE_ME_IN_PRODUCTION_MIN_32_CHARS!!\";")]
    [InlineData("      Smtp__Password: ${Smtp__Password:-}")]
    [InlineData("      Smtp__Port: ${Smtp__Port:-587}")]
    [InlineData("var expected = _config[\"Jobs:ApiKey\"] ?? string.Empty;")]
    [InlineData("var secret = configuration[\"Files:SigningKey\"] ?? configuration[\"Jwt:Secret\"];")]
    [InlineData("conexion.Contrasena = \" clave%&=+de prueba \";")]
    [InlineData("Server=mssql;User " + "Id=sa;Pass" + "word=CHANGE_ME_CLAVE_SA;TrustServerCertificate=True")]
    [InlineData("{ [\"Jwt:Secret\"] = \"\" }")]
    [InlineData("{ [\"Jwt:Secret\"] = \"CHANGE_ME_IN_PRODUCTION_MIN_32_CHARS!!\" }")]
    [InlineData("{ [\"Jwt:Secret\"] = \"llave-de-pruebas-de-al-menos-treinta-y-dos-caracteres\" }")]
    [InlineData("var secret = configuration[\"Jwt:Secret\"];")]
    [InlineData("{ \"Smtp:Password\", null },")]
    [InlineData("const claveTemporal = tempPassword();")]
    [InlineData("var mensaje = \"Clave temporal 2 de 3\";")]
    public void Los_marcadores_y_el_codigo_normal_no_son_hallazgos(string linea)
    {
        Assert.DoesNotContain(Patrones, p => p.Patron.IsMatch(linea));
    }

    private static IEnumerable<string> ArchivosDeTexto(string raiz) =>
        (ArchivosSegunGit(raiz) ?? RecorrerCarpeta(raiz))
            // Este archivo describe los patrones: se excluye a sí mismo (sus muestras se arman en tiempo de ejecución).
            .Where(archivo => Path.GetFileName(archivo) != nameof(SinSecretosVersionadosTests) + ".cs")
            .Where(archivo => !ExtensionesBinarias.Contains(Path.GetExtension(archivo)))
            .Where(File.Exists);

    /// <summary>git ls-files: seguidos + nuevos no ignorados. Null si no hay git o la carpeta no es un repositorio.</summary>
    private static List<string>? ArchivosSegunGit(string raiz)
    {
        try
        {
            var inicio = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = raiz,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var arg in new[] { "-c", "core.quotepath=off", "ls-files", "-z", "--cached", "--others", "--exclude-standard" })
                inicio.ArgumentList.Add(arg);
            using var git = System.Diagnostics.Process.Start(inicio);
            if (git is null) return null;
            var salida = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0) return null;
            return salida.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(ruta => Path.Combine(raiz, ruta.Replace('/', Path.DirectorySeparatorChar)))
                .ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // git no está instalado
        }
    }

    private static IEnumerable<string> RecorrerCarpeta(string raiz)
    {
        var pendientes = new Stack<string>();
        pendientes.Push(raiz);
        while (pendientes.Count > 0)
        {
            var carpeta = pendientes.Pop();
            foreach (var sub in Directory.EnumerateDirectories(carpeta))
            {
                if (!CarpetasExcluidas.Contains(Path.GetFileName(sub)))
                    pendientes.Push(sub);
            }
            foreach (var archivo in Directory.EnumerateFiles(carpeta))
                yield return archivo;
        }
    }

    /// <summary>La carpeta que tiene global.json y apps/: la raíz del repositorio.</summary>
    private static string RaizDelRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(Path.Combine(dir.FullName, "apps"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (global.json + apps/).");
    }
}

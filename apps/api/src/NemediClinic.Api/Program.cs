using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using NemediClinic.Api.Services.Import;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NemediClinic.Api.Json;
using NemediClinic.Api.Middleware;
using NemediClinic.Api.Providers;
using NemediClinic.Api.Services;
using NemediClinic.Api.Services.Jobs;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Infrastructure.Invoicing;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Infrastructure.Services;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ── Configuración privada fuera del sitio (ExternalConfig__Path) ─
// En IIS el web.config viaja en la rama de release (Git) y no puede llevar secretos: el
// web.config solo apunta a un JSON en la carpeta privada de la suscripción (cadena de conexión,
// Jwt, claves) que se sube a mano una vez. Se agrega de último: gana sobre appsettings y variables.
var externalConfig = builder.Configuration["ExternalConfig:Path"];
if (!string.IsNullOrWhiteSpace(externalConfig))
{
    if (!File.Exists(externalConfig))
        throw new InvalidOperationException($"ExternalConfig__Path apunta a {externalConfig}, que no existe (o el app pool no lo puede leer).");
    builder.Configuration.AddJsonFile(externalConfig, optional: false, reloadOnChange: false);
}

// ── Reloj: la hora de la clínica, nunca la del servidor ─────────
// Clock__TimeZone (Bogotá por defecto; id IANA o de Windows). Producción corre en un IIS en US
// Eastern con horario de verano: nada del sistema lee la hora ni la zona local del servidor
// (el analizador de BannedSymbols.txt lo impide en el build).
// Solo en Development, Clock__SimulateServerTimeZone hace que el proceso crea estar en otra zona,
// para probar que eso no cambia nada (tests/e2e/clock.spec.ts).
var simulatedServerZone = builder.Configuration["Clock:SimulateServerTimeZone"];
if (!string.IsNullOrWhiteSpace(simulatedServerZone))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Clock__SimulateServerTimeZone solo se permite en Development.");
    ServerTimeZoneSimulator.Apply(ZonedClock.ResolveTimeZone(simulatedServerZone));
}
var clock = new ZonedClock(TimeProvider.System, builder.Configuration["Clock:TimeZone"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClock>(clock);

// ── Log a archivo rotativo (Logging__File__Path) ────────────────
// En IIS no hay consola que mirar. Un archivo por día (api-AAAAMMDD.log; parte en otro al pasar
// 50 MB), se conservan Logging__File__RetainedFiles (31 por defecto). Nivel del archivo:
// Logging__File__MinimumLevel (Information por defecto); los filtros por categoría de
// Logging:LogLevel siguen aplicando. shared: el proceso viejo y el nuevo de un reciclaje de IIS
// pueden escribir a la vez.
var logDirectory = builder.Configuration["Logging:File:Path"];
if (!string.IsNullOrWhiteSpace(logDirectory))
{
    logDirectory = Path.GetFullPath(logDirectory);
    Directory.CreateDirectory(logDirectory);
    var fileLevel = Enum.TryParse<LogEventLevel>(builder.Configuration["Logging:File:MinimumLevel"], true, out var parsedLevel)
        ? parsedLevel : LogEventLevel.Information;
    var fileLogger = new LoggerConfiguration()
        .MinimumLevel.Is(fileLevel)
        .WriteTo.File(
            Path.Combine(logDirectory, "api-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: builder.Configuration.GetValue("Logging:File:RetainedFiles", 31),
            fileSizeLimitBytes: 50 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
    // AddProvider y no AddSerilog: AddSerilog agrega una regla que deja pasar todo a Serilog y se
    // salta Logging:LogLevel (el archivo se llenaría con cada SQL de EF aunque se pida Warning).
    builder.Logging.AddProvider(new Serilog.Extensions.Logging.SerilogLoggerProvider(fileLogger, dispose: true));
}

// ── JWT: Jwt__Secret obligatorio en Production ──────────────────
// La clave de appsettings.json es solo para desarrollo. En Production debe venir por
// variable de entorno (Jwt__Secret), tener al menos 32 caracteres y no ser la de dev.
const string DevJwtSecret = "CHANGE_ME_IN_PRODUCTION_MIN_32_CHARS!!";
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (builder.Environment.IsProduction())
{
    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret == DevJwtSecret || jwtSecret.Length < 32)
        throw new InvalidOperationException(
            "Jwt__Secret no está configurado para Production. Define la variable de entorno Jwt__Secret " +
            "con una clave aleatoria de al menos 32 caracteres (openssl rand -base64 48). " +
            "No se acepta la clave de desarrollo de appsettings.json.");
}
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("Jwt:Secret no está configurado.");

// ── Producción: nada privado dentro de la carpeta del sitio ─────
// Los archivos subidos (fotos clínicas, comprobantes, consentimientos) se sirven solo por el
// endpoint firmado y los logs no se sirven: si su carpeta quedara donde el servidor web la entrega,
// un error de configuración podría exponerlos. El API no arranca en ese caso:
//   · nunca dentro de su propia carpeta (la aplicación /api);
//   · en IIS (APP_POOL_ID), el API es la aplicación /api de la subcarpeta api\ del sitio: dentro del
//     sitio (la carpeta padre) solo valen bajo App_Data\, que el web.config del sitio responde con 404
//     (regla PrivateData + hiddenSegments). Ahí viven desde que la suscripción de Plesk no deja dar
//     permiso de escritura al pool fuera del sitio.
if (builder.Environment.IsProduction())
{
    var apiRoot = Path.GetFullPath(builder.Environment.ContentRootPath);
    var iisSiteRoot = Environment.GetEnvironmentVariable("APP_POOL_ID") is { Length: > 0 }
        ? Path.GetDirectoryName(apiRoot.TrimEnd(Path.DirectorySeparatorChar))
        : null;
    var uploads = builder.Configuration["Storage:UploadsPath"];
    if (string.IsNullOrWhiteSpace(uploads))
        throw new InvalidOperationException("Storage__UploadsPath no está configurado.");
    foreach (var (name, path) in new[] { ("Storage__UploadsPath", uploads), ("Logging__File__Path", logDirectory), ("ExternalConfig__Path", externalConfig) })
    {
        if (string.IsNullOrWhiteSpace(path)) continue;
        var full = Path.GetFullPath(path);
        if (IsInside(full, apiRoot))
            throw new InvalidOperationException(
                $"{name} ({full}) está dentro de la carpeta del API ({apiRoot}). Muévela fuera, o bajo App_Data del sitio.");
        if (iisSiteRoot is not null && IsInside(full, iisSiteRoot) && !IsInside(full, Path.Combine(iisSiteRoot, "App_Data")))
            throw new InvalidOperationException(
                $"{name} ({full}) está dentro del sitio ({iisSiteRoot}) pero fuera de App_Data: IIS la serviría. Usa {Path.Combine(iisSiteRoot, "App_Data")}\\…");
    }

    var jobsKey = builder.Configuration["Jobs:ApiKey"];
    if (!string.IsNullOrEmpty(jobsKey) && jobsKey.Length < 32)
        throw new InvalidOperationException("Jobs__ApiKey debe tener al menos 32 caracteres (openssl rand -base64 48).");
}

// ── Orígenes del frontend (CORS, manifest, branding) ────────────
var allowedOrigins = new AllowedOrigins(builder.Configuration, builder.Environment);
if (builder.Environment.IsProduction())
    allowedOrigins.EnsureValidForProduction();
builder.Services.AddSingleton(allowedOrigins);

// ── Jobs: Hangfire dentro del proceso solo si Jobs__SchedulerEnabled (true por defecto) ──
var schedulerEnabled = JobRunner.SchedulerEnabled(builder.Configuration);

// ── Authentication ──────────────────────────────────────────────
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSection = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // Por defecto ASP.NET acepta un JWT vencido 5 min más. La web lo renueva sola con el
            // refresh token, así que basta un margen corto para relojes desfasados.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // El token no basta: la cuenta tiene que seguir existiendo, activa y con el mismo rol (SessionValidator)
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var motivo = await SessionValidator.ValidateAsync(context.Principal!, db, jwtSecret, context.HttpContext.RequestAborted);
                if (motivo is not null)
                    context.Fail(motivo);
            }
        };
    });

// ── Authorization ───────────────────────────────────────────────
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdmin", p => p.RequireRole("SuperAdmin"))
    .AddPolicy("Admin", p => p.RequireRole("SuperAdmin", "Admin"))
    .AddPolicy("Esteticista", p => p.RequireRole("SuperAdmin", "Admin", "Esteticista"))
    // Fuera de todo tenant: su JWT no lleva tenant_id y NO entra en las políticas de clínica.
    .AddPolicy("PlatformAdmin", p => p.RequireRole(PlatformAdmin.RoleName));

// ── EF Core ─────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Tenant ──────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();

// ── JWT Service ─────────────────────────────────────────────────
builder.Services.AddScoped<IJwtService, JwtService>();

// ── Contraseñas y correo ────────────────────────────────────────
builder.Services.AddSingleton<IEmailService, SmtpEmailService>(); // sin Smtp:Host no envía nada
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddAuthRateLimits(builder.Configuration, builder.Environment); // RateLimiting:Auth (login, refresh, change-password)

// ── Nivel de plataforma ─────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(builder.Configuration.GetSection("Platform:Precios").Get<PlatformPricing>() ?? new PlatformPricing());
builder.Services.AddScoped<TenantStatusCache>();
builder.Services.AddScoped<BrandingResolver>();
builder.Services.AddScoped<PlatformTenantService>();
builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<LeadService>();
builder.Services.AddScoped<LiquidacionService>();

// ── Adjuntos de imagen ──────────────────────────────────────────
// IFileStorage es el punto de cambio a S3/R2: basta registrar otra implementación aquí.
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<WriteAccessMonitor>(); // ¿puede escribir en archivos y logs? (arranque y /api/health)
builder.Services.AddSingleton<FileTokenService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<CabinConsumptionService>();
builder.Services.AddScoped<EvolutionService>();
builder.Services.AddScoped<ProductLotService>();
builder.Services.AddScoped<RegulatoryReportService>();
builder.Services.AddScoped<DemoImageSeeder>();
builder.Services.AddScoped<DemoSalesSeeder>();

// ── Dashboard y ciclo de vida de paquetes ───────────────────────
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PatientPackageService>();

// ── Importador desde archivos (Excel/CSV) ───────────────────────
// Una IImportDefinition por entidad importable; ImportService las encuentra por su Entidad.
builder.Services.AddScoped<IImportDefinition, PatientImportDefinition>();
builder.Services.AddScoped<IImportDefinition, ProductImportDefinition>();
builder.Services.AddScoped<IImportDefinition, ProcedureImportDefinition>();
builder.Services.AddScoped<IImportDefinition, PackageImportDefinition>();
builder.Services.AddScoped<ImportService>();

// ── Ventas y caja ───────────────────────────────────────────────
// Facturación electrónica con Factus. Cada clínica configura su cuenta (Configuración → Facturación);
// sin configurarla, emitir responde 501 y sus ventas quedan "SinFacturar". Las credenciales se guardan
// cifradas con Secrets:EncryptionKey (si no existe, una clave derivada de Jwt:Secret: cambiar
// cualquiera de las dos obliga a volver a escribir las credenciales de cada clínica).
builder.Services.AddSingleton<ISecretProtector>(new AesSecretProtector(
    builder.Configuration["Secrets:EncryptionKey"] is { Length: > 0 } encryptionKey ? encryptionKey : jwtSecret));
builder.Services.AddSingleton(builder.Configuration.GetSection("Factus").Get<FactusOptions>() ?? new FactusOptions());
builder.Services.AddSingleton<FactusCatalogService>();
builder.Services.AddSingleton<FactusPayloadBuilder>();
builder.Services.AddSingleton<FactusTokenCache>();
builder.Services.AddHttpClient<IElectronicInvoiceProvider, FactusInvoiceProvider>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<InvoicingSettingsService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<CashService>();
builder.Services.AddScoped<SaleService>();

// ── Configuración de la clínica y reportes ──────────────────────
builder.Services.AddScoped<ClinicSettingsService>();
builder.Services.AddScoped<ClinicalNoteService>();
builder.Services.AddScoped<ReportService>();

// ── Valoraciones y consentimiento informado ─────────────────────
builder.Services.AddScoped<ValuationService>();
builder.Services.AddScoped<ConsentService>();

// ── Jobs recurrentes ────────────────────────────────────────────
// Cada IScheduledJob corre por JobRunner (candado + registro en JobRuns, visible en /platform),
// lo dispare quien lo dispare:
//   · Hangfire dentro del API, si Jobs__SchedulerEnabled (desarrollo y servidor propio con Docker).
//   · POST /api/v1/jobs/{nombre} con la cabecera X-Jobs-Key = Jobs__ApiKey (tarea programada de
//     Plesk en IIS, donde el app pool se recicla y un programador interno no es confiable).
builder.Services.AddScoped<JobRunner>();
builder.Services.AddScoped<IScheduledJob, PackageExpirationJob>();
builder.Services.AddScoped<IScheduledJob, LotAlertsJob>();
builder.Services.AddScoped<IScheduledJob, AttachmentPurgeJob>();
builder.Services.AddScoped<IScheduledJob, InvoiceRetryJob>();

// Hangfire sobre la misma base (esquema HangFire), sin dashboard web: no expone nada nuevo. El
// schema se crea al arrancar el servidor de jobs. Apagado, Hangfire ni se registra ni toca la base.
if (schedulerEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"),
            new SqlServerStorageOptions { PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromSeconds(30) }));
    builder.Services.AddHangfireServer(options => options.WorkerCount = 1);
}

// ── Swagger ─────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NemediClinic API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── CORS: solo los orígenes de CORS_ORIGINS (en dev, los locales) ──
const string WebAppCorsPolicy = "WebApp";
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebAppCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins.All.ToArray())
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type", "X-Branding-Host")
              // Las descargas (Excel) toman el nombre del archivo de Content-Disposition
              .WithExposedHeaders("Content-Disposition");
    });
});

// ── Detrás de un proxy: X-Forwarded-For/Proto/Host solo de proxies de confianza ─────
// Por defecto no se confía en ningún proxy y X-Forwarded-* se ignora: la IP es la de la conexión. Así es
// en IIS in-process (el módulo entrega la IP real del cliente). Antes se aceptaba de cualquier origen y
// cualquiera fijaba su IP (el límite de intentos por IP se esquivaba) y su Host (branding).
// Con un proxy delante (Caddy en Docker, que además manda X-Forwarded-Host con el dominio del canal),
// se declaran sus IPs o redes: ForwardedHeaders:KnownProxies (IPs) y ForwardedHeaders:KnownNetworks
// (CIDR, p. ej. la red del compose); una red más amplia que el prefijo mínimo (/8 en IPv4, /32 en IPv6,
// /104 en IPv4 mapeada) o una dirección sin especificar detiene el arranque (TrustedProxies). Ojo: ASP.NET
// acepta las cabeceras de CUALQUIERA si las dos listas quedan vacías, por eso sin configuración no se
// registra UseForwardedHeaders y además se fija ForwardedHeaders = None, para que
// ASPNETCORE_FORWARDEDHEADERS_ENABLED (que registra el middleware por su cuenta con las listas vacías) no
// reabra el hueco. Única excepción: detrás de ANCM fuera de proceso (ASPNETCORE_TOKEN), donde la
// integración de IIS ya acepta solo el loopback del módulo y se deja como está.
var trustedProxies = TrustedProxies.Read(builder.Configuration);
var useForwardedHeaders = trustedProxies.Any;
var behindOutOfProcessAncm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_TOKEN"));
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in trustedProxies.Proxies)
            options.KnownProxies.Add(proxy);
        foreach (var network in trustedProxies.Networks)
            options.KnownIPNetworks.Add(network);
    });
}
else if (!behindOutOfProcessAncm)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.None);
}

// ── Controllers ─────────────────────────────────────────────────
builder.Services
    .AddControllers(options => options.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(options =>
    {
        // Hora de la clínica sin sufijo en toda la API (ver LocalDateTimeJsonConverter).
        options.JsonSerializerOptions.Converters.Add(new LocalDateTimeJsonConverter(clock));
        // Enums aceptan nombre ("Confirmada", "Efectivo") además de entero. El frontend
        // envía nombres en estado de cita, método de pago, tipo de producto y motivo de entrada.
        // Ningún DTO de respuesta expone enums crudos (todos van como string vía ToString()).
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var app = builder.Build();

// Permisos de escritura en la carpeta de archivos y en la de logs: un CRIT al arrancar si faltan,
// en vez de enterarse cuando alguien sube una foto (y /api/health lo sigue reportando).
app.Services.GetRequiredService<WriteAccessMonitor>().GetAsync().GetAwaiter().GetResult();

// ── Migraciones al arrancar: solo si Database__MigrateOnStartup ─
// Docker (servidor propio) lo enciende: espera hasta 60 s a que SQL Server responda, crea la base
// si no existe y aplica las migraciones pendientes; sin conexión termina con código 1 y Compose
// reinicia el contenedor. En IIS/Plesk va apagado: el esquema se actualiza con
// un script idempotente (dotnet ef migrations script --idempotent) antes de publicar, y un reciclaje del app pool nunca dispara
// una migración a medias. Apagado, solo se avisa en el log si falta alguna.
var migrateOnStartup = app.Configuration.GetValue("Database:MigrateOnStartup", false);
if (app.Environment.IsProduction() && !migrateOnStartup)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        using var scope = app.Services.CreateScope();
        var pending = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
            logger.LogCritical("Hay {Count} migraciones sin aplicar ({Migrations}). Aplica el script idempotente de migraciones en la base.",
                pending.Count, string.Join(", ", pending));
    }
    catch (Exception ex)
    {
        logger.LogCritical("No se pudo revisar el estado de la base: {Message}", ex.GetBaseException().Message);
    }
}
if (app.Environment.IsProduction() && migrateOnStartup)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var waited = System.Diagnostics.Stopwatch.StartNew();
    var attempt = 0;
    while (true)
    {
        attempt++;
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = db.Database.GetPendingMigrations().ToList();
            db.Database.Migrate();
            logger.LogInformation("Migraciones aplicadas: {Count} pendientes ejecutadas (intento {Attempt}).", pending.Count, attempt);
            break;
        }
        catch (Exception ex) when (waited.Elapsed < TimeSpan.FromSeconds(60))
        {
            logger.LogWarning("Base de datos no disponible (intento {Attempt}): {Message}. Reintentando en 3 s...", attempt, ex.GetBaseException().Message);
            Thread.Sleep(3000);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "No se pudo conectar a la base de datos en 60 s. Revisa ConnectionStrings__DefaultConnection y que el servicio mssql esté healthy. Último error: {Message}", ex.GetBaseException().Message);
            Environment.Exit(1);
        }
    }
}

// ── Primer PlatformAdmin (Platform__AdminEmail / Platform__AdminPassword) ──
await PlatformAdminSeeder.SeedAsync(
    app.Services, app.Configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PlatformAdminSeeder"));

// ── Reloj: deja en el log con qué zona trabaja y en cuál está el servidor (informativo) ──
var serverNow = TimeProvider.System.GetLocalNow();
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Clock").LogInformation(
    "Hora de la clínica: {Clinica:yyyy-MM-dd HH:mm} ({Zona}). Servidor: {Servidor:yyyy-MM-dd HH:mm} ({ZonaServidor}, UTC{Offset}); no se usa.",
    clock.Now, clock.TimeZone.Id, serverNow.DateTime, TimeProvider.System.LocalTimeZone.Id,
    NemediClinic.Api.Controllers.HealthController.FormatOffset(serverNow.Offset));

// ── Jobs recurrentes en Hangfire (hora local de Bogotá, como el resto del sistema) ──
// Cada IScheduledJob con su CronSugerido, siempre a través de JobRunner (candado + registro).
var jobsLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Jobs");
if (schedulerEnabled)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            var nombre = job.Nombre;
            recurring.AddOrUpdate<JobRunner>(nombre, runner => runner.RunScheduledAsync(nombre), job.CronSugerido,
                new RecurringJobOptions { TimeZone = clock.TimeZone });
        }
    }
    catch (Exception ex)
    {
        jobsLogger.LogWarning("No se pudieron registrar los jobs en Hangfire: {Message}", ex.GetBaseException().Message);
    }
}
else
{
    jobsLogger.LogInformation(string.IsNullOrWhiteSpace(app.Configuration["Jobs:ApiKey"])
        ? "Programador interno apagado y sin Jobs__ApiKey: ningún job recurrente va a correr."
        : "Programador interno apagado: los jobs corren solo por POST /api/v1/jobs/{nombre}.");
}

// ── Pipeline ────────────────────────────────────────────────────
// ── API como aplicación de IIS en /api (clinic.example.com/api) ──
// IIS (in-process) y el módulo fuera de proceso entregan PathBase=/api y Path=/v1/…; las rutas
// del API ya incluyen /api (igual que en desarrollo y en Docker), así que se vuelve a unir. Con
// eso las rutas, los middlewares por ruta y las URLs firmadas (/api/v1/files/…) no cambian.
app.Use((context, next) =>
{
    if (context.Request.PathBase.HasValue)
    {
        context.Request.Path = context.Request.PathBase.Add(context.Request.Path);
        context.Request.PathBase = PathString.Empty;
    }
    return next(context);
});

if (useForwardedHeaders)
    app.UseForwardedHeaders(); // solo de los proxies declarados (ver arriba)

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // En Production el HTTPS lo termina Caddy; Kestrel solo escucha HTTP en 8080.
    app.UseHttpsRedirection();
}

// Explícito y DESPUÉS de reincorporar /api: sin esta línea WebApplication pone UseRouting al
// principio del pipeline y elige el endpoint con la ruta recortada (/v1/…) → todo daría 404 en IIS.
app.UseRouting();
app.UseCors(WebAppCorsPolicy);
app.UseRateLimiter(); // antes de autenticar: cuenta también los intentos sin sesión válida
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<PasswordChangeMiddleware>(); // claim pwd_change: solo change-password, refresh y login hasta cambiar la clave
app.UseMiddleware<TenantStatusMiddleware>(); // tenant Suspendido: escrituras → 423
app.UseMiddleware<BrandingMiddleware>();     // branding por Host para GET /api/v1/branding
app.MapControllers();

app.Run();

static bool IsInside(string path, string root)
{
    var separator = Path.DirectorySeparatorChar.ToString();
    var normalizedRoot = root.EndsWith(separator) ? root : root + separator;
    var normalizedPath = path.EndsWith(separator) ? path : path + separator;
    return normalizedPath.StartsWith(normalizedRoot,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

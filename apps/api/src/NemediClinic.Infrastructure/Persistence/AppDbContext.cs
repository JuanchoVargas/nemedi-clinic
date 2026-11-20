using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;

namespace NemediClinic.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    private readonly IClock _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider, IClock clock)
        : base(options)
    {
        _tenantProvider = tenantProvider;
        _clock = clock;
    }

    /// <summary>
    /// Columnas en UTC (auditoría y expiraciones). SQL Server no guarda el Kind y EF devuelve todo
    /// DateTime como Unspecified; estas se marcan Utc al leerlas para que la API las convierta a la
    /// hora de la clínica al serializarlas (LocalDateTimeJsonConverter). El resto de fechas son
    /// hora de la clínica y viajan tal cual.
    /// </summary>
    public static readonly IReadOnlySet<string> UtcColumns =
        new HashSet<string> { "CreatedAt", "UpdatedAt", "PurgedAt", "RefreshTokenExpiry" };

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    /// <summary>
    /// Tenant de la request actual. El filtro global debe referenciar ESTE miembro
    /// del DbContext (no el provider directamente): EF Core cachea el modelo una sola
    /// vez por proceso y solo re-evalúa por request las expresiones que acceden a
    /// miembros de la instancia actual del DbContext.
    /// </summary>
    public Guid CurrentTenantId => _tenantProvider.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageProcedure> PackageProcedures => Set<PackageProcedure>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ClinicalRecord> ClinicalRecords => Set<ClinicalRecord>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<ClinicalNoteProduct> ClinicalNoteProducts => Set<ClinicalNoteProduct>();
    public DbSet<PatientPackage> PatientPackages => Set<PatientPackage>();
    public DbSet<PatientPackageSession> PatientPackageSessions => Set<PatientPackageSession>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<ElectronicInvoice> ElectronicInvoices => Set<ElectronicInvoice>();
    public DbSet<InvoicingSettings> InvoicingSettings => Set<InvoicingSettings>();
    public DbSet<InvoicingBranchSettings> InvoicingBranchSettings => Set<InvoicingBranchSettings>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<BranchSchedule> BranchSchedules => Set<BranchSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryEntry> InventoryEntries => Set<InventoryEntry>();
    public DbSet<ProductLot> ProductLots => Set<ProductLot>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Valuation> Valuations => Set<Valuation>();
    public DbSet<ValuationProcedure> ValuationProcedures => Set<ValuationProcedure>();
    public DbSet<ConsentTemplate> ConsentTemplates => Set<ConsentTemplate>();
    public DbSet<Consent> Consents => Set<Consent>();

    // Nivel de plataforma: sin TenantId ni filtro global.
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    /// <summary>Ids fijos de los canales sembrados por la migración AddPlatformLevel.</summary>
    public static readonly Guid NemediChannelId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoChannelId = new("22222222-2222-2222-2222-222222222222");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant: exclude from tenant filter (it IS the tenant)
        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // ── Attachment (imágenes) ───────────────────────────
        // Relación polimórfica (EntityType + EntityId): sin FK a propósito.
        modelBuilder.Entity<Attachment>(a =>
        {
            a.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(20);
            a.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            a.Property(x => x.FileName).HasMaxLength(200);
            a.Property(x => x.ContentType).HasMaxLength(100);
            a.Property(x => x.StoragePath).HasMaxLength(300);
            a.Property(x => x.ThumbnailPath).HasMaxLength(300);
            a.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        });

        // ── Valoraciones ────────────────────────────────────
        modelBuilder.Entity<Valuation>(v =>
        {
            v.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            v.Property(x => x.PrecioCotizado).HasPrecision(18, 2);
            v.Property(x => x.ProspectoNombre).HasMaxLength(200);
            v.Property(x => x.ProspectoTelefono).HasMaxLength(50);
            v.Property(x => x.MotivoRechazo).HasMaxLength(500);
            v.HasIndex(x => new { x.TenantId, x.Fecha });
            v.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne(x => x.Esteticist).WithMany().HasForeignKey(x => x.EsteticistId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ValuationProcedure>(vp =>
        {
            vp.HasKey(x => new { x.ValuationId, x.ProcedureId });
            vp.HasOne(x => x.Valuation).WithMany(v => v.Procedures).HasForeignKey(x => x.ValuationId).OnDelete(DeleteBehavior.Cascade);
            vp.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            // Sin TenantId propio: toma el filtro de su valoración (tenant + soft delete). Así EF ya no
            // advierte (10622) que el procedimiento filtrado es el extremo requerido de la relación.
            vp.HasQueryFilter(x => x.Valuation.TenantId == CurrentTenantId && !x.Valuation.IsDeleted);
        });

        // ── Consentimiento informado ────────────────────────
        modelBuilder.Entity<ConsentTemplate>(t =>
        {
            t.Property(x => x.Titulo).HasMaxLength(200);
            t.HasIndex(x => new { x.TenantId, x.ProcedureId }).IsUnique().HasFilter("IsDeleted = 0");
            t.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Consent>(c =>
        {
            c.Property(x => x.Titulo).HasMaxLength(200);
            c.Property(x => x.FirmanteNombre).HasMaxLength(200);
            c.Property(x => x.FirmanteCedula).HasMaxLength(20);
            c.HasIndex(x => new { x.PatientId, x.ProcedureId, x.FechaFirma });
            c.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            c.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            c.HasOne(x => x.Esteticist).WithMany().HasForeignKey(x => x.EsteticistId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Plataforma: Channel / Lead / PlatformAdmin ──────
        modelBuilder.Entity<Channel>(c =>
        {
            c.Property(x => x.Nombre).HasMaxLength(100);
            c.Property(x => x.Slug).HasMaxLength(50);
            c.Property(x => x.NombreComercial).HasMaxLength(100);
            c.Property(x => x.LogoUrl).HasMaxLength(500);
            c.Property(x => x.ColorPrimario).HasMaxLength(7);
            c.Property(x => x.ColorSecundario).HasMaxLength(7);
            c.Property(x => x.Dominio).HasMaxLength(200);
            c.Property(x => x.PorcentajeCanal).HasPrecision(5, 4);
            c.HasIndex(x => x.Slug).IsUnique();
            c.HasIndex(x => x.Dominio).IsUnique();
            c.HasData(
                new Channel
                {
                    Id = NemediChannelId, Nombre = "Nemedi", Slug = "nemedi",
                    NombreComercial = "NemediClinic", ColorPrimario = "#1F4E79", ColorSecundario = "#D9A441",
                    Dominio = "app.example.com", PorcentajeCanal = 0m, Activo = true,
                    CreatedAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc)
                },
                new Channel
                {
                    Id = DemoChannelId, Nombre = "Canal Demo", Slug = "canal-demo",
                    NombreComercial = "Canal Demo Clinic", ColorPrimario = "#0B5FFF", ColorSecundario = "#0A2540",
                    Dominio = "canal-demo.example.com", PorcentajeCanal = 0.20m, Activo = true,
                    CreatedAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc)
                });
        });

        modelBuilder.Entity<Tenant>(t =>
        {
            t.Property(x => x.Plan).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.PorcentajeCanalOverride).HasPrecision(5, 4);
            t.HasOne(x => x.Channel)
                .WithMany(c => c.Tenants)
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Lead>(l =>
        {
            l.Property(x => x.Nombre).HasMaxLength(200);
            l.Property(x => x.NIT).HasMaxLength(20);
            l.Property(x => x.Ciudad).HasMaxLength(100);
            l.Property(x => x.Contacto).HasMaxLength(200);
            l.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            l.HasIndex(x => x.NIT);
            l.HasOne(x => x.Channel)
                .WithMany(c => c.Leads)
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
            l.HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlatformAdmin>(a =>
        {
            a.Property(x => x.Nombre).HasMaxLength(100);
            a.Property(x => x.Email).HasMaxLength(200);
            a.HasIndex(x => x.Email).IsUnique();
        });

        // Ejecuciones de jobs: registro y candado (una sola EnCurso por job)
        modelBuilder.Entity<JobRun>(j =>
        {
            j.Property(x => x.Nombre).HasMaxLength(60);
            j.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            j.Property(x => x.Resultado).HasMaxLength(2000);
            j.Property(x => x.Origen).HasMaxLength(20);
            j.HasIndex(x => x.Nombre).IsUnique().HasFilter("[Estado] = 'EnCurso'").HasDatabaseName("IX_JobRuns_Nombre_EnCurso");
            j.HasIndex(x => new { x.Nombre, x.Inicio });
        });

        // User: unique email per tenant
        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasFilter("IsDeleted = 0");

        modelBuilder.Entity<User>()
            .Property(u => u.Rol)
            .HasConversion<string>()
            .HasMaxLength(20);

        // User -> Branch (optional)
        modelBuilder.Entity<User>()
            .HasOne(u => u.Branch)
            .WithMany(b => b.Users)
            .HasForeignKey(u => u.BranchId)
            .OnDelete(DeleteBehavior.SetNull);

        // User -> Tenant
        modelBuilder.Entity<User>()
            .HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Branch -> Tenant
        modelBuilder.Entity<Branch>()
            .HasOne(b => b.Tenant)
            .WithMany(t => t.Branches)
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Procedure: precision for PrecioBase
        modelBuilder.Entity<Procedure>()
            .Property(p => p.PrecioBase)
            .HasPrecision(18, 2);

        // Package: precision for PrecioTotal
        modelBuilder.Entity<Package>()
            .Property(p => p.PrecioTotal)
            .HasPrecision(18, 2);

        // PackageProcedure: BaseEntity (Id propio). Un procedimiento no se repite dentro del mismo
        // paquete mientras no esté eliminado.
        modelBuilder.Entity<PackageProcedure>()
            .HasIndex(pp => new { pp.PackageId, pp.ProcedureId })
            .IsUnique()
            .HasFilter("IsDeleted = 0");

        modelBuilder.Entity<PackageProcedure>()
            .HasOne(pp => pp.Package)
            .WithMany(p => p.PackageProcedures)
            .HasForeignKey(pp => pp.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PackageProcedure>()
            .HasOne(pp => pp.Procedure)
            .WithMany(p => p.PackageProcedures)
            .HasForeignKey(pp => pp.ProcedureId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── PatientPackage ──────────────────────────────────
        modelBuilder.Entity<PatientPackage>()
            .Property(pp => pp.PackageNombre)
            .HasMaxLength(150);

        // Restrict: el paquete de catálogo se borra en suave y la asignación sigue viva con su copia
        modelBuilder.Entity<PatientPackage>()
            .HasOne(pp => pp.Package)
            .WithMany()
            .HasForeignKey(pp => pp.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── ClinicalNote ────────────────────────────────────
        modelBuilder.Entity<ClinicalNote>(n =>
        {
            n.Property(x => x.ZonaTratada).HasMaxLength(150);
            n.Property(x => x.Parametros).HasMaxLength(300);
            n.Property(x => x.IndicacionesPost).HasMaxLength(1000);
        });

        // ── Product (trazabilidad sanitaria) ────────────────
        modelBuilder.Entity<Product>(p =>
        {
            p.Property(x => x.TipoRegulatorio).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.RegistroSanitarioInvima).HasMaxLength(60);
            p.Property(x => x.PrincipioActivo).HasMaxLength(150);
            p.Property(x => x.Concentracion).HasMaxLength(60);
            p.Property(x => x.Categoria).HasMaxLength(100);
            p.HasIndex(x => new { x.TenantId, x.Categoria });
        });

        // ── ProductLot ──────────────────────────────────────
        modelBuilder.Entity<ProductLot>(l =>
        {
            l.Property(x => x.NumeroLote).HasMaxLength(60);
            l.Property(x => x.Proveedor).HasMaxLength(150);
            l.Property(x => x.NumeroFactura).HasMaxLength(60);
            l.Property(x => x.RegistroSanitario).HasMaxLength(60);
            l.Property(x => x.CantidadInicial).HasPrecision(18, 4);
            l.Property(x => x.CantidadDisponible).HasPrecision(18, 4);
            l.Property(x => x.CostoUnitario).HasPrecision(18, 2);
            l.HasOne(x => x.Product).WithMany(p => p.Lotes)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            // FEFO: la consulta ordena siempre por vencimiento dentro de un producto
            l.HasIndex(x => new { x.ProductId, x.FechaVencimiento });
        });

        // Restrict en ambos: el lote es el respaldo de la trazabilidad y no se borra en cascada
        modelBuilder.Entity<InventoryEntry>()
            .HasOne(e => e.ProductLot).WithMany()
            .HasForeignKey(e => e.ProductLotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<InventoryMovement>()
            .HasOne(m => m.ProductLot).WithMany()
            .HasForeignKey(m => m.ProductLotId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── ClinicalNoteProduct ─────────────────────────────
        modelBuilder.Entity<ClinicalNoteProduct>(cnp =>
        {
            cnp.Property(x => x.Cantidad).HasPrecision(18, 4);
            cnp.HasOne(x => x.ClinicalNote)
                .WithMany(n => n.Productos)
                .HasForeignKey(x => x.ClinicalNoteId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict: un producto con consumo registrado no se puede borrar en duro
            cnp.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Patient ─────────────────────────────────────────
        modelBuilder.Entity<Patient>()
            .HasIndex(p => new { p.TenantId, p.Cedula })
            .IsUnique()
            .HasFilter("IsDeleted = 0");

        // Patient -> ClinicalRecord (1:1)
        modelBuilder.Entity<ClinicalRecord>()
            .HasOne(cr => cr.Patient)
            .WithOne(p => p.ClinicalRecord)
            .HasForeignKey<ClinicalRecord>(cr => cr.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // ClinicalNote -> ClinicalRecord
        modelBuilder.Entity<ClinicalNote>()
            .HasOne(cn => cn.ClinicalRecord)
            .WithMany(cr => cr.ClinicalNotes)
            .HasForeignKey(cn => cn.ClinicalRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // ClinicalNote -> User (Esteticist)
        modelBuilder.Entity<ClinicalNote>()
            .HasOne(cn => cn.Esteticist)
            .WithMany()
            .HasForeignKey(cn => cn.EsteticistId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── PatientPackage ──────────────────────────────────
        modelBuilder.Entity<PatientPackage>()
            .Property(pp => pp.PrecioAcordado)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PatientPackage>()
            .Property(pp => pp.Estado)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<PatientPackage>()
            .HasOne(pp => pp.Patient)
            .WithMany(p => p.PatientPackages)
            .HasForeignKey(pp => pp.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PatientPackage>()
            .HasOne(pp => pp.Package)
            .WithMany()
            .HasForeignKey(pp => pp.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── PatientPackageSession ───────────────────────────
        modelBuilder.Entity<PatientPackageSession>()
            .Property(s => s.Estado)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<PatientPackageSession>()
            .HasOne(s => s.PatientPackage)
            .WithMany(pp => pp.Sessions)
            .HasForeignKey(s => s.PatientPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PatientPackageSession>()
            .HasOne(s => s.Procedure)
            .WithMany()
            .HasForeignKey(s => s.ProcedureId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Payment (antes PatientPayment: paquete, venta o ambos) ──
        modelBuilder.Entity<Payment>(p =>
        {
            p.ToTable("Payments");
            p.Property(x => x.Monto).HasPrecision(18, 2);
            p.Property(x => x.MetodoPago).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.Referencia).HasMaxLength(100);

            // Quién registró el pago. Restrict: borrar (soft) un usuario nunca toca sus pagos.
            p.HasOne(x => x.RegistradoPor).WithMany()
                .HasForeignKey(x => x.RegistradoPorId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.PatientPackage).WithMany(pp => pp.Payments)
                .HasForeignKey(x => x.PatientPackageId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.Sale).WithMany(s => s.Pagos)
                .HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.CashSession).WithMany()
                .HasForeignKey(x => x.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.TenantId, x.FechaPago });
            p.HasIndex(x => x.PatientId);
        });

        // ── Ventas ──────────────────────────────────────────
        modelBuilder.Entity<Sale>(s =>
        {
            s.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            s.Property(x => x.FacturaEstado).HasConversion<string>().HasMaxLength(20);
            s.Property(x => x.Subtotal).HasPrecision(18, 2);
            s.Property(x => x.Descuento).HasPrecision(18, 2);
            s.Property(x => x.Impuestos).HasPrecision(18, 2);
            s.Property(x => x.Total).HasPrecision(18, 2);
            s.Property(x => x.ClienteNombre).HasMaxLength(200);
            s.Property(x => x.ClienteDocumento).HasMaxLength(30);
            s.Property(x => x.Observacion).HasMaxLength(500);
            s.Property(x => x.MotivoAnulacion).HasMaxLength(500);
            s.Property(x => x.FacturaNumero).HasMaxLength(50);
            s.Property(x => x.FacturaCufe).HasMaxLength(200);
            s.Property(x => x.FacturaMensaje).HasMaxLength(1000);
            // Consecutivo interno único por clínica
            s.HasIndex(x => new { x.TenantId, x.Numero }).IsUnique();
            s.HasIndex(x => new { x.TenantId, x.Fecha });
            s.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleLine>(l =>
        {
            l.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            l.Property(x => x.Descripcion).HasMaxLength(250);
            l.Property(x => x.Cantidad).HasPrecision(18, 4);
            l.Property(x => x.PrecioUnitario).HasPrecision(18, 2);
            l.Property(x => x.Descuento).HasPrecision(18, 2);
            l.Property(x => x.IvaPorcentaje).HasPrecision(5, 2);
            l.Property(x => x.Subtotal).HasPrecision(18, 2);
            l.Property(x => x.Iva).HasPrecision(18, 2);
            l.Property(x => x.Total).HasPrecision(18, 2);
            l.HasOne(x => x.Sale).WithMany(s => s.Lineas).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Caja ────────────────────────────────────────────
        modelBuilder.Entity<CashSession>(c =>
        {
            c.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            c.Property(x => x.MontoApertura).HasPrecision(18, 2);
            foreach (var total in new[]
            {
                nameof(CashSession.TotalEfectivo), nameof(CashSession.TotalTransferencia), nameof(CashSession.TotalTarjeta),
                nameof(CashSession.TotalOtro), nameof(CashSession.TotalIngresos), nameof(CashSession.TotalEgresos),
                nameof(CashSession.EfectivoEsperado), nameof(CashSession.EfectivoContado), nameof(CashSession.Diferencia)
            })
                c.Property(total).HasPrecision(18, 2);
            c.Property(x => x.ObservacionApertura).HasMaxLength(500);
            c.Property(x => x.ObservacionCierre).HasMaxLength(500);
            // Una sola caja abierta por usuario
            c.HasIndex(x => new { x.TenantId, x.UsuarioId })
                .IsUnique()
                .HasFilter("[Estado] = 'Abierta' AND [IsDeleted] = 0");
            c.HasIndex(x => new { x.TenantId, x.FechaApertura });
            c.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            c.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashMovement>(m =>
        {
            m.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.Concepto).HasMaxLength(250);
            m.Property(x => x.Monto).HasPrecision(18, 2);
            m.HasOne(x => x.CashSession).WithMany(s => s.Movimientos)
                .HasForeignKey(x => x.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Facturación electrónica ─────────────────────────
        modelBuilder.Entity<ElectronicInvoice>(f =>
        {
            f.Property(x => x.TipoDocumento).HasConversion<string>().HasMaxLength(20);
            f.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            f.Property(x => x.Referencia).HasMaxLength(80);
            f.Property(x => x.Numero).HasMaxLength(50);
            f.Property(x => x.Cufe).HasMaxLength(200);
            f.Property(x => x.ClienteNombre).HasMaxLength(200);
            f.Property(x => x.ClienteDocumento).HasMaxLength(30);
            f.Property(x => x.ClienteEmail).HasMaxLength(200);
            f.Property(x => x.Subtotal).HasPrecision(18, 2);
            f.Property(x => x.Impuestos).HasPrecision(18, 2);
            f.Property(x => x.Total).HasPrecision(18, 2);
            f.Property(x => x.Mensaje).HasMaxLength(4000);
            f.Property(x => x.Advertencias).HasMaxLength(4000);
            f.Property(x => x.ConceptoCorreccion).HasMaxLength(5);
            f.Property(x => x.Motivo).HasMaxLength(500);
            f.Property(x => x.CorreoEnviadoA).HasMaxLength(200);
            // La referencia ante el proveedor nunca se repite en la clínica (ni con borrados)
            f.HasIndex(x => new { x.TenantId, x.Referencia }).IsUnique();
            f.HasIndex(x => new { x.TenantId, x.Estado });
            f.HasIndex(x => new { x.TenantId, x.CreatedAt });
            f.HasIndex(x => x.SaleId);
            f.HasIndex(x => x.PaymentId);
            f.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            f.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            f.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            f.HasOne(x => x.FacturaOrigen).WithMany().HasForeignKey(x => x.FacturaOrigenId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoicingSettings>(c =>
        {
            c.Property(x => x.Ambiente).HasConversion<string>().HasMaxLength(20);
            c.Property(x => x.CredencialesCifradas).HasMaxLength(4000);
            c.Property(x => x.EmisorNit).HasMaxLength(20);
            c.Property(x => x.EmisorDv).HasMaxLength(1);
            c.Property(x => x.EmisorRazonSocial).HasMaxLength(200);
            c.Property(x => x.EmisorTipoOrganizacion).HasMaxLength(5);
            c.Property(x => x.EmisorResponsabilidad).HasMaxLength(20);
            c.Property(x => x.EmisorMunicipio).HasMaxLength(10);
            c.Property(x => x.EmisorDireccion).HasMaxLength(200);
            c.Property(x => x.EmisorTelefono).HasMaxLength(30);
            c.Property(x => x.EmisorEmail).HasMaxLength(200);
            c.Property(x => x.UnidadMedida).HasMaxLength(10);
            c.Property(x => x.CodigoEstandar).HasMaxLength(10);
            c.Property(x => x.CodigosMetodoPago).HasMaxLength(1000);
            c.Property(x => x.UltimaPruebaMensaje).HasMaxLength(1000);
            // Una configuración por clínica
            c.HasIndex(x => x.TenantId).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<InvoicingBranchSettings>(b =>
        {
            b.Property(x => x.PrefijoFactura).HasMaxLength(20);
            b.Property(x => x.PrefijoNotaCredito).HasMaxLength(20);
            b.HasIndex(x => new { x.InvoicingSettingsId, x.BranchId }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasOne(x => x.InvoicingSettings).WithMany(s => s.Sedes).HasForeignKey(x => x.InvoicingSettingsId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Importaciones desde archivo ─────────────────────
        modelBuilder.Entity<ImportRun>(i =>
        {
            i.Property(x => x.Entidad).HasMaxLength(40);
            i.Property(x => x.ArchivoNombre).HasMaxLength(260);
            i.Property(x => x.ModoDuplicados).HasMaxLength(20);
            i.Property(x => x.Detalle).HasMaxLength(300);
            i.HasIndex(x => new { x.TenantId, x.CreatedAt });
            i.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Configuración de la clínica ─────────────────────
        modelBuilder.Entity<Tenant>().Property(t => t.Direccion).HasMaxLength(200);
        modelBuilder.Entity<Tenant>().Property(t => t.Ciudad).HasMaxLength(100);
        modelBuilder.Entity<BranchSchedule>(h =>
        {
            h.HasOne(x => x.Branch).WithMany(b => b.Horarios).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
            h.HasIndex(x => new { x.BranchId, x.DiaSemana });
        });
        modelBuilder.Entity<Appointment>().Property(a => a.MotivoCancelacion).HasMaxLength(500);

        // ── RIPS ────────────────────────────────────────────
        modelBuilder.Entity<Patient>(p =>
        {
            p.Property(x => x.TipoDocumento).HasConversion<string>().HasMaxLength(2);
            p.Property(x => x.Sexo).HasConversion<string>().HasMaxLength(15);
            p.Property(x => x.ZonaResidencia).HasConversion<string>().HasMaxLength(10);
            p.Property(x => x.TipoUsuario).HasConversion<string>().HasMaxLength(30);
            p.Property(x => x.PaisResidencia).HasMaxLength(3);
            p.Property(x => x.MunicipioResidencia).HasMaxLength(5);
            p.Property(x => x.CodigoEntidad).HasMaxLength(10);
            p.Property(x => x.NombreEntidad).HasMaxLength(150);
            p.Property(x => x.Direccion).HasMaxLength(200);
            p.Property(x => x.Ciudad).HasMaxLength(100);
        });
        modelBuilder.Entity<Procedure>(p =>
        {
            p.Property(x => x.CodigoCups).HasMaxLength(10);
            p.Property(x => x.FinalidadConsulta).HasConversion<string>().HasMaxLength(30);
            p.Property(x => x.TipoServicio).HasConversion<string>().HasMaxLength(20);
        });
        modelBuilder.Entity<Tenant>().Property(t => t.CodigoHabilitacionReps).HasMaxLength(12);

        // ── IVA del catálogo y de la clínica ────────────────
        modelBuilder.Entity<Tenant>().Property(t => t.IvaProductos).HasPrecision(5, 2);
        modelBuilder.Entity<Tenant>().Property(t => t.IvaServicios).HasPrecision(5, 2);
        modelBuilder.Entity<Product>().Property(p => p.PrecioVenta).HasPrecision(18, 2);
        modelBuilder.Entity<Product>().Property(p => p.IvaPorcentaje).HasPrecision(5, 2);
        modelBuilder.Entity<Procedure>().Property(p => p.IvaPorcentaje).HasPrecision(5, 2);
        modelBuilder.Entity<Package>().Property(p => p.IvaPorcentaje).HasPrecision(5, 2);

        // ── Appointment ─────────────────────────────────────
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Estado)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Índice para acelerar búsqueda de conflictos por esteticista + ventana de tiempo
        modelBuilder.Entity<Appointment>()
            .HasIndex(a => new { a.EsteticistId, a.FechaInicio });

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Patient)
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.PatientPackageSession)
            .WithMany()
            .HasForeignKey(a => a.PatientPackageSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Esteticist)
            .WithMany()
            .HasForeignKey(a => a.EsteticistId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Procedure)
            .WithMany()
            .HasForeignKey(a => a.ProcedureId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(a => a.Branch)
            .WithMany()
            .HasForeignKey(a => a.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Product ─────────────────────────────────────────
        modelBuilder.Entity<Product>()
            .Property(p => p.TipoProducto)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<Product>()
            .Property(p => p.StockActual)
            .HasPrecision(18, 4);

        modelBuilder.Entity<Product>()
            .Property(p => p.StockMinimo)
            .HasPrecision(18, 4);

        modelBuilder.Entity<Product>()
            .Property(p => p.StockMaximo)
            .HasPrecision(18, 4);

        // ── InventoryEntry ──────────────────────────────────
        modelBuilder.Entity<InventoryEntry>()
            .Property(e => e.MotivoEntrada)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<InventoryEntry>()
            .Property(e => e.Cantidad)
            .HasPrecision(18, 4);

        modelBuilder.Entity<InventoryEntry>()
            .HasOne(e => e.Product)
            .WithMany()
            .HasForeignKey(e => e.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InventoryEntry>()
            .HasOne(e => e.Usuario)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── InventoryMovement ───────────────────────────────
        modelBuilder.Entity<InventoryMovement>()
            .Property(m => m.TipoMovimiento)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<InventoryMovement>()
            .Property(m => m.Cantidad)
            .HasPrecision(18, 4);

        modelBuilder.Entity<InventoryMovement>()
            .HasIndex(m => new { m.ProductId, m.FechaMovimiento });

        modelBuilder.Entity<InventoryMovement>()
            .HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InventoryMovement>()
            .HasOne(m => m.Usuario)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties().Where(p => UtcColumns.Contains(p.Name)))
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(UtcConverter);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(NullableUtcConverter);
            }

            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // RowVersion as concurrency token
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.RowVersion))
                .IsRowVersion();

            // Tenant has its own filter (no TenantId filtering on itself)
            if (entityType.ClrType == typeof(Tenant))
                continue;

            // Global query filter: TenantId + soft delete
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(
                BuildFilterExpression(entityType.ClrType));
        }
    }

    private LambdaExpression BuildFilterExpression(Type entityType)
    {
        // e => e.TenantId == this.CurrentTenantId && !e.IsDeleted
        // IMPORTANTE: la constante debe ser el DbContext (this). Antes era el
        // ITenantProvider, y como EF cachea el modelo por proceso, el filtro
        // quedaba fijado al tenant de la PRIMERA request que construyó el modelo.
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");

        var tenantIdProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.TenantId));
        var tenantIdValue = System.Linq.Expressions.Expression.Property(
            System.Linq.Expressions.Expression.Constant(this),
            nameof(CurrentTenantId));
        var tenantFilter = System.Linq.Expressions.Expression.Equal(tenantIdProperty, tenantIdValue);

        var isDeletedProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
        var notDeleted = System.Linq.Expressions.Expression.Not(isDeletedProperty);

        var combined = System.Linq.Expressions.Expression.AndAlso(tenantFilter, notDeleted);

        return System.Linq.Expressions.Expression.Lambda(combined, parameter);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = _tenantProvider.TenantId;
                    entry.Entity.CreatedAt = _clock.UtcNow;
                    entry.Entity.UpdatedAt = _clock.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = _clock.UtcNow;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

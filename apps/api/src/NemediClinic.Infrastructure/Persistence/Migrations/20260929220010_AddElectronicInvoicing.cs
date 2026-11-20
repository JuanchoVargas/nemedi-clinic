using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NemediClinic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElectronicInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectronicInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TipoDocumento = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacturaOrigenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Referencia = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RangoNumeracionId = table.Column<int>(type: "int", nullable: true),
                    Numero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Cufe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaEmision = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClienteNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClienteDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClienteEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Impuestos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Advertencias = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ConceptoCorreccion = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    XmlId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PdfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    UltimoIntento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Documento = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorreoEnviadoA = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaCorreo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmitidaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectronicInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElectronicInvoices_ElectronicInvoices_FacturaOrigenId",
                        column: x => x.FacturaOrigenId,
                        principalTable: "ElectronicInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicInvoices_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicInvoices_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectronicInvoices_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Habilitada = table.Column<bool>(type: "bit", nullable: false),
                    Ambiente = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CredencialesCifradas = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EmisorNit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmisorDv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    EmisorRazonSocial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmisorTipoOrganizacion = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    EmisorResponsabilidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmisorMunicipio = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    EmisorDireccion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmisorTelefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    EmisorEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UnidadMedida = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CodigoEstandar = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IvaCeroExcluido = table.Column<bool>(type: "bit", nullable: false),
                    EnviarCorreoAlEmitir = table.Column<bool>(type: "bit", nullable: false),
                    CodigosMetodoPago = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UltimaPrueba = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimaPruebaExitosa = table.Column<bool>(type: "bit", nullable: false),
                    UltimaPruebaMensaje = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoicingBranchSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoicingSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RangoFacturaId = table.Column<int>(type: "int", nullable: true),
                    PrefijoFactura = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RangoNotaCreditoId = table.Column<int>(type: "int", nullable: true),
                    PrefijoNotaCredito = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingBranchSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicingBranchSettings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoicingBranchSettings_InvoicingSettings_InvoicingSettingsId",
                        column: x => x.InvoicingSettingsId,
                        principalTable: "InvoicingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_FacturaOrigenId",
                table: "ElectronicInvoices",
                column: "FacturaOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_PatientId",
                table: "ElectronicInvoices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_PaymentId",
                table: "ElectronicInvoices",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_SaleId",
                table: "ElectronicInvoices",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_TenantId_CreatedAt",
                table: "ElectronicInvoices",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_TenantId_Estado",
                table: "ElectronicInvoices",
                columns: new[] { "TenantId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectronicInvoices_TenantId_Referencia",
                table: "ElectronicInvoices",
                columns: new[] { "TenantId", "Referencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingBranchSettings_BranchId",
                table: "InvoicingBranchSettings",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingBranchSettings_InvoicingSettingsId_BranchId",
                table: "InvoicingBranchSettings",
                columns: new[] { "InvoicingSettingsId", "BranchId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingSettings_TenantId",
                table: "InvoicingSettings",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectronicInvoices");

            migrationBuilder.DropTable(
                name: "InvoicingBranchSettings");

            migrationBuilder.DropTable(
                name: "InvoicingSettings");
        }
    }
}

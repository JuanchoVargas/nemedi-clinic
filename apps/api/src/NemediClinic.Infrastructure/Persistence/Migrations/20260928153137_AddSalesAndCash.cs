using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NemediClinic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesAndCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PatientPayments → Payments: renombre, no recreación (los pagos existentes se conservan) ──
            migrationBuilder.DropForeignKey(
                name: "FK_PatientPayments_PatientPackages_PatientPackageId",
                table: "PatientPayments");
            migrationBuilder.DropForeignKey(
                name: "FK_PatientPayments_Users_RegistradoPorId",
                table: "PatientPayments");
            migrationBuilder.DropPrimaryKey(
                name: "PK_PatientPayments",
                table: "PatientPayments");
            migrationBuilder.DropIndex(
                name: "IX_PatientPayments_PatientPackageId",
                table: "PatientPayments");

            migrationBuilder.RenameTable(
                name: "PatientPayments",
                newName: "Payments");
            migrationBuilder.RenameIndex(
                name: "IX_PatientPayments_RegistradoPorId",
                table: "Payments",
                newName: "IX_Payments_RegistradoPorId");

            // Un pago ya no es solo de un paquete: puede ser de una venta
            migrationBuilder.AlterColumn<Guid>(
                name: "PatientPackageId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "CashSessionId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAnulacion",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Payments",
                table: "Payments",
                column: "Id");

            // Los pagos existentes son todos de paquetes: el paciente sale de su asignación
            migrationBuilder.Sql(@"
UPDATE p SET p.PatientId = pp.PatientId
FROM Payments p
INNER JOIN PatientPackages pp ON pp.Id = p.PatientPackageId
WHERE p.PatientId IS NULL;");

            // IVA de productos 19 % en las clínicas existentes; servicios 0 % (excluidos)
            migrationBuilder.AddColumn<decimal>(
                name: "IvaProductos",
                table: "Tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 19m);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaServicios",
                table: "Tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaPorcentaje",
                table: "Products",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioVenta",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaPorcentaje",
                table: "Procedures",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "PatientPackages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IvaPorcentaje",
                table: "Packages",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaApertura = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MontoApertura = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ObservacionApertura = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CerradaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObservacionCierre = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalEfectivo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalTransferencia = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalTarjeta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalOtro = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalIngresos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalEgresos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EfectivoEsperado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EfectivoContado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Diferencia = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashSessions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_Users_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClienteNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClienteDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Impuestos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Observacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladaPorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FacturaEstado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FacturaNumero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FacturaCufe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FacturaFecha = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FacturaXmlId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FacturaPdfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FacturaMensaje = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Concepto = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferenciaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IvaPorcentaje = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PatientPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_PatientPackages_PatientPackageId",
                table: "Payments",
                column: "PatientPackageId",
                principalTable: "PatientPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_RegistradoPorId",
                table: "Payments",
                column: "RegistradoPorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Sales_SaleId",
                table: "Payments",
                column: "SaleId",
                principalTable: "Sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CashSessions_CashSessionId",
                table: "Payments",
                column: "CashSessionId",
                principalTable: "CashSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashSessionId",
                table: "CashMovements",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BranchId",
                table: "CashSessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_FechaApertura",
                table: "CashSessions",
                columns: new[] { "TenantId", "FechaApertura" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_UsuarioId",
                table: "CashSessions",
                columns: new[] { "TenantId", "UsuarioId" },
                unique: true,
                filter: "[Estado] = 'Abierta' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_UsuarioId",
                table: "CashSessions",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CashSessionId",
                table: "Payments",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PatientId",
                table: "Payments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PatientPackageId",
                table: "Payments",
                column: "PatientPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId",
                table: "Payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_FechaPago",
                table: "Payments",
                columns: new[] { "TenantId", "FechaPago" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BranchId",
                table: "Sales",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PatientId",
                table: "Sales",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TenantId_Fecha",
                table: "Sales",
                columns: new[] { "TenantId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TenantId_Numero",
                table: "Sales",
                columns: new[] { "TenantId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_UsuarioId",
                table: "Sales",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropForeignKey(name: "FK_Payments_CashSessions_CashSessionId", table: "Payments");
            migrationBuilder.DropForeignKey(name: "FK_Payments_Sales_SaleId", table: "Payments");
            migrationBuilder.DropForeignKey(name: "FK_Payments_PatientPackages_PatientPackageId", table: "Payments");
            migrationBuilder.DropForeignKey(name: "FK_Payments_Users_RegistradoPorId", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_CashSessionId", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_PatientId", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_PatientPackageId", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_SaleId", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_TenantId_FechaPago", table: "Payments");
            migrationBuilder.DropPrimaryKey(name: "PK_Payments", table: "Payments");
            // Los pagos de ventas sin paquete no caben en el esquema anterior
            migrationBuilder.Sql("DELETE FROM Payments WHERE PatientPackageId IS NULL;");
            migrationBuilder.DropColumn(name: "SaleId", table: "Payments");
            migrationBuilder.DropColumn(name: "PatientId", table: "Payments");
            migrationBuilder.DropColumn(name: "CashSessionId", table: "Payments");
            migrationBuilder.DropColumn(name: "FechaAnulacion", table: "Payments");
            migrationBuilder.AlterColumn<Guid>(
                name: "PatientPackageId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
            migrationBuilder.RenameTable(name: "Payments", newName: "PatientPayments");
            migrationBuilder.RenameIndex(name: "IX_Payments_RegistradoPorId", table: "PatientPayments", newName: "IX_PatientPayments_RegistradoPorId");
            migrationBuilder.AddPrimaryKey(name: "PK_PatientPayments", table: "PatientPayments", column: "Id");
            migrationBuilder.CreateIndex(name: "IX_PatientPayments_PatientPackageId", table: "PatientPayments", column: "PatientPackageId");
            migrationBuilder.AddForeignKey(
                name: "FK_PatientPayments_PatientPackages_PatientPackageId",
                table: "PatientPayments",
                column: "PatientPackageId",
                principalTable: "PatientPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_PatientPayments_Users_RegistradoPorId",
                table: "PatientPayments",
                column: "RegistradoPorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(
                name: "SaleLines");

            migrationBuilder.DropTable(
                name: "CashSessions");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropColumn(
                name: "IvaProductos",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IvaServicios",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IvaPorcentaje",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PrecioVenta",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IvaPorcentaje",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "PatientPackages");

            migrationBuilder.DropColumn(
                name: "IvaPorcentaje",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "InventoryMovements");
        }
    }
}

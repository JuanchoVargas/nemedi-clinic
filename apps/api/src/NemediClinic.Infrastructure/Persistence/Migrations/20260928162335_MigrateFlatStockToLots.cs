using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NemediClinic.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Los productos con existencia de antes de los lotes llevaban el stock en la columna plana
    /// Product.StockActual. Se les crea un lote "sin número" con esa existencia (sin vencimiento,
    /// con la copia del registro INVIMA) para que todo el inventario quede trazable por lote y el
    /// stock se recalcule siempre desde los lotes. Solo toca productos sin ningún lote.
    /// </summary>
    public partial class MigrateFlatStockToLots : Migration
    {
        public const string Marca = "Existencia anterior a los lotes";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
INSERT INTO ProductLots (Id, TenantId, ProductId, NumeroLote, FechaVencimiento, CantidadInicial, CantidadDisponible,
                         FechaIngreso, Proveedor, NumeroFactura, RegistroSanitario, CreatedAt, UpdatedAt, IsDeleted)
SELECT NEWID(), p.TenantId, p.Id, NULL, NULL, p.StockActual, p.StockActual,
       -- Hora de Bogotá, no la del SQL Server (GETDATE() sería la zona del servidor)
       CONVERT(datetime2, SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'SA Pacific Standard Time'), '{Marca}', NULL, p.RegistroSanitarioInvima, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
FROM Products p
WHERE p.IsDeleted = 0 AND p.StockActual > 0
  AND NOT EXISTS (SELECT 1 FROM ProductLots l WHERE l.ProductId = p.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo los lotes creados aquí y que no se movieron (sin movimientos que los referencien)
            migrationBuilder.Sql($@"
DELETE l FROM ProductLots l
WHERE l.Proveedor = '{Marca}' AND l.NumeroLote IS NULL
  AND NOT EXISTS (SELECT 1 FROM InventoryMovements m WHERE m.ProductLotId = l.Id);");
        }
    }
}

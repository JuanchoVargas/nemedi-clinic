// Limpieza de lo que deja el capítulo de ventas y caja: sus ventas (por la observación) se anulan
// por API —así el stock vuelve a su lote y los paquetes se retiran— y después se borran en físico
// con sus pagos y movimientos; también las cajas de recepción. Es la base de desarrollo que se usa
// en las demos: no puede ir acumulando ventas de prueba corrida tras corrida.
import { req } from "./api";
import { sql, lit } from "../sql";

const OBS = "Venta del recorrido de manual";
const RECEPCION = "recepcion@nemedi.demo";

export async function cleanupWalkthroughSales(token: string) {
  const ids = sql(`SELECT CONVERT(varchar(36), Id) FROM Sales WHERE Observacion = ${lit(OBS)} AND IsDeleted = 0`).map((r) => r[0]);
  for (const id of ids) await req("POST", `/api/v1/sales/${id}/void`, { token, body: { motivo: "Limpieza del recorrido" } });

  const ventas = `SELECT Id FROM Sales WHERE Observacion = ${lit(OBS)}`;
  const cajas = `SELECT c.Id FROM CashSessions c JOIN Users u ON u.Id = c.UsuarioId WHERE u.Email = ${lit(RECEPCION)}`;
  sql([
    `DELETE FROM Payments WHERE SaleId IN (${ventas}) OR CashSessionId IN (${cajas});`,
    `DELETE FROM InventoryMovements WHERE SaleId IN (${ventas});`,
    `DELETE FROM PatientPackageSessions WHERE PatientPackageId IN (SELECT Id FROM PatientPackages WHERE SaleId IN (${ventas}));`,
    `DELETE FROM PatientPackages WHERE SaleId IN (${ventas});`,
    `DELETE FROM SaleLines WHERE SaleId IN (${ventas});`,
    `DELETE FROM CashMovements WHERE CashSessionId IN (${cajas}) OR SaleId IN (${ventas});`,
    `DELETE FROM Sales WHERE Observacion = ${lit(OBS)};`,
    `DELETE FROM CashSessions WHERE Id IN (${cajas});`,
  ].join(" "));
}

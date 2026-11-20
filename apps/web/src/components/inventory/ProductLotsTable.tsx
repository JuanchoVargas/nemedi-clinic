// ============================================================
// ProductLotsTable.tsx — Lotes de un producto
//
// Orden FEFO: primero el que vence antes, que es el que hay que sacar.
// Los lotes agotados quedan al final, en gris, porque siguen siendo
// parte del respaldo que se le muestra a la Secretaría de Salud.
// ============================================================
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/shared/EmptyState";
import { LoteBadge } from "@/components/shared/LoteBadge";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { MotionTableRow, staggerProps } from "@/components/shared/motion-elements";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { WriteOffLotDialog, type WriteOffTarget } from "@/components/inventory/WriteOffLotDialog";
import { useProductLots } from "@/api/inventory.api";
import { usePermissions } from "@/hooks/use-permissions";
import { formatShortDate } from "@/lib/format-platform";
import { formatCOP } from "@/lib/format-cop";
import { cn } from "@/lib/utils";

export function ProductLotsTable({ productId, unidadMedida, productoNombre = "" }: { productId: string; unidadMedida: string; productoNombre?: string }) {
  const { data: lotes, isLoading } = useProductLots(productId);
  const { can } = usePermissions();
  const [baja, setBaja] = useState<WriteOffTarget | null>(null);

  if (isLoading) return <Skeleton className="h-32 w-full" />;

  if (!lotes || lotes.length === 0) {
    return (
      <EmptyState
        illustration="inventory"
        title="Este producto no maneja lotes"
        description="Al registrar una entrada puedes anotar el número de lote, el vencimiento, el proveedor y la factura."
      />
    );
  }

  return (
    <ResponsiveTable>
      <Table aria-label="Lotes del producto">
        <TableHeader>
          <TableRow>
            <TableHead>Lote</TableHead>
            <TableHead>Vencimiento</TableHead>
            <TableHead className="text-right">Disponible</TableHead>
            <TableHead>Proveedor</TableHead>
            <TableHead>Factura</TableHead>
            <TableHead className="text-right">Costo unitario</TableHead>
            <TableHead>Ingreso</TableHead>
            {can("products.update") && (
              <TableHead className="text-right">
                <span className="sr-only">Acciones</span>
              </TableHead>
            )}
          </TableRow>
        </TableHeader>
        <TableBody>
          {lotes.map((lote, i) => (
            <MotionTableRow key={lote.id} {...staggerProps(i)} className={cn(lote.cantidadDisponible <= 0 && "opacity-55")}>
              <TableCell className="font-medium">
                {lote.numeroLote ?? <span className="text-muted-foreground">Sin número</span>}
                {lote.registroSanitario && (
                  <div className="text-xs text-muted-foreground">{lote.registroSanitario}</div>
                )}
              </TableCell>
              <TableCell>
                {lote.fechaVencimiento ? (
                  <div className="flex flex-col items-start gap-1">
                    <span>{formatShortDate(lote.fechaVencimiento)}</span>
                    <LoteBadge estado={lote.estado} diasParaVencer={lote.diasParaVencer} />
                  </div>
                ) : (
                  <span className="text-muted-foreground">No vence</span>
                )}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {lote.cantidadDisponible.toLocaleString("es-CO")} {unidadMedida}
                <div className="text-xs text-muted-foreground">de {lote.cantidadInicial.toLocaleString("es-CO")}</div>
              </TableCell>
              <TableCell>{lote.proveedor ?? <span className="text-muted-foreground">—</span>}</TableCell>
              <TableCell>{lote.numeroFactura ?? <span className="text-muted-foreground">—</span>}</TableCell>
              <TableCell className="text-right tabular-nums">
                {lote.costoUnitario != null ? formatCOP(lote.costoUnitario) : <span className="text-muted-foreground">—</span>}
              </TableCell>
              <TableCell className="whitespace-nowrap">{formatShortDate(lote.fechaIngreso)}</TableCell>
              {can("products.update") && (
                <TableCell className="text-right">
                  {lote.cantidadDisponible > 0 && (
                    <Button
                      variant={lote.estado === "Vencido" ? "destructive" : "ghost"}
                      size="sm"
                      onClick={() =>
                        setBaja({
                          productId,
                          lotId: lote.id,
                          productoNombre,
                          numeroLote: lote.numeroLote,
                          cantidad: lote.cantidadDisponible,
                          unidadMedida,
                          vencido: lote.estado === "Vencido",
                        })
                      }
                      aria-label={`Dar de baja el lote ${lote.numeroLote ?? "sin número"}`}
                    >
                      Dar de baja
                    </Button>
                  )}
                </TableCell>
              )}
            </MotionTableRow>
          ))}
        </TableBody>
      </Table>
      {baja && <WriteOffLotDialog target={baja} onClose={() => setBaja(null)} />}
    </ResponsiveTable>
  );
}

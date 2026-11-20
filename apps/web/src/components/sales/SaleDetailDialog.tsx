// ============================================================
// SaleDetailDialog.tsx — Detalle de una venta
//
// Líneas con base/IVA/total, pagos (los anulados tachados, con el paquete al que
// se aplicó cada abono), estado de la factura electrónica y acciones según el
// estado: abonar (con saldo), anular (confirmada), continuar o descartar (borrador).
// ============================================================
import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { Ban, Pencil, Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ConfirmDeleteButton } from "@/components/shared/ConfirmDeleteButton";
import { FormDialog } from "@/components/shared/FormDialog";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { SalePaymentDialog } from "@/components/sales/SalePaymentDialog";
import { VoidSaleDialog } from "@/components/sales/VoidSaleDialog";
import { useDeleteDraftSale, useSale } from "@/api/sales.api";
import { usePermissions } from "@/hooks/use-permissions";
import { formatCOP } from "@/lib/format-cop";
import { formatShortDate } from "@/lib/format-platform";
import { cn } from "@/lib/utils";
import { useToastStore } from "@/stores/toast.store";
import { SALE_LINE_LABELS, SALE_STATUS_BADGE } from "@/types/sale";
import { SaleInvoices } from "@/components/invoices/SaleInvoices";

export function SaleDetailDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: sale, isLoading } = useSale(id);
  const { can } = usePermissions();
  const [paying, setPaying] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const discard = useDeleteDraftSale();

  const confirmada = sale?.estado === "Confirmada";
  const borrador = sale?.estado === "Borrador";

  return (
    <>
      <FormDialog
        open
        onOpenChange={(o) => !o && onClose()}
        title={sale ? `Venta #${sale.numero}` : "Venta"}
        description={sale ? `${sale.cliente} · ${formatShortDate(sale.fecha)} · ${sale.branchNombre}` : undefined}
        cancelLabel="Cerrar"
        className="sm:max-w-3xl"
        actions={
          sale && (
            <>
              {borrador && (
                <Button asChild>
                  <Link to="/sales/new" search={{ draftId: sale.id }}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Continuar
                  </Link>
                </Button>
              )}
              {confirmada && sale.saldo > 0 && can("sales.create") && (
                <Button onClick={() => setPaying(true)}>
                  <Plus className="mr-2 h-4 w-4" />
                  Registrar abono
                </Button>
              )}
              {confirmada && can("sales.void") && (
                <Button variant="destructive" onClick={() => setVoiding(true)}>
                  <Ban className="mr-2 h-4 w-4" />
                  Anular
                </Button>
              )}
            </>
          )
        }
      >
        {isLoading || !sale ? (
          <Skeleton className="h-64 w-full" />
        ) : (
          <div className="space-y-5 pt-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={SALE_STATUS_BADGE[sale.estado]}>{sale.estado}</Badge>
              {confirmada && (
                <Badge variant={sale.saldo > 0 ? "warning" : "success"}>
                  {sale.saldo > 0 ? `Saldo ${formatCOP(sale.saldo)}` : "Pagada"}
                </Badge>
              )}
              {sale.patientId && (
                <Link to="/patients/$id" params={{ id: sale.patientId }} className="text-sm text-primary hover:underline">
                  Ver ficha del paciente
                </Link>
              )}
              <span className="text-sm text-muted-foreground">Vendió {sale.vendedor}</span>
            </div>

            {sale.estado === "Anulada" && (
              <p className="rounded-md bg-muted p-3 text-sm">
                Anulada el {formatShortDate(sale.fechaAnulacion)} por {sale.anuladaPor ?? "—"}: {sale.motivoAnulacion}. El inventario, los
                paquetes y los pagos se revirtieron.
              </p>
            )}

            <ResponsiveTable>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Ítem</TableHead>
                    <TableHead className="text-right">Cant.</TableHead>
                    <TableHead className="text-right">Precio</TableHead>
                    <TableHead className="text-right">Desc.</TableHead>
                    <TableHead className="text-right">IVA</TableHead>
                    <TableHead className="text-right">Total</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sale.lineas.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>
                        <span className="font-medium">{l.descripcion}</span>
                        <div className="text-xs text-muted-foreground">{SALE_LINE_LABELS[l.tipo]}</div>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{l.cantidad}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatCOP(l.precioUnitario)}</TableCell>
                      <TableCell className="text-right tabular-nums">{l.descuento ? formatCOP(l.descuento) : "—"}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatCOP(l.iva)} <span className="text-xs text-muted-foreground">({l.ivaPorcentaje} %)</span>
                      </TableCell>
                      <TableCell className="text-right font-medium tabular-nums">{formatCOP(l.total)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </ResponsiveTable>

            <dl className="ml-auto grid max-w-xs grid-cols-2 gap-x-4 gap-y-1 text-sm">
              <dt className="text-muted-foreground">Subtotal</dt>
              <dd className="text-right tabular-nums">{formatCOP(sale.subtotal)}</dd>
              <dt className="text-muted-foreground">IVA</dt>
              <dd className="text-right tabular-nums">{formatCOP(sale.impuestos)}</dd>
              <dt className="font-medium">Total</dt>
              <dd className="text-right font-heading text-lg font-bold tabular-nums">{formatCOP(sale.total)}</dd>
              {confirmada && (
                <>
                  <dt className="text-muted-foreground">Pagado</dt>
                  <dd className="text-right tabular-nums">{formatCOP(sale.totalPagado)}</dd>
                </>
              )}
            </dl>

            <section>
              <h3 className="mb-2 font-heading text-sm font-semibold">Pagos</h3>
              {sale.pagos.length === 0 ? (
                <p className="text-sm text-muted-foreground">Sin pagos.</p>
              ) : (
                <ul className="divide-y rounded-lg border">
                  {sale.pagos.map((p) => (
                    <li key={p.id} className={cn("flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm", p.anulado && "text-muted-foreground line-through")}>
                      <span>
                        {formatShortDate(p.fechaPago)} · {p.metodoPago}
                        {p.referencia && ` · ${p.referencia}`}
                        {p.aplicadoA && <span className="text-muted-foreground"> · a {p.aplicadoA}</span>}
                      </span>
                      <span className="font-medium tabular-nums">
                        {formatCOP(p.monto)}
                        {p.anulado && <span className="ml-1 no-underline">(anulado)</span>}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <SaleInvoices sale={sale} />

            {sale.observacion && <p className="text-sm text-muted-foreground">Observación: {sale.observacion}</p>}

            {borrador && (
              <ConfirmDeleteButton
                label="Descartar borrador"
                title={`¿Descartar el borrador #${sale.numero}?`}
                description="No tuvo efectos: no movió inventario ni recibió pagos."
                pending={discard.isPending}
                onConfirm={async () => {
                  await discard.mutateAsync(sale.id);
                  useToastStore.success("Borrador descartado");
                  onClose();
                }}
              />
            )}
          </div>
        )}
      </FormDialog>

      {paying && sale && <SalePaymentDialog sale={sale} onClose={() => setPaying(false)} />}
      {voiding && sale && <VoidSaleDialog sale={sale} onClose={() => setVoiding(false)} />}
    </>
  );
}

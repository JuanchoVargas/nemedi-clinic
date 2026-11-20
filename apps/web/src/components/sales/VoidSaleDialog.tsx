// ============================================================
// VoidSaleDialog.tsx — Anular una venta (con motivo)
//
// Explica qué se va a revertir antes de hacerlo y, después, dice qué se revirtió
// (el backend lo devuelve contado). Anular dos veces no revierte dos veces.
// ============================================================
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { FormDialog } from "@/components/shared/FormDialog";
import { useVoidSale } from "@/api/sales.api";
import { formatCOP } from "@/lib/format-cop";
import { useToastStore } from "@/stores/toast.store";
import type { Sale } from "@/types/sale";

export function VoidSaleDialog({ sale, onClose }: { sale: Sale; onClose: () => void }) {
  const voidSale = useVoidSale();
  const [motivo, setMotivo] = useState("");
  const tieneProductos = sale.lineas.some((l) => l.tipo === "Producto");
  const tienePaquetes = sale.lineas.some((l) => l.tipo === "Paquete");

  const onConfirm = async () => {
    try {
      const r = await voidSale.mutateAsync({ id: sale.id, motivo: motivo.trim() });
      const partes = [
        r.movimientosRevertidos > 0 && "el inventario volvió a sus lotes",
        r.paquetesRevertidos > 0 && `${r.paquetesRevertidos} paquete(s) retirado(s)`,
        r.pagosAnulados > 0 && `${r.pagosAnulados} pago(s) anulado(s)`,
        r.devolucionEfectivo > 0 && `devolución de ${formatCOP(r.devolucionEfectivo)} en tu caja`,
        r.devolucionFueraDeCaja > 0 && `${formatCOP(r.devolucionFueraDeCaja)} en efectivo a devolver fuera de caja`,
      ].filter(Boolean);
      useToastStore.success(`Venta #${sale.numero} anulada`, partes.length ? `Se revirtió: ${partes.join(", ")}.` : undefined);
      onClose();
    } catch {
      // toast global (p. ej. 409: el paquete ya tiene sesiones realizadas)
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title={`Anular la venta #${sale.numero}`}
      description={`${sale.cliente} · ${formatCOP(sale.total)}`}
      dirty={motivo.length > 0}
      actions={
        <Button variant="destructive" disabled={!motivo.trim() || voidSale.isPending} onClick={() => void onConfirm()}>
          {voidSale.isPending ? "Anulando..." : "Anular venta"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
          {tieneProductos && <li>Los productos vuelven al inventario, al mismo lote del que salieron.</li>}
          {tienePaquetes && <li>Los paquetes se retiran de la ficha del paciente (no se puede si ya tienen sesiones realizadas).</li>}
          <li>Los pagos quedan anulados. El efectivo cobrado en una caja ya cerrada sale como devolución de tu caja abierta.</li>
        </ul>
        <div className="space-y-1.5">
          <Label htmlFor="motivo-anulacion">Motivo *</Label>
          <Textarea id="motivo-anulacion" rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="La clienta desistió, error de digitación..." />
        </div>
      </div>
    </FormDialog>
  );
}

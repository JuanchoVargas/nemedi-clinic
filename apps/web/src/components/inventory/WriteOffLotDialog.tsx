// ============================================================
// WriteOffLotDialog.tsx — Dar de baja lo que queda de un lote
//
// Para lotes vencidos (o dañados, retirados): la existencia sale del inventario
// con un movimiento "Baja" y el motivo, que el reporte para la Secretaría de Salud
// muestra aparte del consumo. El lote no se borra: sigue siendo parte del respaldo.
// ============================================================
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { FormDialog } from "@/components/shared/FormDialog";
import { useWriteOffLot } from "@/api/inventory.api";
import { useToastStore } from "@/stores/toast.store";

const MOTIVOS = ["Vencimiento", "Producto dañado", "Retiro del INVIMA", "Pérdida"];

export interface WriteOffTarget {
  productId: string;
  lotId: string;
  productoNombre: string;
  numeroLote?: string | null;
  cantidad: number;
  unidadMedida: string;
  vencido: boolean;
}

export function WriteOffLotDialog({ target, onClose }: { target: WriteOffTarget; onClose: () => void }) {
  const writeOff = useWriteOffLot(target.productId);
  const [motivo, setMotivo] = useState(target.vencido ? "Vencimiento" : "");

  const onConfirm = async () => {
    try {
      await writeOff.mutateAsync({ lotId: target.lotId, motivo: motivo.trim() });
      useToastStore.success("Lote dado de baja", `${target.productoNombre} · ${target.cantidad.toLocaleString("es-CO")} ${target.unidadMedida}`);
      onClose();
    } catch {
      // toast global
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Dar de baja el lote"
      description={`${target.productoNombre} · lote ${target.numeroLote ?? "sin número"} · quedan ${target.cantidad.toLocaleString("es-CO")} ${target.unidadMedida}`}
      actions={
        <Button variant="destructive" disabled={!motivo.trim() || writeOff.isPending} onClick={() => void onConfirm()}>
          {writeOff.isPending ? "Dando de baja..." : "Dar de baja"}
        </Button>
      }
    >
      <div className="space-y-3 pt-1">
        <p className="text-sm text-muted-foreground">
          Toda la existencia del lote sale del inventario. Queda registrada como baja (no como consumo) en los movimientos y en el reporte para la
          Secretaría de Salud.
        </p>
        <Label htmlFor="motivo-baja">Motivo *</Label>
        <div className="flex flex-wrap gap-2">
          {MOTIVOS.map((m) => (
            <Button key={m} type="button" variant={motivo === m ? "default" : "outline"} size="sm" className="h-7 text-xs" onClick={() => setMotivo(m)}>
              {m}
            </Button>
          ))}
        </div>
        <Input id="motivo-baja" value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Otro motivo" />
      </div>
    </FormDialog>
  );
}

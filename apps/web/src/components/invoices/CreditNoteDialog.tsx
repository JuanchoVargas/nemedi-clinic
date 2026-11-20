// ============================================================
// CreditNoteDialog.tsx — Nota crédito desde una factura emitida
//
// Anulación: toda la factura (solo si no tiene devoluciones). Devolución: se elige
// cuánto de cada línea, hasta lo que queda sin acreditar. El backend recalcula base
// e IVA de cada línea; aquí solo se muestra el total aproximado de lo elegido.
// Anular la venta desde Ventas hace la nota de anulación sola.
// ============================================================
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { FormDialog } from "@/components/shared/FormDialog";
import { SegmentedControl } from "@/components/shared/SegmentedControl";
import { reportInvoiceResult } from "@/lib/invoice-result";
import { useCreditNote, useInvoice } from "@/api/invoices.api";
import { formatCOP } from "@/lib/format-cop";
import type { Invoice } from "@/types/invoice";

type Tipo = "Anulacion" | "Devolucion";

export function CreditNoteDialog({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  // El listado no trae las líneas: el detalle sí (con lo ya devuelto)
  const { data: detalle } = useInvoice(invoice.id);
  const creditNote = useCreditNote();
  const tieneDevoluciones = invoice.totalNotasCredito > 0;
  const [tipo, setTipo] = useState<Tipo>(tieneDevoluciones ? "Devolucion" : "Anulacion");
  const [motivo, setMotivo] = useState("");
  const [cantidades, setCantidades] = useState<Record<number, number>>({});

  const lineas = detalle?.lineas ?? [];
  const elegidas = Object.entries(cantidades).filter(([, c]) => c > 0).map(([i, c]) => ({ indice: Number(i), cantidad: c }));
  const valido = motivo.trim().length > 0 && (tipo === "Anulacion" || elegidas.length > 0);

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title={`Nota crédito de la factura ${invoice.numero}`}
      description={`${invoice.clienteNombre} · ${formatCOP(invoice.total)}`}
      dirty={motivo.length > 0 || elegidas.length > 0}
      className="sm:max-w-2xl"
      actions={
        <Button
          variant="destructive"
          disabled={!valido || creditNote.isPending}
          onClick={async () => {
            try {
              const nota = await creditNote.mutateAsync({
                id: invoice.id,
                body: { tipo, motivo: motivo.trim(), lineas: tipo === "Devolucion" ? elegidas : [] },
              });
              reportInvoiceResult(nota);
              if (nota.estado !== "Rechazada") onClose();
            } catch {
              // toast global
            }
          }}
        >
          {creditNote.isPending ? "Emitiendo..." : "Emitir nota crédito"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        {!tieneDevoluciones && (
          <SegmentedControl<Tipo>
            value={tipo}
            onChange={setTipo}
            options={[
              { value: "Anulacion", label: "Anular toda la factura" },
              { value: "Devolucion", label: "Devolución parcial" },
            ]}
            label="Tipo de nota crédito"
          />
        )}
        {tipo === "Anulacion" ? (
          <p className="text-sm text-muted-foreground">
            La factura queda anulada ante la DIAN. Esto no anula la venta: para revertir inventario, paquetes y pagos, anúlala desde Ventas (emite esta
            misma nota sola).
          </p>
        ) : (
          <ul className="divide-y rounded-lg border">
            {lineas.map((l) => {
              const disponible = l.cantidad - l.cantidadDevuelta;
              return (
                <li key={l.indice} className="flex flex-wrap items-center justify-between gap-3 px-3 py-2 text-sm">
                  <div className="min-w-0">
                    <div className="font-medium">{l.descripcion}</div>
                    <div className="text-xs text-muted-foreground">
                      {l.cantidad} × {formatCOP(l.precioUnitario)} · IVA {l.ivaPorcentaje} %
                      {l.cantidadDevuelta > 0 && ` · ya devuelto ${l.cantidadDevuelta}`}
                    </div>
                  </div>
                  <Input
                    type="number"
                    aria-label={`Cantidad a devolver de ${l.descripcion}`}
                    className="w-24"
                    min={0}
                    max={disponible}
                    step="any"
                    disabled={disponible <= 0}
                    value={cantidades[l.indice] ?? ""}
                    placeholder="0"
                    onChange={(e) => {
                      const v = e.target.value === "" ? 0 : Math.min(Math.max(e.target.valueAsNumber || 0, 0), disponible);
                      setCantidades((c) => ({ ...c, [l.indice]: v }));
                    }}
                  />
                </li>
              );
            })}
          </ul>
        )}
        <div className="space-y-1.5">
          <Label htmlFor="motivo-nota">Motivo *</Label>
          <Textarea id="motivo-nota" rows={2} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Devolución del producto, error en el precio..." />
        </div>
      </div>
    </FormDialog>
  );
}

// ============================================================
// CashDialogs.tsx — Abrir caja, registrar ingreso/egreso y arqueo con cierre
//
// Se montan solo mientras están abiertos: los valores iniciales salen de las props.
// ============================================================
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { CurrencyInput } from "@/components/ui/CurrencyInput";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { FormDialog } from "@/components/shared/FormDialog";
import { SegmentedControl } from "@/components/shared/SegmentedControl";
import { DifferenceBadge } from "@/components/cash/CashPanels";
import { useAddCashMovement, useCloseCash, useOpenCash } from "@/api/cash.api";
import { formatCOP } from "@/lib/format-cop";
import { useToastStore } from "@/stores/toast.store";
import type { Branch } from "@/types/branch";
import type { CashSession } from "@/types/cash";

export function OpenCashDialog({ branches, onClose }: { branches: Branch[]; onClose: () => void }) {
  const open = useOpenCash();
  const [branchId, setBranchId] = useState("");
  const [monto, setMonto] = useState(0);
  const [observacion, setObservacion] = useState("");

  const onSubmit = async () => {
    try {
      await open.mutateAsync({ branchId: branchId || undefined, montoApertura: monto, observacion: observacion.trim() || undefined });
      useToastStore.success("Caja abierta", `Base ${formatCOP(monto)}`);
      onClose();
    } catch {
      // toast global (409: ya tienes una caja abierta)
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Abrir caja"
      description="La base es el efectivo con el que empiezas el día. Los pagos que registres quedan en esta caja hasta cerrarla."
      dirty={monto > 0 || !!observacion}
      actions={
        <Button onClick={() => void onSubmit()} disabled={open.isPending}>
          {open.isPending ? "Abriendo..." : "Abrir caja"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        {branches.length > 1 && (
          <div className="space-y-1.5">
            <Label>Sede</Label>
            <Select value={branchId || "auto"} onValueChange={(v) => setBranchId(v === "auto" ? "" : v)}>
              <SelectTrigger className="w-full" aria-label="Sede">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="auto">Mi sede</SelectItem>
                {branches.map((b) => (
                  <SelectItem key={b.id} value={b.id}>
                    {b.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
        <div className="space-y-1.5">
          <Label htmlFor="caja-base">Base en efectivo</Label>
          <CurrencyInput id="caja-base" value={monto} onChange={setMonto} placeholder="0" />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="caja-obs">Observación (opcional)</Label>
          <Textarea id="caja-obs" rows={2} value={observacion} onChange={(e) => setObservacion(e.target.value)} />
        </div>
      </div>
    </FormDialog>
  );
}

export function CashMovementDialog({ session, onClose }: { session: CashSession; onClose: () => void }) {
  const add = useAddCashMovement();
  const [tipo, setTipo] = useState<"Egreso" | "Ingreso">("Egreso");
  const [concepto, setConcepto] = useState("");
  const [monto, setMonto] = useState(0);
  const excede = tipo === "Egreso" && monto > session.resumen.efectivoEsperado;

  const onSubmit = async () => {
    try {
      await add.mutateAsync({ id: session.id, body: { tipo, concepto: concepto.trim(), monto } });
      useToastStore.success(tipo === "Egreso" ? "Egreso registrado" : "Ingreso registrado", `${concepto.trim()} · ${formatCOP(monto)}`);
      onClose();
    } catch {
      // toast global
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Movimiento de caja"
      description="Ingresos y egresos en efectivo que no son pagos de pacientes: compras menores, base adicional, devoluciones."
      dirty={!!concepto || monto > 0}
      actions={
        <Button onClick={() => void onSubmit()} disabled={!concepto.trim() || monto <= 0 || excede || add.isPending}>
          {add.isPending ? "Guardando..." : "Registrar"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        <SegmentedControl<"Egreso" | "Ingreso">
          label="Tipo de movimiento"
          value={tipo}
          onChange={setTipo}
          options={[
            { value: "Egreso", label: "Egreso" },
            { value: "Ingreso", label: "Ingreso" },
          ]}
        />
        <div className="space-y-1.5">
          <Label htmlFor="mov-concepto">Concepto</Label>
          <Input id="mov-concepto" value={concepto} onChange={(e) => setConcepto(e.target.value)} placeholder={tipo === "Egreso" ? "Compra de café y agua" : "Base adicional"} />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="mov-monto">Monto</Label>
          <CurrencyInput id="mov-monto" value={monto} onChange={setMonto} placeholder="0" />
          {excede && <p className="text-xs text-destructive">Supera el efectivo en caja ({formatCOP(session.resumen.efectivoEsperado)}).</p>}
        </div>
      </div>
    </FormDialog>
  );
}

export function CloseCashDialog({ session, onClose }: { session: CashSession; onClose: () => void }) {
  const close = useCloseCash();
  const [contado, setContado] = useState(0);
  const [tocado, setTocado] = useState(false);
  const [observacion, setObservacion] = useState("");
  const esperado = session.resumen.efectivoEsperado;
  const diferencia = contado - esperado;

  const onSubmit = async () => {
    try {
      const cerrada = await close.mutateAsync({ id: session.id, body: { efectivoContado: contado, observacion: observacion.trim() || undefined } });
      const d = cerrada.diferencia ?? 0;
      useToastStore.success("Caja cerrada", d === 0 ? "El arqueo cuadra." : d < 0 ? `Faltan ${formatCOP(-d)}.` : `Sobran ${formatCOP(d)}.`);
      onClose();
    } catch {
      // toast global
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Arqueo y cierre de caja"
      description="Cuenta el efectivo del cajón y escríbelo. Al cerrar, los totales quedan congelados."
      dirty={tocado || !!observacion}
      actions={
        <Button onClick={() => void onSubmit()} disabled={!tocado || close.isPending}>
          {close.isPending ? "Cerrando..." : "Cerrar caja"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        <dl className="grid grid-cols-2 gap-y-1 rounded-lg bg-muted/50 p-3 text-sm">
          <dt className="text-muted-foreground">Base</dt>
          <dd className="text-right tabular-nums">{formatCOP(session.resumen.montoApertura)}</dd>
          <dt className="text-muted-foreground">+ Efectivo recibido</dt>
          <dd className="text-right tabular-nums">{formatCOP(session.resumen.totalEfectivo)}</dd>
          <dt className="text-muted-foreground">+ Ingresos</dt>
          <dd className="text-right tabular-nums">{formatCOP(session.resumen.totalIngresos)}</dd>
          <dt className="text-muted-foreground">− Egresos</dt>
          <dd className="text-right tabular-nums">{formatCOP(session.resumen.totalEgresos)}</dd>
          <dt className="border-t pt-1 font-medium">Esperado</dt>
          <dd className="border-t pt-1 text-right font-semibold tabular-nums">{formatCOP(esperado)}</dd>
        </dl>
        <div className="space-y-1.5">
          <Label htmlFor="caja-contado">Efectivo contado</Label>
          <CurrencyInput
            id="caja-contado"
            value={contado}
            onChange={(v) => {
              setContado(v);
              setTocado(true);
            }}
            placeholder="0"
          />
        </div>
        {tocado && (
          <p className="flex items-center justify-between text-sm">
            <span>Diferencia contra lo esperado</span>
            <DifferenceBadge value={diferencia} />
          </p>
        )}
        <div className="space-y-1.5">
          <Label htmlFor="caja-cierre-obs">Observación (opcional)</Label>
          <Textarea id="caja-cierre-obs" rows={2} value={observacion} onChange={(e) => setObservacion(e.target.value)} placeholder="Explica la diferencia si la hay" />
        </div>
      </div>
    </FormDialog>
  );
}

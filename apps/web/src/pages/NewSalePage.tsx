// ============================================================
// NewSalePage.tsx — Nueva venta (/sales/new[?patientId=][&draftId=])
//
// Izquierda: cliente (paciente o mostrador), buscador del catálogo y líneas.
// Derecha: totales (los calcula el backend con POST /sales/quote), pagos en uno
// o varios medios y los botones "Guardar borrador" / "Confirmar venta".
// Con ?draftId= retoma un borrador guardado antes.
//
// EQUIVALENTE A: la pantalla de facturación de un POS en Vue, con el carrito en
// un composable (aquí useSaleDraft) y la vista solo pintando.
// ============================================================
import { useState } from "react";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { AlertTriangle, ArrowLeft, Banknote, Plus, Save, Trash2 } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { CurrencyInput } from "@/components/ui/CurrencyInput";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { PageContainer } from "@/components/shared/PageContainer";
import { PatientCombobox } from "@/components/shared/PatientCombobox";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { SegmentedControl } from "@/components/shared/SegmentedControl";
import { EmptyState } from "@/components/shared/EmptyState";
import { CatalogSearch } from "@/components/sales/CatalogSearch";
import { useSale, useSaleQuote, useSaveSale } from "@/api/sales.api";
import { useCurrentCash } from "@/api/cash.api";
import { useBranches } from "@/api/branches.api";
import { usePatient } from "@/api/patients.api";
import { draftFromSale, useSaleDraft, type DraftLine } from "@/hooks/use-sale-draft";
import { useDebounce } from "@/hooks/use-debounce";
import { formatCOP } from "@/lib/format-cop";
import { useToastStore } from "@/stores/toast.store";
import type { PatientSummary } from "@/types/patient";
import { PAYMENT_METHODS, SALE_LINE_LABELS, type Sale } from "@/types/sale";

type ClientMode = "paciente" | "mostrador";

export function NewSalePage() {
  const { draftId, patientId } = useSearch({ strict: false }) as { draftId?: string; patientId?: string };
  const { data: draft, isLoading } = useSale(draftId);

  if (draftId && isLoading) {
    return (
      <PageContainer>
        <Skeleton className="h-96 w-full" />
      </PageContainer>
    );
  }
  // key: al cambiar de borrador el formulario se remonta con sus valores iniciales (sin efectos de reset)
  return <SaleForm key={draft?.id ?? "nueva"} draft={draft?.estado === "Borrador" ? draft : undefined} presetPatientId={patientId} />;
}

function SaleForm({ draft, presetPatientId }: { draft?: Sale; presetPatientId?: string }) {
  const navigate = useNavigate();
  const save = useSaveSale();
  const { data: cash } = useCurrentCash();
  const { data: branches } = useBranches();

  const sale = useSaleDraft(draft ? draftFromSale(draft) : []);
  const debouncedLines = useDebounce(sale.lineRequests, 250);
  const { data: quote, isFetching: quoting, isError: quoteError } = useSaleQuote(debouncedLines);

  const initialPatientId = draft?.patientId ?? presetPatientId;
  const { data: presetPatient } = usePatient(initialPatientId ?? undefined);
  const [pickedPatient, setPatient] = useState<PatientSummary | null | undefined>(undefined);
  const patient = pickedPatient === undefined ? (presetPatient ?? null) : pickedPatient;
  const [mode, setMode] = useState<ClientMode>(draft ? (draft.esMostrador ? "mostrador" : "paciente") : "paciente");
  const [clienteNombre, setClienteNombre] = useState(draft?.esMostrador ? (draft.cliente === "Cliente de mostrador" ? "" : draft.cliente) : "");
  const [clienteDocumento, setClienteDocumento] = useState(draft?.esMostrador ? (draft.clienteDocumento ?? "") : "");
  const [branchId, setBranchId] = useState<string>(draft?.branchId ?? "");
  const [observacion, setObservacion] = useState(draft?.observacion ?? "");

  // Los totales valen solo cuando la cotización corresponde a las líneas actuales
  const quoteVigente = !!quote && !quoting && debouncedLines === sale.lineRequests && !quoteError;
  const total = quote?.total ?? 0;
  const porCobrar = Math.max(0, total - sale.totalPagos);
  const excedido = sale.totalPagos > total;
  const falta = mode === "paciente" && !patient;
  const paqueteSinPaciente = sale.tienePaquete && (mode !== "paciente" || !patient);

  const submit = async (confirmar: boolean) => {
    try {
      const saved = await save.mutateAsync({
        id: draft?.id,
        body: {
          branchId: branchId || undefined,
          patientId: mode === "paciente" ? patient?.id : undefined,
          clienteNombre: mode === "mostrador" ? clienteNombre.trim() || undefined : undefined,
          clienteDocumento: mode === "mostrador" ? clienteDocumento.trim() || undefined : undefined,
          observacion: observacion.trim() || undefined,
          lineas: sale.lineRequests,
          pagos: confirmar ? sale.paymentRequests : [],
          confirmar,
        },
      });
      useToastStore.success(
        confirmar ? `Venta #${saved.numero} confirmada` : `Borrador #${saved.numero} guardado`,
        confirmar && saved.saldo > 0 ? `Queda un saldo de ${formatCOP(saved.saldo)} por cobrar.` : undefined,
      );
      void navigate({ to: "/sales", search: { saleId: saved.id } });
    } catch {
      // toast global (stock insuficiente, pagos de más, lote vencido…)
    }
  };

  const puedeGuardar = sale.lines.length > 0 && quoteVigente && !falta && !paqueteSinPaciente && !save.isPending;

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <Button asChild variant="ghost" size="sm" className="-ml-2 mb-1">
            <Link to="/sales">
              <ArrowLeft className="mr-1 h-4 w-4" />
              Ventas
            </Link>
          </Button>
          <h1 className="text-3xl font-bold">{draft ? `Borrador #${draft.numero}` : "Nueva venta"}</h1>
          <p className="text-muted-foreground">Productos, procedimientos y paquetes en una sola venta, con uno o varios medios de pago.</p>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          {/* ── Cliente ── */}
          <Card>
            <CardHeader>
              <CardTitle>Cliente</CardTitle>
              <CardDescription>Un paquete queda en la ficha del paciente; lo demás también se puede vender en mostrador.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <SegmentedControl<ClientMode>
                label="Tipo de cliente"
                value={mode}
                onChange={setMode}
                options={[
                  { value: "paciente", label: "Paciente" },
                  { value: "mostrador", label: "Mostrador" },
                ]}
              />
              {mode === "paciente" ? (
                <PatientCombobox value={patient} onChange={setPatient} />
              ) : (
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-1.5">
                    <Label htmlFor="cliente-nombre">Nombre (opcional)</Label>
                    <Input id="cliente-nombre" value={clienteNombre} onChange={(e) => setClienteNombre(e.target.value)} placeholder="Cliente de mostrador" />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="cliente-doc">Documento (opcional)</Label>
                    <Input id="cliente-doc" value={clienteDocumento} onChange={(e) => setClienteDocumento(e.target.value)} placeholder="Para la factura" />
                  </div>
                </div>
              )}
              {branches && branches.length > 1 && (
                <div className="space-y-1.5">
                  <Label>Sede</Label>
                  <Select value={branchId || "auto"} onValueChange={(v) => setBranchId(v === "auto" ? "" : v)}>
                    <SelectTrigger className="w-full sm:w-72" aria-label="Sede">
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
            </CardContent>
          </Card>

          {/* ── Catálogo ── */}
          <Card>
            <CardHeader>
              <CardTitle>Agregar a la venta</CardTitle>
              <CardDescription>Los precios del catálogo son sin IVA; el IVA de cada ítem lo decide la configuración de la clínica.</CardDescription>
            </CardHeader>
            <CardContent>
              <CatalogSearch onAdd={sale.addItem} onAddOther={sale.addOther} />
            </CardContent>
          </Card>

          {/* ── Líneas ── */}
          <Card>
            <CardHeader>
              <CardTitle>Detalle</CardTitle>
            </CardHeader>
            <CardContent>
              {sale.lines.length === 0 ? (
                <EmptyState illustration="inventory" title="La venta está vacía" description="Busca arriba un producto, un procedimiento o un paquete y haz clic para agregarlo." />
              ) : (
                <ResponsiveTable>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Ítem</TableHead>
                        <TableHead className="w-24">Cantidad</TableHead>
                        <TableHead className="w-32">Descuento</TableHead>
                        <TableHead className="text-right">IVA</TableHead>
                        <TableHead className="text-right">Total</TableHead>
                        <TableHead className="w-12 text-right">
                          <span className="sr-only">Quitar</span>
                        </TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {sale.lines.map((line, i) => (
                        <LineRow
                          key={line.key}
                          line={line}
                          quoted={quoteVigente ? quote?.lineas[i] : undefined}
                          onChange={(patch) => sale.updateLine(line.key, patch)}
                          onRemove={() => sale.removeLine(line.key)}
                        />
                      ))}
                    </TableBody>
                  </Table>
                </ResponsiveTable>
              )}
              {paqueteSinPaciente && (
                <p className="mt-3 flex items-center gap-2 text-sm text-destructive">
                  <AlertTriangle className="h-4 w-4 shrink-0" />
                  Un paquete necesita paciente: elige uno en "Cliente".
                </p>
              )}
            </CardContent>
          </Card>
        </div>

        {/* ── Resumen y pagos ── */}
        <div className="space-y-6 lg:sticky lg:top-20 lg:self-start">
          <Card>
            <CardHeader>
              <CardTitle>Resumen</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              <SummaryRow label="Subtotal (sin IVA)" value={quote?.subtotal} pending={!quoteVigente && sale.lines.length > 0} />
              {!!quote?.descuento && <SummaryRow label="Descuentos" value={-quote.descuento} pending={!quoteVigente} />}
              <SummaryRow label="IVA" value={quote?.impuestos} pending={!quoteVigente && sale.lines.length > 0} />
              <div className="flex items-center justify-between border-t pt-2">
                <span className="font-medium">Total</span>
                <span className="font-heading text-2xl font-bold tabular-nums" data-testid="sale-total">
                  {formatCOP(sale.lines.length > 0 ? total : 0)}
                </span>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Pagos</CardTitle>
              <CardDescription>Uno o varios medios. Si queda saldo, la venta sigue en cartera.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {!cash && (
                <p className="flex items-start gap-2 rounded-md bg-sand-soft p-2 text-xs text-sand-foreground dark:text-sand">
                  <Banknote className="mt-0.5 h-4 w-4 shrink-0" />
                  <span>
                    No tienes una caja abierta: los pagos no quedarán en ningún arqueo.{" "}
                    <Link to="/cash" className="font-medium underline">
                      Abrir caja
                    </Link>
                  </span>
                </p>
              )}
              {sale.payments.map((p) => (
                <div key={p.key} className="space-y-2 rounded-lg border p-3">
                  <div className="flex gap-2">
                    <Select value={p.metodoPago} onValueChange={(v) => sale.updatePayment(p.key, { metodoPago: v as (typeof PAYMENT_METHODS)[number] })}>
                      <SelectTrigger className="w-36" aria-label="Medio de pago">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {PAYMENT_METHODS.map((m) => (
                          <SelectItem key={m} value={m}>
                            {m}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <CurrencyInput value={p.monto} onChange={(v) => sale.updatePayment(p.key, { monto: v })} placeholder="0" className="min-w-0 flex-1" aria-label={`Monto en ${p.metodoPago}`} />
                    <Button type="button" variant="ghost" size="icon" onClick={() => sale.removePayment(p.key)} aria-label="Quitar pago">
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                  {p.metodoPago !== "Efectivo" && (
                    <Input
                      value={p.referencia}
                      onChange={(e) => sale.updatePayment(p.key, { referencia: e.target.value })}
                      placeholder={p.metodoPago === "Tarjeta" ? "Número del voucher" : "Número de la transferencia"}
                      aria-label="Referencia del pago"
                    />
                  )}
                </div>
              ))}
              <Button
                type="button"
                variant="outline"
                className="w-full"
                disabled={sale.lines.length === 0 || porCobrar <= 0}
                onClick={() => sale.addPayment(sale.payments.length === 0 ? "Efectivo" : "Tarjeta", porCobrar)}
              >
                <Plus className="mr-2 h-4 w-4" />
                {sale.payments.length === 0 ? "Registrar pago" : "Agregar otro medio"}
              </Button>

              <div className="space-y-1 border-t pt-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Pagado ahora</span>
                  <span className="tabular-nums">{formatCOP(sale.totalPagos)}</span>
                </div>
                <div className="flex justify-between font-medium">
                  <span>Queda por cobrar</span>
                  <span className="tabular-nums">{formatCOP(porCobrar)}</span>
                </div>
                {excedido && <p className="text-xs text-destructive">Los pagos superan el total de la venta.</p>}
              </div>
            </CardContent>
          </Card>

          <div className="space-y-1.5">
            <Label htmlFor="venta-obs">Observación (opcional)</Label>
            <Textarea id="venta-obs" rows={2} value={observacion} onChange={(e) => setObservacion(e.target.value)} />
          </div>

          <div className="flex flex-col gap-2">
            <Button size="lg" disabled={!puedeGuardar || excedido} onClick={() => void submit(true)}>
              {save.isPending ? "Guardando..." : `Confirmar venta · ${formatCOP(sale.lines.length > 0 ? total : 0)}`}
            </Button>
            <Button variant="outline" disabled={!puedeGuardar} onClick={() => void submit(false)}>
              <Save className="mr-2 h-4 w-4" />
              Guardar borrador
            </Button>
            {falta && sale.lines.length > 0 && (
              <p className="text-center text-xs text-muted-foreground">Elige el paciente o cambia a "Mostrador".</p>
            )}
          </div>
        </div>
      </div>
    </PageContainer>
  );
}

function LineRow({
  line,
  quoted,
  onChange,
  onRemove,
}: {
  line: DraftLine;
  quoted?: { ivaPorcentaje: number; iva: number; total: number; precioUnitario: number };
  onChange: (patch: Partial<DraftLine>) => void;
  onRemove: () => void;
}) {
  const soloUno = line.tipo === "Paquete";
  const entero = line.tipo !== "Producto" && line.tipo !== "Otro";
  const sinStock = line.tipo === "Producto" && line.stock != null && line.cantidad > line.stock;

  return (
    <TableRow>
      <TableCell className="max-w-64">
        <div className="flex items-center gap-2">
          <Badge variant="outline" className="shrink-0">
            {SALE_LINE_LABELS[line.tipo]}
          </Badge>
          <span className="truncate font-medium">{line.descripcion}</span>
        </div>
        <div className="text-xs text-muted-foreground">
          {quoted ? `${formatCOP(quoted.precioUnitario)} c/u sin IVA` : line.detalle}
          {sinStock && <span className="ml-1 text-destructive">· solo hay {line.stock}</span>}
        </div>
      </TableCell>
      <TableCell>
        <Input
          type="number"
          min={entero ? 1 : 0.01}
          step={entero ? 1 : 0.01}
          value={line.cantidad}
          disabled={soloUno}
          onChange={(e) => onChange({ cantidad: Math.max(0, Number(e.target.value)) })}
          className="w-20"
          aria-label={`Cantidad de ${line.descripcion}`}
        />
      </TableCell>
      <TableCell>
        <CurrencyInput value={line.descuento} onChange={(v) => onChange({ descuento: v })} placeholder="0" className="w-28" aria-label={`Descuento de ${line.descripcion}`} />
      </TableCell>
      <TableCell className="text-right whitespace-nowrap tabular-nums">
        {quoted ? (
          <>
            {formatCOP(quoted.iva)}
            <div className="text-xs text-muted-foreground">{quoted.ivaPorcentaje} %</div>
          </>
        ) : (
          <Skeleton className="ml-auto h-4 w-14" />
        )}
      </TableCell>
      <TableCell className="text-right font-medium whitespace-nowrap tabular-nums">
        {quoted ? formatCOP(quoted.total) : <Skeleton className="ml-auto h-4 w-16" />}
      </TableCell>
      <TableCell className="text-right">
        <Button type="button" variant="ghost" size="icon" onClick={onRemove} aria-label={`Quitar ${line.descripcion}`}>
          <Trash2 className="h-4 w-4" />
        </Button>
      </TableCell>
    </TableRow>
  );
}

function SummaryRow({ label, value, pending }: { label: string; value?: number; pending?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-muted-foreground">{label}</span>
      {pending ? <Skeleton className="h-4 w-20" /> : <span className="tabular-nums">{formatCOP(value ?? 0)}</span>}
    </div>
  );
}

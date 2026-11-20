// ============================================================
// CashPage.tsx — Caja (/cash)
//
// Arriba, mi caja: abrirla con una base, ver en vivo lo recibido por medio de
// pago, registrar ingresos/egresos, exportar el arqueo y cerrarla contando el
// efectivo. Abajo, el historial de cajas de la clínica (por sede y usuario) con
// su diferencia, exportable a Excel. Recepción y dueño.
// ============================================================
import { useState } from "react";
import { Banknote, Download, FileSpreadsheet, Lock, Loader2, MinusCircle, Unlock } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/shared/EmptyState";
import { FormDialog } from "@/components/shared/FormDialog";
import { PageContainer } from "@/components/shared/PageContainer";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { CashMovementsTable, CashSummaryCards, DifferenceBadge } from "@/components/cash/CashPanels";
import { CashMovementDialog, CloseCashDialog, OpenCashDialog } from "@/components/cash/CashDialogs";
import { downloadCashSession, downloadCashSessions, useCashSession, useCashSessions, useCurrentCash } from "@/api/cash.api";
import { useBranches } from "@/api/branches.api";
import { addDaysToLocalDate, toLocalDate } from "@/lib/dates";
import { formatCOP } from "@/lib/format-cop";
import { useToastStore } from "@/stores/toast.store";
import type { CashSession } from "@/types/cash";

type Dialog = "open" | "movement" | "close" | null;

export function CashPage() {
  const { data: current, isLoading } = useCurrentCash();
  const { data: branches } = useBranches();
  const [dialog, setDialog] = useState<Dialog>(null);
  const [detailId, setDetailId] = useState<string | null>(null);

  return (
    <PageContainer>
      <div className="mb-6">
        <h1 className="text-3xl font-bold">Caja</h1>
        <p className="text-muted-foreground">Apertura, pagos del día, movimientos, arqueo y cierre por sede y usuario.</p>
      </div>

      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : current ? (
        <OpenSession session={current} onMovement={() => setDialog("movement")} onClose={() => setDialog("close")} />
      ) : (
        <Card className="mb-8">
          <CardContent className="flex flex-col items-center gap-4 py-10 text-center">
            <span className="rounded-full bg-primary/10 p-4 text-primary">
              <Banknote className="h-8 w-8" />
            </span>
            <div>
              <p className="font-heading text-lg font-semibold">No tienes una caja abierta</p>
              <p className="text-sm text-muted-foreground">Ábrela al empezar el turno: los pagos que registres quedarán en ella hasta el arqueo.</p>
            </div>
            <Button onClick={() => setDialog("open")}>
              <Unlock className="mr-2 h-4 w-4" />
              Abrir caja
            </Button>
          </CardContent>
        </Card>
      )}

      <History onOpen={setDetailId} />

      {dialog === "open" && <OpenCashDialog branches={branches ?? []} onClose={() => setDialog(null)} />}
      {dialog === "movement" && current && <CashMovementDialog session={current} onClose={() => setDialog(null)} />}
      {dialog === "close" && current && <CloseCashDialog session={current} onClose={() => setDialog(null)} />}
      {detailId && <CashDetailDialog id={detailId} onClose={() => setDetailId(null)} />}
    </PageContainer>
  );
}

function OpenSession({ session, onMovement, onClose }: { session: CashSession; onMovement: () => void; onClose: () => void }) {
  return (
    <section className="mb-8 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="success">Abierta</Badge>
          <span className="text-sm text-muted-foreground">
            {session.branchNombre} · {session.usuario} · desde{" "}
            {new Date(session.fechaApertura).toLocaleString("es-CO", { dateStyle: "medium", timeStyle: "short" })}
          </span>
          {session.abiertaDesdeOtroDia && <Badge variant="warning">Abierta desde otro día: ciérrala</Badge>}
        </div>
        <div className="flex flex-wrap gap-2">
          <ExportButton id={session.id} />
          {session.puedeOperar && (
            <>
              <Button variant="outline" onClick={onMovement}>
                <MinusCircle className="mr-2 h-4 w-4" />
                Ingreso / egreso
              </Button>
              <Button onClick={onClose}>
                <Lock className="mr-2 h-4 w-4" />
                Arqueo y cierre
              </Button>
            </>
          )}
        </div>
      </div>
      <CashSummaryCards session={session} />
      <Card>
        <CardHeader>
          <CardTitle>Movimientos</CardTitle>
          <CardDescription>{session.resumen.cantidadPagos} pagos registrados en esta caja.</CardDescription>
        </CardHeader>
        <CardContent>
          <CashMovementsTable movements={session.movimientos} />
        </CardContent>
      </Card>
    </section>
  );
}

function ExportButton({ id, size = "default" }: { id: string; size?: "default" | "sm" }) {
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    try {
      await downloadCashSession(id);
    } catch (error) {
      useToastStore.report(error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Button variant="outline" size={size} onClick={() => void run()} disabled={busy} aria-label="Exportar arqueo a Excel">
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <FileSpreadsheet className="mr-2 h-4 w-4" />}
      Excel
    </Button>
  );
}

function History({ onOpen }: { onOpen: (id: string) => void }) {
  const [hasta, setHasta] = useState(() => toLocalDate(new Date()));
  const [desde, setDesde] = useState(() => addDaysToLocalDate(toLocalDate(new Date()), -30));
  const { data, isLoading } = useCashSessions(desde, hasta);
  const [busy, setBusy] = useState(false);

  const exportar = async () => {
    setBusy(true);
    try {
      await downloadCashSessions(desde, hasta);
    } catch (error) {
      useToastStore.report(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-end justify-between gap-3">
        <div>
          <CardTitle>Historial de cajas</CardTitle>
          <CardDescription>Todas las cajas de la clínica en el rango, con su diferencia de arqueo.</CardDescription>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div className="space-y-1">
            <Label htmlFor="cajas-desde" className="text-xs text-muted-foreground">Desde</Label>
            <Input id="cajas-desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} className="w-40" />
          </div>
          <div className="space-y-1">
            <Label htmlFor="cajas-hasta" className="text-xs text-muted-foreground">Hasta</Label>
            <Input id="cajas-hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} className="w-40" />
          </div>
          <Button variant="outline" onClick={() => void exportar()} disabled={busy}>
            <Download className="mr-2 h-4 w-4" />
            Excel
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <Skeleton className="h-32 w-full" />
        ) : !data || data.length === 0 ? (
          <EmptyState illustration="search" title="Sin cajas en ese rango" />
        ) : (
          <ResponsiveTable>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Apertura</TableHead>
                  <TableHead>Sede · usuario</TableHead>
                  <TableHead className="text-right">Recaudado</TableHead>
                  <TableHead className="text-right">Efectivo esperado</TableHead>
                  <TableHead>Arqueo</TableHead>
                  <TableHead className="text-right">Detalle</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="whitespace-nowrap">
                      {new Date(c.fechaApertura).toLocaleString("es-CO", { dateStyle: "medium", timeStyle: "short" })}
                    </TableCell>
                    <TableCell>
                      {c.branchNombre}
                      <div className="text-xs text-muted-foreground">{c.usuario}</div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatCOP(c.resumen.totalPagos)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatCOP(c.resumen.efectivoEsperado)}</TableCell>
                    <TableCell>{c.estado === "Abierta" ? <Badge variant="outline">Abierta</Badge> : <DifferenceBadge value={c.diferencia ?? 0} />}</TableCell>
                    <TableCell className="text-right">
                      <Button variant="outline" size="sm" onClick={() => onOpen(c.id)} aria-label={`Ver la caja de ${c.usuario}`}>
                        Ver
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ResponsiveTable>
        )}
      </CardContent>
    </Card>
  );
}

function CashDetailDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: session, isLoading } = useCashSession(id);
  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Detalle de caja"
      description={
        session
          ? `${session.branchNombre} · ${session.usuario} · ${new Date(session.fechaApertura).toLocaleDateString("es-CO", { dateStyle: "long" })}`
          : undefined
      }
      cancelLabel="Cerrar"
      className="sm:max-w-4xl"
      actions={session && <ExportButton id={session.id} />}
    >
      {isLoading || !session ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <div className="space-y-4 pt-1">
          <CashSummaryCards session={session} />
          {session.observacionCierre && <p className="text-sm text-muted-foreground">Observación de cierre: {session.observacionCierre}</p>}
          <CashMovementsTable movements={session.movimientos} />
        </div>
      )}
    </FormDialog>
  );
}

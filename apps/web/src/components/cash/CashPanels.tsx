// ============================================================
// CashPanels.tsx — Piezas de la caja: resumen del arqueo y tabla de movimientos
//
// Los números llegan calculados del backend (abierta: en vivo; cerrada: congelados
// al cerrar). Aquí solo se pintan.
// ============================================================
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/shared/EmptyState";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { formatCOP } from "@/lib/format-cop";
import { cn } from "@/lib/utils";
import type { CashMovement, CashSession } from "@/types/cash";

export function CashSummaryCards({ session }: { session: CashSession }) {
  const r = session.resumen;
  const cards = [
    { label: "Base de apertura", value: r.montoApertura },
    { label: "Efectivo recibido", value: r.totalEfectivo },
    { label: "Ingresos manuales", value: r.totalIngresos },
    { label: "Egresos", value: -r.totalEgresos },
  ];
  const otros = [
    { label: "Transferencias", value: r.totalTransferencia },
    { label: "Tarjeta", value: r.totalTarjeta },
    { label: "Otros medios", value: r.totalOtro },
  ];

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_auto]">
      <Card>
        <CardContent className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {cards.map((c) => (
            <div key={c.label}>
              <p className="text-xs text-muted-foreground">{c.label}</p>
              <p className={cn("font-heading text-xl font-semibold tabular-nums", c.value < 0 && "text-destructive")}>
                {c.value < 0 ? `−${formatCOP(-c.value)}` : formatCOP(c.value)}
              </p>
            </div>
          ))}
          <div className="border-t pt-3 sm:col-span-2 xl:col-span-4">
            <p className="text-xs text-muted-foreground">Otros medios (no están en el cajón)</p>
            <p className="text-sm">
              {otros.map((o) => `${o.label} ${formatCOP(o.value)}`).join(" · ")} · <strong>Total recaudado {formatCOP(r.totalPagos)}</strong>
            </p>
          </div>
        </CardContent>
      </Card>
      <Card className="lg:w-72">
        <CardContent className="space-y-3">
          <div>
            <p className="text-sm text-muted-foreground">Efectivo esperado en caja</p>
            <p className="font-heading text-3xl font-bold tabular-nums" data-testid="efectivo-esperado">
              {formatCOP(r.efectivoEsperado)}
            </p>
          </div>
          {session.estado === "Cerrada" && session.efectivoContado != null && (
            <div className="border-t pt-3 text-sm">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Contado</span>
                <span className="tabular-nums">{formatCOP(session.efectivoContado)}</span>
              </div>
              <div className="flex justify-between font-medium">
                <span>Diferencia</span>
                <DifferenceBadge value={session.diferencia ?? 0} />
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

/** Diferencia del arqueo: 0 cuadra; negativa falta; positiva sobra. */
export function DifferenceBadge({ value }: { value: number }) {
  if (value === 0) return <Badge variant="success">Cuadra</Badge>;
  return <Badge variant={value < 0 ? "destructive" : "warning"}>{value < 0 ? `Faltan ${formatCOP(-value)}` : `Sobran ${formatCOP(value)}`}</Badge>;
}

const TIPO_BADGE: Record<CashMovement["tipo"], "info" | "success" | "destructive"> = {
  Pago: "info",
  Ingreso: "success",
  Egreso: "destructive",
};

export function CashMovementsTable({ movements }: { movements: CashMovement[] }) {
  if (movements.length === 0) {
    return <EmptyState illustration="generic" title="Sin movimientos todavía" description="Los pagos que registres mientras la caja está abierta aparecen aquí solos." />;
  }
  return (
    <ResponsiveTable>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Hora</TableHead>
            <TableHead>Tipo</TableHead>
            <TableHead>Concepto</TableHead>
            <TableHead>Medio</TableHead>
            <TableHead>Registró</TableHead>
            <TableHead className="text-right">Monto</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {movements.map((m) => (
            <TableRow key={m.id} className={cn(m.anulado && "text-muted-foreground line-through")}>
              <TableCell className="whitespace-nowrap tabular-nums">
                {new Date(m.fecha).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" })}
              </TableCell>
              <TableCell>
                <Badge variant={TIPO_BADGE[m.tipo]}>{m.anulado ? "Anulado" : m.tipo}</Badge>
              </TableCell>
              <TableCell className="max-w-64">
                <span className="block truncate">{m.concepto}</span>
                {m.pacienteNombre && <span className="block truncate text-xs text-muted-foreground">{m.pacienteNombre}</span>}
              </TableCell>
              <TableCell>
                {m.metodoPago ?? "Efectivo"}
                {m.referencia && <div className="text-xs text-muted-foreground">{m.referencia}</div>}
              </TableCell>
              <TableCell className="text-sm">{m.usuario ?? "—"}</TableCell>
              <TableCell className={cn("text-right font-medium whitespace-nowrap tabular-nums", m.tipo === "Egreso" && "text-destructive")}>
                {m.tipo === "Egreso" ? "−" : ""}
                {formatCOP(m.monto)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </ResponsiveTable>
  );
}

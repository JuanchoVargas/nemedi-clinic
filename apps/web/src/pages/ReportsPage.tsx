// ============================================================
// ReportsPage.tsx — Reportes (/reports[?tab=])
//
// Ventas por período, procedimientos realizados, producción por esteticista y
// cartera por paciente. Rango de fechas compartido (por defecto el mes en curso)
// y "Descargar Excel" en cada uno. Los números llegan calculados del backend.
// ============================================================
import { useState } from "react";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { FileSpreadsheet, Loader2 } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { EmptyState } from "@/components/shared/EmptyState";
import { PageContainer } from "@/components/shared/PageContainer";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { downloadReport, useCarteraReport, useProceduresReport, useProductionReport, useSalesReport } from "@/api/reports.api";
import { toLocalDate } from "@/lib/dates";
import { formatCOP } from "@/lib/format-cop";
import { formatShortDate } from "@/lib/format-platform";
import { useToastStore } from "@/stores/toast.store";
import { SALE_LINE_LABELS, type SaleLineType } from "@/types/sale";
import type { ReportKind } from "@/types/report";

const TABS: { value: ReportKind; label: string }[] = [
  { value: "sales", label: "Ventas" },
  { value: "procedures", label: "Procedimientos" },
  { value: "production", label: "Producción" },
  { value: "cartera", label: "Cartera" },
];

function inicioDeMes() {
  const d = new Date();
  return toLocalDate(new Date(d.getFullYear(), d.getMonth(), 1));
}

export function ReportsPage() {
  const navigate = useNavigate();
  const { tab } = useSearch({ strict: false }) as { tab?: string };
  const activa: ReportKind = TABS.some((t) => t.value === tab) ? (tab as ReportKind) : "sales";
  const [desde, setDesde] = useState(inicioDeMes);
  const [hasta, setHasta] = useState(() => toLocalDate(new Date()));
  const [descargando, setDescargando] = useState(false);
  const rangoInvalido = !desde || !hasta || desde > hasta;

  const descargar = async () => {
    setDescargando(true);
    try {
      await downloadReport(activa, desde, hasta);
    } catch (error) {
      useToastStore.report(error);
    } finally {
      setDescargando(false);
    }
  };

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Reportes</h1>
          <p className="text-muted-foreground">Ventas, procedimientos, producción del equipo y cartera, con descarga en Excel.</p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          {activa !== "cartera" && (
            <>
              <div className="space-y-1">
                <Label htmlFor="rep-desde" className="text-xs text-muted-foreground">Desde</Label>
                <Input id="rep-desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} className="w-40 bg-card" />
              </div>
              <div className="space-y-1">
                <Label htmlFor="rep-hasta" className="text-xs text-muted-foreground">Hasta</Label>
                <Input id="rep-hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} className="w-40 bg-card" />
              </div>
            </>
          )}
          <Button onClick={() => void descargar()} disabled={descargando || (activa !== "cartera" && rangoInvalido)}>
            {descargando ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <FileSpreadsheet className="mr-2 h-4 w-4" />}
            Descargar Excel
          </Button>
        </div>
      </div>

      <Tabs value={activa} onValueChange={(v) => void navigate({ to: "/reports", search: { tab: v }, replace: true })}>
        <TabsList className="mb-4 flex-wrap">
          {TABS.map((t) => (
            <TabsTrigger key={t.value} value={t.value}>
              {t.label}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="sales">
          <SalesReportView desde={desde} hasta={hasta} enabled={activa === "sales"} />
        </TabsContent>
        <TabsContent value="procedures">
          <ProceduresReportView desde={desde} hasta={hasta} enabled={activa === "procedures"} />
        </TabsContent>
        <TabsContent value="production">
          <ProductionReportView desde={desde} hasta={hasta} enabled={activa === "production"} />
        </TabsContent>
        <TabsContent value="cartera">
          <CarteraReportView enabled={activa === "cartera"} />
        </TabsContent>
      </Tabs>
    </PageContainer>
  );
}

function Kpis({ items }: { items: { label: string; value: string }[] }) {
  return (
    <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {items.map((k) => (
        <Card key={k.label}>
          <CardContent>
            <p className="text-xs text-muted-foreground">{k.label}</p>
            <p className="font-heading text-2xl font-semibold tabular-nums">{k.value}</p>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function SalesReportView({ desde, hasta, enabled }: { desde: string; hasta: string; enabled: boolean }) {
  const { data, isLoading } = useSalesReport(desde, hasta, enabled);
  if (isLoading || !data) return <Skeleton className="h-64 w-full" />;
  return (
    <div className="space-y-4">
      <Kpis
        items={[
          { label: `Ventas confirmadas (${data.anuladas} anuladas)`, value: String(data.ventas) },
          { label: "Total vendido (IVA incluido)", value: formatCOP(data.total) },
          { label: "IVA", value: formatCOP(data.impuestos) },
          { label: "Por cobrar de estas ventas", value: formatCOP(data.saldo) },
        ]}
      />
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardContent>
            <p className="mb-2 font-heading text-sm font-semibold">Por tipo</p>
            {data.porTipo.length === 0 ? (
              <p className="text-sm text-muted-foreground">Sin ventas.</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {data.porTipo.map((t) => (
                  <li key={t.tipo} className="flex justify-between gap-3">
                    <span>{SALE_LINE_LABELS[t.tipo as SaleLineType] ?? t.tipo}</span>
                    <span className="tabular-nums">{formatCOP(t.total)}</span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
        <Card>
          <CardContent>
            <p className="mb-2 font-heading text-sm font-semibold">Cobrado por medio de pago</p>
            {data.porMetodo.length === 0 ? (
              <p className="text-sm text-muted-foreground">Sin pagos.</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {data.porMetodo.map((m) => (
                  <li key={m.metodo} className="flex justify-between gap-3">
                    <span>{m.metodo}</span>
                    <span className="tabular-nums">{formatCOP(m.total)}</span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </div>
      {data.filas.length === 0 ? (
        <EmptyState illustration="search" title="Sin ventas en el período" />
      ) : (
        <ResponsiveTable>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Venta</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">IVA</TableHead>
                <TableHead className="text-right">Total</TableHead>
                <TableHead className="text-right">Saldo</TableHead>
                <TableHead>Vendedor</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.filas.map((f) => (
                <TableRow key={f.id} className={f.estado === "Anulada" ? "text-muted-foreground" : undefined}>
                  <TableCell>
                    <Link to="/sales" search={{ saleId: f.id }} className="font-medium hover:text-primary hover:underline">
                      #{f.numero}
                    </Link>
                    <div className="text-xs text-muted-foreground">{formatShortDate(f.fecha)}</div>
                  </TableCell>
                  <TableCell>{f.cliente}</TableCell>
                  <TableCell>
                    <Badge variant={f.estado === "Anulada" ? "secondary" : "success"}>{f.estado}</Badge>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatCOP(f.impuestos)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCOP(f.total)}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.saldo > 0 ? formatCOP(f.saldo) : "—"}</TableCell>
                  <TableCell className="text-sm">{f.vendedor}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ResponsiveTable>
      )}
    </div>
  );
}

function ProceduresReportView({ desde, hasta, enabled }: { desde: string; hasta: string; enabled: boolean }) {
  const { data, isLoading } = useProceduresReport(desde, hasta, enabled);
  if (isLoading || !data) return <Skeleton className="h-64 w-full" />;
  return (
    <div>
      <Kpis
        items={[
          { label: "Procedimientos realizados", value: String(data.total) },
          { label: "Valor a precio de lista", value: formatCOP(data.valorTotal) },
        ]}
      />
      {data.filas.length === 0 ? (
        <EmptyState illustration="calendar" title="Sin citas completadas en el período" />
      ) : (
        <ResponsiveTable>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Procedimiento</TableHead>
                <TableHead className="text-right">Realizadas</TableHead>
                <TableHead className="text-right">De paquete</TableHead>
                <TableHead className="text-right">Sueltas</TableHead>
                <TableHead className="text-right">Valor</TableHead>
                <TableHead>Esteticistas</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.filas.map((f) => (
                <TableRow key={f.procedureId}>
                  <TableCell className="font-medium">{f.procedimiento}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.realizadas}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.dePaquete}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.sueltas}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCOP(f.valor)}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{f.esteticistas}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ResponsiveTable>
      )}
    </div>
  );
}

function ProductionReportView({ desde, hasta, enabled }: { desde: string; hasta: string; enabled: boolean }) {
  const { data, isLoading } = useProductionReport(desde, hasta, enabled);
  if (isLoading || !data) return <Skeleton className="h-64 w-full" />;
  if (data.filas.length === 0) return <EmptyState illustration="calendar" title="Sin citas en el período" />;
  return (
    <ResponsiveTable>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Esteticista</TableHead>
            <TableHead className="text-right">Completadas</TableHead>
            <TableHead className="text-right">Canceladas</TableHead>
            <TableHead className="text-right">No asistió</TableHead>
            <TableHead className="text-right">Horas de cabina</TableHead>
            <TableHead className="text-right">Pacientes</TableHead>
            <TableHead className="text-right">Notas</TableHead>
            <TableHead className="text-right">Valor producido</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {data.filas.map((f) => (
            <TableRow key={f.esteticistId}>
              <TableCell className="font-medium">{f.esteticista}</TableCell>
              <TableCell className="text-right tabular-nums">{f.completadas}</TableCell>
              <TableCell className="text-right tabular-nums">{f.canceladas}</TableCell>
              <TableCell className="text-right tabular-nums">{f.noAsistio}</TableCell>
              <TableCell className="text-right tabular-nums">{f.horas.toLocaleString("es-CO")}</TableCell>
              <TableCell className="text-right tabular-nums">{f.pacientes}</TableCell>
              <TableCell className="text-right tabular-nums">{f.notasClinicas}</TableCell>
              <TableCell className="text-right font-medium tabular-nums">{formatCOP(f.valorProducido)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </ResponsiveTable>
  );
}

function CarteraReportView({ enabled }: { enabled: boolean }) {
  const { data, isLoading } = useCarteraReport(enabled);
  if (isLoading || !data) return <Skeleton className="h-64 w-full" />;
  return (
    <div>
      <Kpis
        items={[
          { label: `Cartera al ${formatShortDate(data.corte)}`, value: formatCOP(data.total) },
          { label: "Clientes con saldo", value: String(data.clientes) },
        ]}
      />
      {data.filas.length === 0 ? (
        <EmptyState illustration="generic" title="Nadie debe nada" description="Todos los paquetes y ventas están pagados." />
      ) : (
        <ResponsiveTable>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Cliente</TableHead>
                <TableHead className="text-right">Paquetes</TableHead>
                <TableHead className="text-right">Ventas</TableHead>
                <TableHead className="text-right">Total</TableHead>
                <TableHead className="text-right">Días</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.filas.map((f, i) => (
                <TableRow key={`${f.patientId ?? "m"}-${i}`}>
                  <TableCell>
                    {f.patientId ? (
                      <Link to="/patients/$id" params={{ id: f.patientId }} className="font-medium hover:text-primary hover:underline">
                        {f.cliente}
                      </Link>
                    ) : (
                      <span className="font-medium">{f.cliente}</span>
                    )}
                    <div className="text-xs text-muted-foreground">
                      {[f.documento, f.telefono].filter(Boolean).join(" · ")}
                    </div>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{f.saldoPaquetes ? formatCOP(f.saldoPaquetes) : "—"}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.saldoVentas ? formatCOP(f.saldoVentas) : "—"}</TableCell>
                  <TableCell className="text-right font-medium tabular-nums">{formatCOP(f.total)}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    <Badge variant={f.diasMora > 60 ? "destructive" : f.diasMora > 30 ? "warning" : "outline"}>{f.diasMora}</Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ResponsiveTable>
      )}
    </div>
  );
}

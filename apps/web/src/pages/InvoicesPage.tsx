// ============================================================
// InvoicesPage.tsx — Facturas electrónicas (/invoices)
//
// Facturas y notas crédito con su estado ante la DIAN (Emitida, En proceso, Rechazada
// con el motivo del proveedor, Anulada), filtros, Excel y, por fila, PDF, XML,
// reenviar por correo, nota crédito y reintentar. Recepción y dueño.
// ============================================================
import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight, FileSpreadsheet, Search, Settings2 } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/shared/EmptyState";
import { PageContainer } from "@/components/shared/PageContainer";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { MotionTableRow, staggerProps } from "@/components/shared/motion-elements";
import { InvoiceActions } from "@/components/invoices/InvoiceActions";
import { downloadInvoices, useInvoices, useInvoicingSettings } from "@/api/invoices.api";
import { useDebounce } from "@/hooks/use-debounce";
import { usePermissions } from "@/hooks/use-permissions";
import { toLocalDate } from "@/lib/dates";
import { formatCOP } from "@/lib/format-cop";
import { formatShortDate } from "@/lib/format-platform";
import { useToastStore } from "@/stores/toast.store";
import { DOCUMENT_LABELS, INVOICE_BADGE } from "@/types/invoice";
import { INVOICE_STATUS_LABELS } from "@/types/sale";

const ALL = "all";
const PAGE_SIZE = 20;

function inicioDeMes() {
  const d = new Date();
  return toLocalDate(new Date(d.getFullYear(), d.getMonth(), 1));
}

export function InvoicesPage() {
  const { can } = usePermissions();
  const { data: settings } = useInvoicingSettings();
  const [searchInput, setSearchInput] = useState("");
  const search = useDebounce(searchInput, 300);
  const [estado, setEstado] = useState<string>(ALL);
  const [tipo, setTipo] = useState<string>(ALL);
  const [desde, setDesde] = useState(inicioDeMes);
  const [hasta, setHasta] = useState(() => toLocalDate(new Date()));
  const [page, setPage] = useState(1);
  const [downloading, setDownloading] = useState(false);

  const filtros = {
    search,
    estado: estado === ALL ? undefined : estado,
    tipoDocumento: tipo === ALL ? undefined : tipo,
    desde: desde || undefined,
    hasta: hasta || undefined,
  };
  const { data, isLoading } = useInvoices({ ...filtros, page, pageSize: PAGE_SIZE });
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;
  const filtrado = !!search || estado !== ALL || tipo !== ALL;

  const withReset = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPage(1);
  };

  const onExcel = async () => {
    setDownloading(true);
    try {
      await downloadInvoices(filtros);
    } catch (e) {
      useToastStore.report(e);
    } finally {
      setDownloading(false);
    }
  };

  const sinConfigurar = settings && !settings.habilitada;

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Facturas</h1>
          <p className="text-muted-foreground">Facturas electrónicas y notas crédito ante la DIAN{settings?.proveedor ? `, emitidas con ${settings.proveedor}` : ""}.</p>
        </div>
        <Button variant="outline" disabled={downloading} onClick={() => void onExcel()}>
          <FileSpreadsheet className="mr-2 h-4 w-4" />
          {downloading ? "Descargando..." : "Descargar Excel"}
        </Button>
      </div>

      {sinConfigurar && (
        <div className="mb-4 flex flex-wrap items-center gap-3 rounded-lg border border-dashed p-3 text-sm">
          <span>La facturación electrónica no está activa para la clínica.</span>
          {can("tenants.update") && (
            <Button asChild size="sm" variant="outline">
              <Link to="/settings" search={{ tab: "facturacion" }}>
                <Settings2 className="mr-1.5 h-4 w-4" />
                Configurar
              </Link>
            </Button>
          )}
        </div>
      )}

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            type="search"
            placeholder="Número, cliente o documento..."
            value={searchInput}
            onChange={(e) => withReset(setSearchInput)(e.target.value)}
            className="bg-card pl-9"
            aria-label="Buscar facturas"
          />
        </div>
        <Select value={estado} onValueChange={withReset(setEstado)}>
          <SelectTrigger className="w-40 bg-card" aria-label="Estado">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>Todos los estados</SelectItem>
            <SelectItem value="Emitida">Emitidas</SelectItem>
            <SelectItem value="Pendiente">En proceso</SelectItem>
            <SelectItem value="Rechazada">Rechazadas</SelectItem>
            <SelectItem value="Anulada">Anuladas</SelectItem>
          </SelectContent>
        </Select>
        <Select value={tipo} onValueChange={withReset(setTipo)}>
          <SelectTrigger className="w-40 bg-card" aria-label="Documento">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>Todos los documentos</SelectItem>
            <SelectItem value="Factura">Facturas</SelectItem>
            <SelectItem value="NotaCredito">Notas crédito</SelectItem>
          </SelectContent>
        </Select>
        <div className="space-y-1">
          <Label htmlFor="facturas-desde" className="text-xs text-muted-foreground">Desde</Label>
          <Input id="facturas-desde" type="date" value={desde} onChange={(e) => withReset(setDesde)(e.target.value)} className="w-40 bg-card" />
        </div>
        <div className="space-y-1">
          <Label htmlFor="facturas-hasta" className="text-xs text-muted-foreground">Hasta</Label>
          <Input id="facturas-hasta" type="date" value={hasta} onChange={(e) => withReset(setHasta)(e.target.value)} className="w-40 bg-card" />
        </div>
      </div>

      <ResponsiveTable>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Documento</TableHead>
              <TableHead>Cliente</TableHead>
              <TableHead>Origen</TableHead>
              <TableHead className="text-right">Total</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading &&
              Array.from({ length: 5 }).map((_, i) => (
                <TableRow key={`skel-${i}`}>
                  <TableCell colSpan={6}>
                    <Skeleton className="h-6 w-full" />
                  </TableCell>
                </TableRow>
              ))}

            {!isLoading && data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>
                  <EmptyState
                    illustration={filtrado ? "search" : "generic"}
                    title={filtrado ? "Sin documentos con esos filtros" : "Aún no hay facturas en este periodo"}
                    description="Se emiten desde el detalle de una venta o desde un pago en la ficha del paciente."
                  />
                </TableCell>
              </TableRow>
            )}

            {data?.items.map((f, i) => (
              <MotionTableRow key={f.id} {...staggerProps(i)} className={f.estado === "Anulada" ? "text-muted-foreground" : undefined}>
                <TableCell>
                  <span className="font-heading font-semibold">{f.numero ?? "Sin número"}</span>
                  <div className="text-xs text-muted-foreground">
                    {DOCUMENT_LABELS[f.tipoDocumento]} · {formatShortDate(f.fechaEmision ?? f.creada)}
                  </div>
                </TableCell>
                <TableCell className="max-w-48">
                  {f.patientId ? (
                    <Link to="/patients/$id" params={{ id: f.patientId }} className="block truncate font-medium hover:text-primary hover:underline">
                      {f.clienteNombre}
                    </Link>
                  ) : (
                    <span className="block truncate">{f.clienteNombre}</span>
                  )}
                  <div className="text-xs text-muted-foreground">{f.clienteDocumento}</div>
                </TableCell>
                <TableCell className="text-sm">
                  {f.saleId && f.ventaNumero ? (
                    <Link to="/sales" search={{ saleId: f.saleId }} className="hover:text-primary hover:underline">
                      Venta #{f.ventaNumero}
                    </Link>
                  ) : f.paymentId ? (
                    "Abono de paquete"
                  ) : (
                    "—"
                  )}
                  {f.facturaOrigenNumero && <div className="text-xs text-muted-foreground">Corrige {f.facturaOrigenNumero}</div>}
                </TableCell>
                <TableCell className="text-right font-medium whitespace-nowrap tabular-nums">
                  {f.tipoDocumento === "NotaCredito" ? `−${formatCOP(f.total)}` : formatCOP(f.total)}
                </TableCell>
                <TableCell className="max-w-64">
                  <Badge variant={INVOICE_BADGE[f.estado]}>{INVOICE_STATUS_LABELS[f.estado]}</Badge>
                  {f.estado === "Rechazada" && f.mensaje && <p className="mt-1 line-clamp-3 text-xs text-destructive" title={f.mensaje}>{f.mensaje}</p>}
                  {f.estado === "Pendiente" && <p className="mt-1 text-xs text-muted-foreground">{f.intentos} intento(s)</p>}
                  {f.totalNotasCredito > 0 && f.tipoDocumento === "Factura" && (
                    <p className="mt-1 text-xs text-muted-foreground">Notas crédito: {formatCOP(f.totalNotasCredito)}</p>
                  )}
                </TableCell>
                <TableCell>
                  <InvoiceActions invoice={f} compact />
                </TableCell>
              </MotionTableRow>
            ))}
          </TableBody>
        </Table>
      </ResponsiveTable>

      {data && data.totalCount > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-end gap-2 text-sm">
          <span className="text-muted-foreground">
            Página {page} de {totalPages} · {data.totalCount} documentos
          </span>
          <Button variant="outline" size="icon" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label="Página anterior">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button variant="outline" size="icon" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label="Página siguiente">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      )}
    </PageContainer>
  );
}

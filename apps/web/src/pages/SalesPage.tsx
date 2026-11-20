// ============================================================
// SalesPage.tsx — Ventas (/sales[?saleId=])
//
// Listado con filtros (texto, estado, rango de fechas, solo con saldo) y el
// detalle de cada venta en un diálogo (?saleId= lo abre al volver de "Nueva venta").
// Recepción y dueño; la esteticista no llega aquí (requireRoles en el router).
// ============================================================
import { useState } from "react";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight, Eye, Plus, Search } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/shared/EmptyState";
import { PageContainer } from "@/components/shared/PageContainer";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { MotionTableRow, staggerProps } from "@/components/shared/motion-elements";
import { SaleDetailDialog } from "@/components/sales/SaleDetailDialog";
import { useSales } from "@/api/sales.api";
import { useDebounce } from "@/hooks/use-debounce";
import { usePermissions } from "@/hooks/use-permissions";
import { toLocalDate } from "@/lib/dates";
import { formatCOP } from "@/lib/format-cop";
import { formatShortDate } from "@/lib/format-platform";
import { SALE_STATUS_BADGE, type SaleStatus } from "@/types/sale";

const ALL = "all";
const PAGE_SIZE = 20;

function inicioDeMes() {
  const d = new Date();
  return toLocalDate(new Date(d.getFullYear(), d.getMonth(), 1));
}

export function SalesPage() {
  const { can } = usePermissions();
  const navigate = useNavigate();
  const { saleId } = useSearch({ strict: false }) as { saleId?: string };

  const [searchInput, setSearchInput] = useState("");
  const search = useDebounce(searchInput, 300);
  const [estado, setEstado] = useState<string>(ALL);
  const [desde, setDesde] = useState(inicioDeMes);
  const [hasta, setHasta] = useState(() => toLocalDate(new Date()));
  const [conSaldo, setConSaldo] = useState(false);
  const [page, setPage] = useState(1);

  const { data, isLoading } = useSales({
    search,
    estado: estado === ALL ? undefined : (estado as SaleStatus),
    desde: desde || undefined,
    hasta: hasta || undefined,
    conSaldo,
    page,
    pageSize: PAGE_SIZE,
  });
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;
  const filtrado = !!search || estado !== ALL || conSaldo;

  // Cambiar un filtro vuelve a la primera página (en el handler: sin efectos de reset)
  const withReset = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPage(1);
  };
  const openSale = (id?: string) => void navigate({ to: "/sales", search: id ? { saleId: id } : {}, replace: true });

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Ventas</h1>
          <p className="text-muted-foreground">Productos, procedimientos y paquetes vendidos, con sus pagos y su saldo.</p>
        </div>
        {can("sales.create") && (
          <Button asChild>
            <Link to="/sales/new">
              <Plus className="mr-2 h-4 w-4" />
              Nueva venta
            </Link>
          </Button>
        )}
      </div>

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            type="search"
            placeholder="Número, cliente o cédula..."
            value={searchInput}
            onChange={(e) => withReset(setSearchInput)(e.target.value)}
            className="bg-card pl-9"
            aria-label="Buscar ventas"
          />
        </div>
        <Select value={estado} onValueChange={withReset(setEstado)}>
          <SelectTrigger className="w-40 bg-card" aria-label="Estado">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>Todos los estados</SelectItem>
            <SelectItem value="Confirmada">Confirmadas</SelectItem>
            <SelectItem value="Borrador">Borradores</SelectItem>
            <SelectItem value="Anulada">Anuladas</SelectItem>
          </SelectContent>
        </Select>
        <div className="space-y-1">
          <Label htmlFor="ventas-desde" className="text-xs text-muted-foreground">Desde</Label>
          <Input id="ventas-desde" type="date" value={desde} onChange={(e) => withReset(setDesde)(e.target.value)} className="w-40 bg-card" />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ventas-hasta" className="text-xs text-muted-foreground">Hasta</Label>
          <Input id="ventas-hasta" type="date" value={hasta} onChange={(e) => withReset(setHasta)(e.target.value)} className="w-40 bg-card" />
        </div>
        <label className="flex h-9 items-center gap-2 text-sm">
          <Switch checked={conSaldo} onCheckedChange={withReset(setConSaldo)} aria-label="Solo con saldo" />
          Solo con saldo
        </label>
      </div>

      <ResponsiveTable>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Venta</TableHead>
              <TableHead>Cliente</TableHead>
              <TableHead>Detalle</TableHead>
              <TableHead className="text-right">Total</TableHead>
              <TableHead className="text-right">Saldo</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading &&
              Array.from({ length: 5 }).map((_, i) => (
                <TableRow key={`skel-${i}`}>
                  <TableCell colSpan={7}>
                    <Skeleton className="h-6 w-full" />
                  </TableCell>
                </TableRow>
              ))}

            {!isLoading && data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>
                  <EmptyState
                    illustration={filtrado ? "search" : "generic"}
                    title={filtrado ? "Sin ventas con esos filtros" : "Aún no hay ventas en este periodo"}
                    description="Una venta descuenta el inventario, asigna los paquetes al paciente y deja el pago en la caja."
                    action={
                      !filtrado &&
                      can("sales.create") && (
                        <Button asChild>
                          <Link to="/sales/new">
                            <Plus className="mr-2 h-4 w-4" />
                            Nueva venta
                          </Link>
                        </Button>
                      )
                    }
                  />
                </TableCell>
              </TableRow>
            )}

            {data?.items.map((s, i) => (
              <MotionTableRow key={s.id} {...staggerProps(i)} className={s.estado === "Anulada" ? "text-muted-foreground" : undefined}>
                <TableCell>
                  <span className="font-heading font-semibold">#{s.numero}</span>
                  <div className="text-xs text-muted-foreground">{formatShortDate(s.fecha)}</div>
                </TableCell>
                <TableCell className="max-w-48">
                  {s.patientId ? (
                    <Link to="/patients/$id" params={{ id: s.patientId }} className="block truncate font-medium hover:text-primary hover:underline">
                      {s.cliente}
                    </Link>
                  ) : (
                    <span className="block truncate">{s.cliente}</span>
                  )}
                  <div className="text-xs text-muted-foreground">{s.esMostrador ? "Mostrador" : s.clienteDocumento}</div>
                </TableCell>
                <TableCell className="max-w-56 truncate text-sm">{s.resumen}</TableCell>
                <TableCell className="text-right font-medium whitespace-nowrap tabular-nums">{formatCOP(s.total)}</TableCell>
                <TableCell className="text-right whitespace-nowrap tabular-nums">
                  {s.estado === "Confirmada" ? (s.saldo > 0 ? formatCOP(s.saldo) : <span className="text-success">Pagada</span>) : "—"}
                </TableCell>
                <TableCell>
                  <Badge variant={SALE_STATUS_BADGE[s.estado]}>{s.estado}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  <Button variant="outline" size="sm" onClick={() => openSale(s.id)} aria-label={`Ver la venta ${s.numero}`}>
                    <Eye className="mr-1 h-4 w-4" />
                    Ver
                  </Button>
                </TableCell>
              </MotionTableRow>
            ))}
          </TableBody>
        </Table>
      </ResponsiveTable>

      {data && data.totalCount > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-end gap-2 text-sm">
          <span className="text-muted-foreground">
            Página {page} de {totalPages} · {data.totalCount} ventas
          </span>
          <Button variant="outline" size="icon" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label="Página anterior">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button variant="outline" size="icon" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label="Página siguiente">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      )}

      {saleId && <SaleDetailDialog key={saleId} id={saleId} onClose={() => openSale(undefined)} />}
    </PageContainer>
  );
}

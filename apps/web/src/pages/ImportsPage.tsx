// ============================================================
// ImportsPage.tsx — Historial de importaciones (/imports)
//
// Quién importó qué archivo, de qué (pacientes, productos, procedimientos, paquetes), cuándo y con
// qué resultado (tabla ImportRuns del backend).
// ============================================================
import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { ChevronDown, FileUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { EmptyState } from "@/components/shared/EmptyState";
import { PageContainer } from "@/components/shared/PageContainer";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { useImportHistory } from "@/api/imports.api";

const PAGE_SIZE = 20;

// Orden sugerido: los paquetes necesitan que sus procedimientos ya existan
const IMPORT_TARGETS = [
  { label: "Pacientes", to: "/patients/import" },
  { label: "Productos e inventario", to: "/inventory/import" },
  { label: "Procedimientos", to: "/procedures/import" },
  { label: "Paquetes", to: "/packages/import" },
] as const;

const formatFecha = (iso: string) =>
  new Date(iso).toLocaleString("es-CO", { day: "2-digit", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });

export function ImportsPage() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useImportHistory(page, PAGE_SIZE);
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold">Importaciones</h1>
          <p className="text-muted-foreground">Cada archivo importado: quién, cuándo y qué pasó con sus filas.</p>
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button data-testid="imports-new">
              <FileUp className="mr-2 h-4 w-4" />
              Importar
              <ChevronDown className="ml-2 h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {IMPORT_TARGETS.map((t) => (
              <DropdownMenuItem key={t.to} asChild>
                <Link to={t.to}>{t.label}</Link>
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <ResponsiveTable>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Qué</TableHead>
              <TableHead>Archivo</TableHead>
              <TableHead>Usuario</TableHead>
              <TableHead className="text-right">Filas</TableHead>
              <TableHead className="text-right">Creados</TableHead>
              <TableHead className="text-right">Actualizados</TableHead>
              <TableHead className="text-right">Omitidos</TableHead>
              <TableHead className="text-right">Con error</TableHead>
              <TableHead>Detalle</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading &&
              Array.from({ length: 4 }).map((_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={10}>
                    <Skeleton className="h-4 w-full" />
                  </TableCell>
                </TableRow>
              ))}
            {data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={10}>
                  <EmptyState
                    illustration="patients"
                    title="Aún no hay importaciones"
                    description="Sube un Excel o CSV con tus pacientes, productos, procedimientos o paquetes y aparecerá aquí."
                  />
                </TableCell>
              </TableRow>
            )}
            {data?.items.map((r) => (
              <TableRow key={r.id}>
                <TableCell className="whitespace-nowrap">{formatFecha(r.fecha)}</TableCell>
                <TableCell>{r.entidadNombre}</TableCell>
                <TableCell className="max-w-[240px] truncate" title={r.archivoNombre}>
                  {r.archivoNombre}
                </TableCell>
                <TableCell>{r.usuario || "—"}</TableCell>
                <TableCell className="text-right">{r.filasTotales}</TableCell>
                <TableCell className="text-right">{r.creadas}</TableCell>
                <TableCell className="text-right">{r.actualizadas}</TableCell>
                <TableCell className="text-right">{r.omitidas}</TableCell>
                <TableCell className="text-right">{r.conError}</TableCell>
                <TableCell className="text-muted-foreground">{r.detalle ?? "—"}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </ResponsiveTable>

      {data && data.totalCount > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground">
          <span>
            Página {data.page} de {totalPages}
          </span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={() => setPage((p) => p - 1)} disabled={page <= 1}>
              Anterior
            </Button>
            <Button variant="outline" size="sm" onClick={() => setPage((p) => p + 1)} disabled={page >= totalPages}>
              Siguiente
            </Button>
          </div>
        </div>
      )}
    </PageContainer>
  );
}

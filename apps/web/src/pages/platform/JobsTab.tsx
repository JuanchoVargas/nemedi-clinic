// ============================================================
// JobsTab.tsx — Tareas programadas (jobs recurrentes)
//
// Última corrida, duración y resultado de cada job, y el historial reciente.
// En IIS los dispara la tarea programada de Plesk (POST /api/v1/jobs/{nombre});
// en desarrollo, Hangfire. "Ejecutar ahora" usa el mismo candado.
// ============================================================

import { Play } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { usePlatformJobs, useRunPlatformJob } from "@/api/platform.api";
import { useToastStore } from "@/stores/toast.store";
import type { JobRun, JobRunEstado } from "@/types/platform";

const ESTADO: Record<JobRunEstado, { label: string; variant: "success" | "destructive" | "warning" | "info" }> = {
  Exitosa: { label: "Exitosa", variant: "success" },
  Fallida: { label: "Fallida", variant: "destructive" },
  Abandonada: { label: "Sin terminar", variant: "warning" },
  EnCurso: { label: "En curso", variant: "info" },
};

/** "2026-09-28T02:00:03" (hora local, sin zona) → "28 sep 02:00". */
function formatInicio(iso: string) {
  const [fecha, hora] = iso.split("T");
  const [y, m, d] = fecha.split("-").map(Number);
  const mes = new Date(y, m - 1, d).toLocaleDateString("es-CO", { month: "short" }).replace(".", "");
  return `${d} ${mes} ${hora.slice(0, 5)}`;
}

function formatDuracion(ms: number | null) {
  if (ms == null) return "—";
  if (ms < 1000) return `${ms} ms`;
  const s = ms / 1000;
  return s < 60 ? `${s.toFixed(1)} s` : `${Math.floor(s / 60)} min ${Math.round(s % 60)} s`;
}

function EstadoBadge({ run }: { run: JobRun }) {
  const e = ESTADO[run.estado];
  return <Badge variant={e.variant}>{e.label}</Badge>;
}

export function JobsTab() {
  const { data, isLoading } = usePlatformJobs();
  const run = useRunPlatformJob();

  const ejecutar = async (nombre: string) => {
    try {
      const r = await run.mutateAsync(nombre);
      if (r.estado === "Exitosa") useToastStore.success(`${nombre}: listo`, r.resultado ?? undefined);
      else useToastStore.error(`${nombre}: ${r.estado === "Fallida" ? "falló" : r.estado}`, r.resultado ?? undefined);
    } catch {
      // toast global (409 si ya está corriendo)
    }
  };

  if (isLoading || !data) return <Skeleton className="h-40 w-full" />;

  return (
    <div className="space-y-6">
      <p className="text-sm text-muted-foreground">
        {data.programadorActivo
          ? "El programador interno (Hangfire) corre los jobs en este servidor."
          : data.endpointHabilitado
            ? "Programador interno apagado: los jobs los dispara la tarea programada de Plesk (POST /api/v1/jobs/{nombre})."
            : "Programador interno apagado y sin clave de jobs: ninguna tarea recurrente se está ejecutando."}
      </p>

      <ResponsiveTable>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Job</TableHead>
              <TableHead>Frecuencia</TableHead>
              <TableHead>Última corrida</TableHead>
              <TableHead>Duración</TableHead>
              <TableHead>Resultado</TableHead>
              <TableHead className="text-right">Acción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.jobs.map((j) => {
              const u = j.ultimaEjecucion;
              return (
                <TableRow key={j.nombre}>
                  <TableCell>
                    <div className="font-medium">{j.nombre}</div>
                    <div className="max-w-xs text-xs whitespace-normal text-muted-foreground">{j.descripcion}</div>
                  </TableCell>
                  <TableCell>{j.frecuenciaSugerida}</TableCell>
                  <TableCell>
                    {u ? (
                      <div className="flex flex-wrap items-center gap-2">
                        <EstadoBadge run={u} />
                        <span>{formatInicio(u.inicio)}</span>
                      </div>
                    ) : (
                      <span className="text-muted-foreground">Nunca</span>
                    )}
                    {u && u.estado !== "Exitosa" && j.ultimaExitosa && (
                      <div className="text-xs text-muted-foreground">Última exitosa: {formatInicio(j.ultimaExitosa.inicio)}</div>
                    )}
                  </TableCell>
                  <TableCell>{formatDuracion(u?.duracionMs ?? null)}</TableCell>
                  <TableCell className="max-w-sm text-sm whitespace-normal">{u?.resultado ?? "—"}</TableCell>
                  <TableCell className="text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={run.isPending}
                      onClick={() => void ejecutar(j.nombre)}
                      aria-label={`Ejecutar ${j.nombre} ahora`}
                    >
                      <Play className="h-4 w-4" />
                      Ejecutar
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </ResponsiveTable>

      <section aria-label="Historial de ejecuciones">
        <h2 className="mb-2 text-lg font-semibold">Historial</h2>
        <ResponsiveTable>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Inicio</TableHead>
                <TableHead>Job</TableHead>
                <TableHead>Origen</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Duración</TableHead>
                <TableHead>Resultado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.historial.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="py-4 text-center text-muted-foreground">
                    Todavía no se ha ejecutado ningún job.
                  </TableCell>
                </TableRow>
              )}
              {data.historial.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>{formatInicio(r.inicio)}</TableCell>
                  <TableCell>{r.nombre}</TableCell>
                  <TableCell>{r.origen}</TableCell>
                  <TableCell><EstadoBadge run={r} /></TableCell>
                  <TableCell>{formatDuracion(r.duracionMs)}</TableCell>
                  <TableCell className="max-w-sm text-sm whitespace-normal">{r.resultado ?? "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ResponsiveTable>
      </section>
    </div>
  );
}

// ============================================================
// PreviewStep.tsx — Paso 3: cómo quedarían los datos, antes de guardar nada
//
// El resumen cuenta TODAS las filas; la tabla muestra las primeras 50 (o las primeras 50 con
// algo que revisar). Los duplicados (mismo documento que un paciente existente) se omiten o se
// actualizan; las filas con error nunca se importan y se pueden bajar en Excel con su motivo.
// ============================================================
import { ArrowLeft, Download, Info, Loader2, Upload } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import { SegmentedControl } from "@/components/shared/SegmentedControl";
import type { ImportWizardState } from "@/hooks/use-import-wizard";
import type { DuplicateMode, ImportRowState } from "@/types/import";

const STATE_BADGE: Record<ImportRowState, { label: string; variant: "success" | "destructive" | "warning" }> = {
  Valida: { label: "Lista", variant: "success" },
  Error: { label: "Con error", variant: "destructive" },
  Duplicada: { label: "Duplicada", variant: "warning" },
};

const DUPLICATE_OPTIONS = [
  { value: "Omitir", label: "Omitir" },
  { value: "Actualizar", label: "Actualizar" },
] as const;

export function PreviewStep({ wizard, count, duplicateKey }: { wizard: ImportWizardState; count: (n: number) => string; duplicateKey: string }) {
  const preview = wizard.preview!;
  const analysis = wizard.analysis!;
  // Columnas: los campos con algún valor en la muestra, en el orden del sistema
  const shown = analysis.campos.filter((f) => f.key !== "nombreCompleto" && preview.filas.some((r) => r.valores[f.key]));
  const n = (v: number) => v.toLocaleString("es-CO");

  return (
    <Card>
      <CardHeader>
        <CardTitle>Revisar antes de importar</CardTitle>
        <CardDescription data-testid="import-summary">
          {n(preview.validas)} {preview.validas === 1 ? "lista" : "listas"}, {n(preview.conError)} con error, {n(preview.duplicadas)}{" "}
          {preview.duplicadas === 1 ? "duplicada" : "duplicadas"}
          {preview.conAvisos > 0 && ` · ${n(preview.conAvisos)} con avisos para revisar`} · {n(preview.total)} filas en total
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {preview.avisosGenerales.map((aviso) => (
          <div key={aviso} role="status" className="flex gap-2 rounded-xl bg-sand-soft p-3 text-sm text-sand-foreground dark:text-sand" data-testid="import-notice">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <p>{aviso}</p>
          </div>
        ))}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4" data-testid="import-stats">
          <Stat label="Listas" value={preview.validas} tone="text-success" />
          <Stat label="Con error" value={preview.conError} tone="text-destructive" />
          <Stat label="Duplicadas" value={preview.duplicadas} tone="text-sand-foreground dark:text-sand" />
          <Stat label="Con avisos" value={preview.conAvisos} tone="text-muted-foreground" />
        </div>
        {preview.detalle && (
          <p className="text-sm text-muted-foreground" data-testid="import-detail">
            {count(preview.nuevos)} {preview.nuevos === 1 ? "nuevo" : "nuevos"} · {preview.detalle}
          </p>
        )}

        <div className="flex flex-wrap items-center justify-between gap-4">
          {preview.duplicadas > 0 ? (
            <div className="space-y-1">
              <p className="text-sm font-medium">
                {preview.existentes === 1 ? "El que ya existe" : `Los ${n(preview.existentes)} que ya existen`} ({duplicateKey}):
              </p>
              <SegmentedControl<DuplicateMode>
                label="Qué hacer con los duplicados"
                options={DUPLICATE_OPTIONS}
                value={wizard.duplicados}
                onChange={wizard.setDuplicados}
              />
              <p className="text-xs text-muted-foreground">
                {wizard.duplicados === "Omitir"
                  ? "Se dejan como están."
                  : "Se actualizan con los datos del archivo; una celda vacía no borra lo que ya tienen."}
              </p>
            </div>
          ) : (
            <span />
          )}
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center gap-2">
              <Switch id="solo-problemas" data-testid="import-only-problems" checked={wizard.soloProblemas} onCheckedChange={wizard.toggleSoloProblemas} disabled={wizard.previewing} />
              <Label htmlFor="solo-problemas">Solo filas con problemas</Label>
            </div>
            {preview.conError > 0 && (
              <Button variant="outline" onClick={wizard.downloadErrorRows} disabled={wizard.downloadingErrors} data-testid="import-download-errors">
                {wizard.downloadingErrors ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Download className="mr-2 h-4 w-4" />}
                Descargar filas con error
              </Button>
            )}
          </div>
        </div>

        <ResponsiveTable>
          <Table aria-busy={wizard.previewing}>
            <TableHeader>
              <TableRow>
                <TableHead className="w-16">Fila</TableHead>
                <TableHead>Estado</TableHead>
                {/* Justo después del estado: el motivo se lee sin desplazar la tabla */}
                <TableHead className="min-w-[280px]">Observaciones</TableHead>
                {shown.map((f) => (
                  <TableHead key={f.key}>{f.label}</TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {preview.filas.map((row) => (
                <TableRow key={row.fila} data-testid={`preview-row-${row.fila}`}>
                  <TableCell className="font-mono text-muted-foreground">{row.fila}</TableCell>
                  <TableCell>
                    <Badge variant={STATE_BADGE[row.estado].variant}>{STATE_BADGE[row.estado].label}</Badge>
                  </TableCell>
                  <TableCell className="text-sm">
                    <ul className="space-y-0.5">
                      {row.existente && <li className="text-sand-foreground dark:text-sand">Ya existe: {row.existente}</li>}
                      {row.errores.map((e) => (
                        <li key={e} className="text-destructive">{e}</li>
                      ))}
                      {row.avisos.map((a) => (
                        <li key={a} className="text-muted-foreground">{a}</li>
                      ))}
                    </ul>
                  </TableCell>
                  {shown.map((f) => (
                    <TableCell key={f.key} className="whitespace-nowrap">
                      {row.valores[f.key] ?? <span className="text-muted-foreground">—</span>}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
              {preview.filas.length === 0 && (
                <TableRow>
                  <TableCell colSpan={shown.length + 3} className="py-8 text-center text-muted-foreground">
                    Ninguna fila tiene problemas.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </ResponsiveTable>
        {preview.total > preview.filas.length && (
          <p className="text-xs text-muted-foreground">
            Se muestran {n(preview.filas.length)} filas; el resumen y la importación cuentan las {n(preview.total)}.
          </p>
        )}

        <div className="flex flex-wrap items-center justify-between gap-2">
          <Button variant="outline" onClick={() => wizard.setStep("mapping")} disabled={wizard.importing}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Volver al mapeo
          </Button>
          <Button onClick={() => void wizard.runImport()} disabled={wizard.toWrite === 0 || wizard.importing} data-testid="import-run">
            {wizard.importing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Upload className="mr-2 h-4 w-4" />}
            {wizard.importing
              ? "Importando…"
              : wizard.toWrite === 0
                ? "No hay filas para importar"
                : `Importar ${count(wizard.toWrite)}`}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl border bg-card p-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={`text-2xl font-semibold ${tone}`}>{value.toLocaleString("es-CO")}</p>
    </div>
  );
}

// ============================================================
// MappingStep.tsx — Paso 2: qué columna del archivo va a qué campo
//
// Llega con el mapeo que propuso el backend (por parecido de nombres). Cada columna tiene un
// select; elegir un campo que ya tenía otra columna se lo quita a esa. "No importar" = ignorarla.
// ============================================================
import { useState } from "react";
import { AlertTriangle, ArrowLeft, ArrowRight, Loader2, Wand2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ResponsiveTable } from "@/components/shared/ResponsiveTable";
import type { ImportWizardState } from "@/hooks/use-import-wizard";
import type { ImportField } from "@/types/import";

const NONE = "__none";

export function MappingStep({ wizard }: { wizard: ImportWizardState }) {
  const analysis = wizard.analysis!;
  const [headerRow, setHeaderRow] = useState(String(analysis.filaEncabezados));
  const fieldByColumn = new Map(Object.entries(wizard.mapping).map(([field, col]) => [col, field]));
  const suggested = new Map(Object.entries(analysis.mapeoSugerido).map(([field, col]) => [col, field]));
  const general = analysis.campos.filter((f) => !f.group);
  const groups = [...new Set(analysis.campos.filter((f) => f.group).map((f) => f.group!))];
  const mappedCount = Object.keys(wizard.mapping).length;

  const applyHeaderRow = () => {
    const fila = Number(headerRow);
    if (Number.isInteger(fila) && fila >= 1) void wizard.changeHeaderRow(fila);
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Mapear columnas</CardTitle>
        <CardDescription>
          {analysis.archivoNombre} · {analysis.filasDeDatos.toLocaleString("es-CO")} filas de datos · {mappedCount} de{" "}
          {analysis.columnas.length} columnas asignadas
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <Label htmlFor="header-row">Fila de los títulos</Label>
            <div className="flex gap-2">
              <Input
                id="header-row"
                type="number"
                min={1}
                className="w-24"
                value={headerRow}
                onChange={(e) => setHeaderRow(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && applyHeaderRow()}
              />
              <Button
                variant="outline"
                onClick={applyHeaderRow}
                disabled={wizard.analyzing || headerRow === String(analysis.filaEncabezados)}
              >
                {wizard.analyzing && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Usar esta fila
              </Button>
            </div>
          </div>
          <p className="max-w-md text-sm text-muted-foreground">
            Se detectó en la fila {analysis.filaEncabezados}. Si los títulos están en otra, cámbiala y se vuelve a leer.
          </p>
        </div>

        <ResponsiveTable>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-16">Columna</TableHead>
                <TableHead>Título en el archivo</TableHead>
                <TableHead>Ejemplos</TableHead>
                <TableHead className="w-[260px]">Campo del sistema</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {analysis.columnas.map((col) => {
                const field = fieldByColumn.get(col.index);
                return (
                  <TableRow key={col.index} data-testid={`map-row-${col.letter}`}>
                    <TableCell className="font-mono text-muted-foreground">{col.letter}</TableCell>
                    <TableCell className="font-medium">{col.header}</TableCell>
                    <TableCell className="max-w-[260px] truncate text-muted-foreground" title={col.samples.join(" · ")}>
                      {col.samples.join(" · ") || "—"}
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <Select
                          value={field ?? NONE}
                          onValueChange={(v) => wizard.assignColumn(col.index, v === NONE ? null : v)}
                        >
                          <SelectTrigger className="w-full" aria-label={`Campo para la columna ${col.letter} (${col.header})`}>
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value={NONE}>No importar</SelectItem>
                            <FieldItems fields={general} />
                            {groups.map((g) => (
                              <SelectGroup key={g}>
                                <SelectLabel>{g}</SelectLabel>
                                <FieldItems fields={analysis.campos.filter((f) => f.group === g)} />
                              </SelectGroup>
                            ))}
                          </SelectContent>
                        </Select>
                        {field && suggested.get(col.index) === field && (
                          <Wand2 className="h-4 w-4 shrink-0 text-muted-foreground" aria-label="Asignada automáticamente">
                            <title>Asignada automáticamente</title>
                          </Wand2>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </ResponsiveTable>

        {wizard.missing.length > 0 && (
          <div role="alert" className="flex gap-2 rounded-xl bg-sand-soft p-3 text-sm text-sand-foreground dark:text-sand">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>
              Falta asignar: {wizard.missing.map((f) => f.label).join(", ")}.
              {wizard.missing.some((f) => f.key === "nombre" || f.key === "apellido") &&
                " Si nombre y apellido vienen juntos, asigna esa columna a «Nombre completo»."}
            </p>
          </div>
        )}

        <div className="flex flex-wrap items-center justify-between gap-2">
          <Button variant="outline" onClick={wizard.reset}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Otro archivo
          </Button>
          <Button onClick={() => void wizard.loadPreview()} disabled={wizard.missing.length > 0 || wizard.previewing} data-testid="import-to-preview">
            {wizard.previewing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Revisar datos
            <ArrowRight className="ml-2 h-4 w-4" />
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          Fechas: {analysis.formatosDeFecha.length} formatos reconocidos ({analysis.formatosDeFecha.slice(2, 5).join(", ")}, mes en letras,
          serial de Excel…).
        </p>
      </CardContent>
    </Card>
  );
}

function FieldItems({ fields }: { fields: ImportField[] }) {
  return (
    <>
      {fields.map((f) => (
        <SelectItem key={f.key} value={f.key}>
          <span className="flex items-center gap-2">
            {f.label}
            {f.required && <Badge variant="outline">obligatorio</Badge>}
          </span>
        </SelectItem>
      ))}
    </>
  );
}

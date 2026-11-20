// ============================================================
// ResultStep.tsx — Paso 4: qué pasó (creadas / actualizadas / omitidas / con error)
// ============================================================
import { Link, type LinkProps } from "@tanstack/react-router";
import { CheckCircle2, FileUp, History } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import type { ImportWizardState } from "@/hooks/use-import-wizard";

export function ResultStep({ wizard, noun, count, backTo }: {
  wizard: ImportWizardState;
  noun: string;
  count: (n: number) => string;
  backTo: NonNullable<LinkProps["to"]>;
}) {
  const r = wizard.result!;
  const items = [
    { label: "Creados", value: r.creadas },
    { label: "Actualizados", value: r.actualizadas },
    { label: "Omitidos (ya existían)", value: r.omitidas },
    { label: "Con error (no se importaron)", value: r.conError },
  ];

  return (
    <Card>
      <CardContent className="space-y-6 pt-6">
        <div className="flex items-center gap-3">
          <CheckCircle2 className="h-10 w-10 shrink-0 text-success" />
          <div>
            <p className="text-xl font-semibold" data-testid="import-result">
              Importación terminada: {count(r.creadas)} {r.creadas === 1 ? "nuevo" : "nuevos"}
              {r.actualizadas > 0 && `, ${r.actualizadas.toLocaleString("es-CO")} ${r.actualizadas === 1 ? "actualizado" : "actualizados"}`}
            </p>
            <p className="text-sm text-muted-foreground">
              {r.detalle && <span data-testid="import-result-detail">{r.detalle} · </span>}
              {wizard.file?.name} · {r.total.toLocaleString("es-CO")} filas · queda registrada en el historial
            </p>
          </div>
        </div>
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {items.map((i) => (
            <div key={i.label} className="rounded-xl border bg-card p-3">
              <dt className="text-xs text-muted-foreground">{i.label}</dt>
              <dd className="text-2xl font-semibold">{i.value.toLocaleString("es-CO")}</dd>
            </div>
          ))}
        </dl>
        <div className="flex flex-wrap gap-2">
          <Button asChild>
            <Link to={backTo}>Ver {noun}</Link>
          </Button>
          <Button variant="outline" onClick={wizard.reset}>
            <FileUp className="mr-2 h-4 w-4" />
            Importar otro archivo
          </Button>
          <Button variant="ghost" asChild>
            <Link to="/imports">
              <History className="mr-2 h-4 w-4" />
              Historial de importaciones
            </Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

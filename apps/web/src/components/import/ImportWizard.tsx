// ============================================================
// ImportWizard.tsx — Asistente de importación en 4 pasos (genérico por entidad)
//
//   1. Subir   2. Mapear columnas   3. Revisar   4. Importar
//
// Sirve para cualquier entidad que el backend sepa importar (hoy pacientes): los campos, el
// mapeo propuesto y las reglas llegan del API. El estado vive en useImportWizard.
// Cancelar sale sin guardar nada (el backend solo escribe en "Importar", en una transacción).
// ============================================================
import { Link, useNavigate, type LinkProps } from "@tanstack/react-router";
import { Check, History, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageContainer } from "@/components/shared/PageContainer";
import { useImportWizard, type ImportStep } from "@/hooks/use-import-wizard";
import { cn } from "@/lib/utils";
import type { ImportEntity } from "@/types/import";
import { UploadStep } from "./UploadStep";
import { MappingStep } from "./MappingStep";
import { PreviewStep } from "./PreviewStep";
import { ResultStep } from "./ResultStep";

const STEPS: { key: ImportStep; label: string }[] = [
  { key: "upload", label: "Subir archivo" },
  { key: "mapping", label: "Mapear columnas" },
  { key: "preview", label: "Revisar" },
  { key: "result", label: "Importar" },
];

interface ImportWizardProps {
  entity: ImportEntity;
  title: string;
  /** "pacientes": para los textos ("Importar 120 pacientes"). */
  noun: string;
  /** "paciente": para "1 paciente nuevo", "Importar 1 paciente". */
  nounOne: string;
  /** Cómo se reconoce un duplicado, para la vista previa: "mismo documento". */
  duplicateKey: string;
  backTo: NonNullable<LinkProps["to"]>;
}

export function ImportWizard({ entity, title, noun, nounOne, duplicateKey, backTo }: ImportWizardProps) {
  const navigate = useNavigate();
  const wizard = useImportWizard(entity);
  const current = STEPS.findIndex((s) => s.key === wizard.step);
  const count = (n: number) => `${n.toLocaleString("es-CO")} ${n === 1 ? nounOne : noun}`;

  return (
    <PageContainer>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-3xl font-bold">{title}</h1>
          <p className="text-muted-foreground">
            Desde un Excel (.xlsx, .xls) o CSV. El archivo no se guarda en el servidor: se lee y se descarta.
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" asChild>
            <Link to="/imports">
              <History className="mr-2 h-4 w-4" />
              Historial
            </Link>
          </Button>
          {wizard.step !== "result" && (
            <Button
              variant="ghost"
              onClick={() => navigate({ to: backTo })}
              disabled={wizard.importing}
              data-testid="import-cancel"
            >
              <X className="mr-2 h-4 w-4" />
              Cancelar
            </Button>
          )}
        </div>
      </div>

      <ol className="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Pasos de la importación">
        {STEPS.map((s, i) => (
          <li
            key={s.key}
            aria-current={i === current ? "step" : undefined}
            className={cn(
              "flex items-center gap-2 rounded-xl border px-3 py-2 text-sm",
              i === current && "border-primary bg-primary/5 font-medium text-foreground",
              i < current && "text-foreground",
              i > current && "text-muted-foreground",
            )}
          >
            <span
              className={cn(
                "flex h-6 w-6 shrink-0 items-center justify-center rounded-full border text-xs",
                i < current && "border-transparent bg-success-soft text-success",
                i === current && "border-transparent bg-primary text-primary-foreground",
              )}
            >
              {i < current ? <Check className="h-3.5 w-3.5" /> : i + 1}
            </span>
            <span className="truncate">{s.label}</span>
          </li>
        ))}
      </ol>

      {wizard.step === "upload" && <UploadStep wizard={wizard} />}
      {wizard.step === "mapping" && <MappingStep wizard={wizard} />}
      {wizard.step === "preview" && <PreviewStep wizard={wizard} count={count} duplicateKey={duplicateKey} />}
      {wizard.step === "result" && <ResultStep wizard={wizard} noun={noun} count={count} backTo={backTo} />}
    </PageContainer>
  );
}

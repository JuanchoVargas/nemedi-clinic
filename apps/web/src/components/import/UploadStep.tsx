// ============================================================
// UploadStep.tsx — Paso 1: elegir el archivo (arrastrar o hacer clic)
// ============================================================
import { useRef, useState, type DragEvent } from "react";
import { FileSpreadsheet, Loader2, Upload } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { ImportWizardState } from "@/hooks/use-import-wizard";

export function UploadStep({ wizard }: { wizard: ImportWizardState }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    const file = event.dataTransfer.files[0];
    if (file && !wizard.analyzing) void wizard.chooseFile(file);
  };

  return (
    <Card>
      <CardContent className="space-y-4 pt-6">
        <div
          role="button"
          tabIndex={0}
          aria-label="Elegir archivo para importar"
          aria-busy={wizard.analyzing}
          onClick={() => !wizard.analyzing && inputRef.current?.click()}
          onKeyDown={(e) => {
            if ((e.key === "Enter" || e.key === " ") && !wizard.analyzing) {
              e.preventDefault();
              inputRef.current?.click();
            }
          }}
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          className={cn(
            "flex cursor-pointer flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed px-6 py-12 text-center transition-colors",
            dragging ? "border-primary bg-primary/5" : "border-border hover:bg-muted/50",
            wizard.analyzing && "cursor-wait opacity-70",
          )}
        >
          {wizard.analyzing ? (
            <Loader2 className="h-10 w-10 animate-spin text-primary" />
          ) : (
            <Upload className="h-10 w-10 text-primary" />
          )}
          <div>
            <p className="font-medium">{wizard.analyzing ? "Leyendo el archivo…" : "Arrastra el archivo aquí o haz clic para elegirlo"}</p>
            <p className="text-sm text-muted-foreground">Excel (.xlsx, .xls) o CSV · hasta 10 MB y 5.000 filas · se lee la primera hoja</p>
          </div>
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx,.xls,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel,text/csv"
            className="hidden"
            data-testid="import-file"
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = ""; // permite volver a elegir el mismo archivo
              if (file) void wizard.chooseFile(file);
            }}
          />
        </div>
        {wizard.fileError && (
          <p role="alert" className="text-sm text-destructive">
            {wizard.fileError}
          </p>
        )}
        <div className="flex gap-3 rounded-xl bg-muted/50 p-4 text-sm text-muted-foreground">
          <FileSpreadsheet className="mt-0.5 h-5 w-5 shrink-0" />
          <p>
            No hace falta preparar el archivo: los títulos pueden estar en cualquier fila y las columnas en cualquier orden. En el
            siguiente paso eliges qué columna va a cada campo; las que no asignes se ignoran.
          </p>
        </div>
      </CardContent>
    </Card>
  );
}

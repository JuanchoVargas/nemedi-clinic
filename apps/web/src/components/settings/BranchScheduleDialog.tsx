// ============================================================
// BranchScheduleDialog.tsx — Horario de atención de una sede
//
// Un renglón por día con hasta dos franjas (mañana y tarde). Un día sin marcar
// queda cerrado; si no se marca ninguno, la sede no restringe el horario.
// El backend valida que las franjas no se crucen y que cierren después de abrir.
// ============================================================
import { useState } from "react";
import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { FormDialog } from "@/components/shared/FormDialog";
import { useBranchSchedule, useSaveBranchSchedule } from "@/api/settings.api";
import { useToastStore } from "@/stores/toast.store";
import type { Branch } from "@/types/branch";
import { DIAS_SEMANA, type BranchScheduleDay } from "@/types/settings";

interface Franja {
  desde: string;
  hasta: string;
}
type Semana = Record<number, Franja[]>;

// Lunes primero, como se piensa una semana de trabajo
const ORDEN = [1, 2, 3, 4, 5, 6, 0];

function toSemana(dias: BranchScheduleDay[]): Semana {
  const semana: Semana = { 0: [], 1: [], 2: [], 3: [], 4: [], 5: [], 6: [] };
  for (const d of dias) semana[d.diaSemana].push({ desde: d.horaApertura.slice(0, 5), hasta: d.horaCierre.slice(0, 5) });
  return semana;
}

function fromSemana(semana: Semana): BranchScheduleDay[] {
  return ORDEN.flatMap((dia) =>
    semana[dia].map((f) => ({ diaSemana: dia, horaApertura: `${f.desde}:00`, horaCierre: `${f.hasta}:00` })),
  );
}

export function BranchScheduleDialog({ branch, onClose }: { branch: Branch; onClose: () => void }) {
  const { data, isLoading } = useBranchSchedule(branch.id);
  return isLoading || !data ? (
    <FormDialog open onOpenChange={(o) => !o && onClose()} title={`Horario de ${branch.nombre}`}>
      <Skeleton className="h-64 w-full" />
    </FormDialog>
  ) : (
    <ScheduleForm branch={branch} initial={toSemana(data)} onClose={onClose} />
  );
}

function ScheduleForm({ branch, initial, onClose }: { branch: Branch; initial: Semana; onClose: () => void }) {
  const save = useSaveBranchSchedule(branch.id);
  const [semana, setSemana] = useState<Semana>(initial);
  const [dirty, setDirty] = useState(false);

  const update = (dia: number, franjas: Franja[]) => {
    setSemana((s) => ({ ...s, [dia]: franjas }));
    setDirty(true);
  };
  const invalida = ORDEN.some((d) => semana[d].some((f) => !f.desde || !f.hasta || f.hasta <= f.desde));
  const sinHorario = ORDEN.every((d) => semana[d].length === 0);

  const onSubmit = async () => {
    try {
      await save.mutateAsync(fromSemana(semana));
      useToastStore.success("Horario guardado", branch.nombre);
      onClose();
    } catch {
      // toast global (franjas cruzadas)
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title={`Horario de ${branch.nombre}`}
      description="Las citas solo se pueden agendar dentro de estas franjas. Un día apagado queda cerrado."
      dirty={dirty}
      actions={
        <Button onClick={() => void onSubmit()} disabled={invalida || save.isPending}>
          {save.isPending ? "Guardando..." : "Guardar horario"}
        </Button>
      }
    >
      <ul className="space-y-3 pt-1">
        {ORDEN.map((dia) => {
          const franjas = semana[dia];
          const abierto = franjas.length > 0;
          return (
            <li key={dia} className="grid grid-cols-[6.5rem_1fr] items-start gap-3">
              <label className="flex h-9 items-center gap-2 text-sm font-medium">
                <Switch
                  checked={abierto}
                  onCheckedChange={(on) => update(dia, on ? [{ desde: "08:00", hasta: "18:00" }] : [])}
                  aria-label={`Abierto el ${DIAS_SEMANA[dia]}`}
                />
                {DIAS_SEMANA[dia].slice(0, 3)}
              </label>
              {abierto ? (
                <div className="space-y-2">
                  {franjas.map((f, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <Input
                        type="time"
                        value={f.desde}
                        onChange={(e) => update(dia, franjas.map((x, j) => (j === i ? { ...x, desde: e.target.value } : x)))}
                        className="w-28"
                        aria-label={`Abre el ${DIAS_SEMANA[dia]}`}
                      />
                      <span className="text-muted-foreground">a</span>
                      <Input
                        type="time"
                        value={f.hasta}
                        onChange={(e) => update(dia, franjas.map((x, j) => (j === i ? { ...x, hasta: e.target.value } : x)))}
                        className="w-28"
                        aria-label={`Cierra el ${DIAS_SEMANA[dia]}`}
                      />
                      {franjas.length > 1 && (
                        <Button variant="ghost" size="icon" onClick={() => update(dia, franjas.filter((_, j) => j !== i))} aria-label="Quitar franja">
                          <X className="h-4 w-4" />
                        </Button>
                      )}
                    </div>
                  ))}
                  {franjas.length < 2 && (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 px-2 text-xs"
                      onClick={() => update(dia, [...franjas, { desde: "14:00", hasta: "18:00" }])}
                    >
                      <Plus className="mr-1 h-3 w-3" />
                      Otra franja
                    </Button>
                  )}
                </div>
              ) : (
                <p className="flex h-9 items-center text-sm text-muted-foreground">Cerrado</p>
              )}
            </li>
          );
        })}
      </ul>
      {sinHorario && <p className="mt-3 text-xs text-muted-foreground">Sin ningún día abierto la sede no restringe el horario de las citas.</p>}
      {invalida && <p className="mt-3 text-xs text-destructive">Revisa las franjas: cada una debe cerrar después de abrir.</p>}
    </FormDialog>
  );
}

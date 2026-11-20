// ============================================================
// AppointmentDialogs.tsx — Reagendar y cancelar una cita
//
// Reagendar: nueva fecha/hora (y opcionalmente otra esteticista); el backend
// conserva la duración y valida choque, horario de la sede y anticipación mínima.
// Cancelar: el motivo es obligatorio (lo exige el backend).
// ============================================================
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { FormDialog } from "@/components/shared/FormDialog";
import { useRescheduleAppointment, useUpdateAppointmentStatus } from "@/api/appointments.api";
import { useEsteticistas } from "@/api/users.api";
import { toLocalDateTimeInput, toLocalIso } from "@/lib/dates";
import { useToastStore } from "@/stores/toast.store";
import type { AppointmentDto } from "@/types/appointment";

/** Motivos frecuentes: un clic llena el campo, que se puede seguir editando. */
const MOTIVOS = ["La paciente canceló", "No se pudo contactar", "Reprogramada", "Enfermedad", "Error al agendar"];

export function CancelAppointmentDialog({ appointment, onClose, onDone }: { appointment: AppointmentDto; onClose: () => void; onDone: () => void }) {
  const update = useUpdateAppointmentStatus();
  const [motivo, setMotivo] = useState("");

  const onConfirm = async () => {
    try {
      await update.mutateAsync({ id: appointment.id, body: { estado: "Cancelada", motivo: motivo.trim() } });
      useToastStore.success("Cita marcada como Cancelada", motivo.trim());
      onDone();
    } catch {
      // toast global
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Cancelar cita"
      description={`${appointment.patientNombre} · ${appointment.procedureNombre}`}
      dirty={motivo.length > 0}
      cancelLabel="Volver"
      actions={
        <Button variant="destructive" disabled={!motivo.trim() || update.isPending} onClick={() => void onConfirm()}>
          {update.isPending ? "Cancelando..." : "Cancelar cita"}
        </Button>
      }
    >
      <div className="space-y-3 pt-1">
        <Label htmlFor="motivo-cancelacion">Motivo *</Label>
        <div className="flex flex-wrap gap-2">
          {MOTIVOS.map((m) => (
            <Button key={m} type="button" variant="outline" size="sm" className="h-7 text-xs" onClick={() => setMotivo(m)}>
              {m}
            </Button>
          ))}
        </div>
        <Textarea id="motivo-cancelacion" rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Por qué no se hará la cita" />
      </div>
    </FormDialog>
  );
}

export function RescheduleAppointmentDialog({
  appointment,
  lockedToSelf,
  onClose,
  onDone,
}: {
  appointment: AppointmentDto;
  /** La esteticista solo mueve sus citas y no puede pasarlas a otra. */
  lockedToSelf: boolean;
  onClose: () => void;
  onDone: () => void;
}) {
  const reschedule = useRescheduleAppointment();
  const { data: esteticistas } = useEsteticistas();
  const inicial = toLocalDateTimeInput(new Date(appointment.fechaInicio));
  const [fecha, setFecha] = useState(inicial);
  const [esteticistId, setEsteticistId] = useState(appointment.esteticistId);

  const onConfirm = async () => {
    try {
      await reschedule.mutateAsync({
        id: appointment.id,
        body: { fechaInicio: toLocalIso(new Date(fecha)), esteticistId: esteticistId !== appointment.esteticistId ? esteticistId : undefined },
      });
      useToastStore.success(
        "Cita reagendada",
        new Date(fecha).toLocaleString("es-CO", { weekday: "long", day: "numeric", month: "long", hour: "numeric", minute: "2-digit" }),
      );
      onDone();
    } catch {
      // toast global (choque, fuera del horario, anticipación)
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Reagendar cita"
      description={`${appointment.patientNombre} · ${appointment.procedureNombre}. Conserva la duración.`}
      dirty={fecha !== inicial || esteticistId !== appointment.esteticistId}
      actions={
        <Button disabled={!fecha || reschedule.isPending} onClick={() => void onConfirm()}>
          {reschedule.isPending ? "Guardando..." : "Reagendar"}
        </Button>
      }
    >
      <div className="space-y-4 pt-1">
        <div className="space-y-1.5">
          <Label htmlFor="reagendar-fecha">Nueva fecha y hora</Label>
          <Input id="reagendar-fecha" type="datetime-local" value={fecha} onChange={(e) => setFecha(e.target.value)} />
        </div>
        {!lockedToSelf && (
          <div className="space-y-1.5">
            <Label>Esteticista</Label>
            <Select value={esteticistId} onValueChange={setEsteticistId}>
              <SelectTrigger className="w-full" aria-label="Esteticista">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {esteticistas?.map((e) => (
                  <SelectItem key={e.id} value={e.id}>
                    {e.nombre} {e.apellido}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
        <p className="text-xs text-muted-foreground">También puedes arrastrar la cita en el calendario.</p>
      </div>
    </FormDialog>
  );
}

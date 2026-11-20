// ============================================================
// SessionDetail.tsx — Columna derecha de una sesión en Evolución
//
// Grilla de dos columnas con lo que se registró de la sesión. **Cada bloque
// solo aparece si tiene contenido**: una ficha con diez "—" no dice nada, y
// la mayoría de las notas llenan dos o tres campos.
// ============================================================
import { useState } from "react";
import { CalendarPlus, Clock, ExternalLink, FilePlus2, Pencil, UserRound } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { Button } from "@/components/ui/button";
import { formatShortDate } from "@/lib/format-platform";
import { cn } from "@/lib/utils";
import { ClinicalNoteDialog } from "@/components/patient/ClinicalNoteDialog";
import { EditClinicalNoteDialog } from "@/components/patient/EditClinicalNoteDialog";
import { usePermissions } from "@/hooks/use-permissions";
import type { EvolutionSession } from "@/types/clinical-record";

/** Un par etiqueta/valor de la grilla. No se pinta si no hay valor. */
function Campo({ label, children, wide }: { label: string; children: React.ReactNode; wide?: boolean }) {
  return (
    <div className={cn(wide && "sm:col-span-2")}>
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</p>
      <div className="mt-0.5 text-sm whitespace-pre-wrap">{children}</div>
    </div>
  );
}

/** Cómo se sintió la paciente: cinco puntos, los marcados en color. */
function Evaluacion({ valor }: { valor: number }) {
  return (
    <span className="inline-flex items-center gap-1.5" aria-label={`Evaluación de la paciente: ${valor} de 5`}>
      <span className="flex gap-1" aria-hidden>
        {[1, 2, 3, 4, 5].map((n) => (
          <span
            key={n}
            className={cn("h-2.5 w-2.5 rounded-full", n <= valor ? "bg-sand" : "bg-muted-foreground/25")}
          />
        ))}
      </span>
      <span className="text-muted-foreground">{valor} de 5</span>
    </span>
  );
}

interface SessionDetailProps {
  session: EvolutionSession;
  patientId: string;
}

export function SessionDetail({ session, patientId }: SessionDetailProps) {
  const { can } = usePermissions();
  const [editing, setEditing] = useState(false);
  const [aclarando, setAclarando] = useState(false);
  const {
    observaciones, zonaTratada, parametros, indicacionesPost,
    proximaSesionSugerida, evaluacionPaciente, productos, productosUsados,
  } = session;

  // Enlace a "agendar" con paciente, procedimiento y fecha ya puestos
  const agendarSearch = {
    patientId,
    ...(session.procedureId ? { procedureId: session.procedureId } : {}),
    ...(proximaSesionSugerida ? { fecha: proximaSesionSugerida } : {}),
  };

  return (
    <div className="space-y-4">
      <div className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
        <Campo label="Observaciones clínicas" wide>
          {observaciones}
        </Campo>

        {zonaTratada && <Campo label="Zona tratada">{zonaTratada}</Campo>}
        {parametros && <Campo label="Parámetros">{parametros}</Campo>}

        {productos.length > 0 && (
          <Campo label="Productos usados" wide={!zonaTratada && !parametros}>
            <ul className="space-y-0.5">
              {productos.map((p) => (
                <li key={p.productId} className="flex justify-between gap-3 tabular-nums">
                  <span className="min-w-0 truncate">{p.nombre}</span>
                  <span className="shrink-0 text-muted-foreground">
                    {p.cantidad.toLocaleString("es-CO")} {p.unidadMedida}
                  </span>
                </li>
              ))}
            </ul>
          </Campo>
        )}
        {/* Notas viejas: el texto libre de antes del consumo de cabina */}
        {!productos.length && productosUsados && <Campo label="Productos usados">{productosUsados}</Campo>}

        {indicacionesPost && (
          <Campo label="Indicaciones para la paciente" wide>
            {indicacionesPost}
          </Campo>
        )}

        {evaluacionPaciente != null && (
          <Campo label="Cómo se sintió la paciente">
            <Evaluacion valor={evaluacionPaciente} />
          </Campo>
        )}

        {proximaSesionSugerida && (
          <Campo label="Próxima sesión sugerida">
            <span className="flex flex-wrap items-center gap-2">
              {formatShortDate(proximaSesionSugerida)}
              <Button asChild size="sm" variant="outline" className="h-7">
                <Link to="/calendar" search={agendarSearch}>
                  <CalendarPlus className="mr-1.5 h-3.5 w-3.5" aria-hidden />
                  Agendar
                </Link>
              </Button>
            </span>
          </Campo>
        )}
      </div>

      <footer className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t pt-3 text-xs text-muted-foreground">
        <span className="inline-flex items-center gap-1.5">
          <UserRound className="h-3.5 w-3.5" aria-hidden />
          {session.esteticista}
        </span>
        {session.duracionMinutos != null && (
          <span className="inline-flex items-center gap-1.5">
            <Clock className="h-3.5 w-3.5" aria-hidden />
            {session.duracionMinutos} min
          </span>
        )}
        {session.appointmentId && (
          <Link
            to="/calendar"
            search={{ appointmentId: session.appointmentId }}
            className="inline-flex items-center gap-1.5 underline-offset-4 hover:underline"
          >
            <ExternalLink className="h-3.5 w-3.5" aria-hidden />
            Ver la cita
          </Link>
        )}
        {session.fechaEdicion && <span>Corregida el {formatShortDate(session.fechaEdicion)}</span>}
        <span className="ml-auto flex gap-2">
          {session.editable ? (
            <Button size="sm" variant="ghost" className="h-7" onClick={() => setEditing(true)}>
              <Pencil className="mr-1.5 h-3.5 w-3.5" aria-hidden />
              Corregir
            </Button>
          ) : (
            can("clinical.note.create") && (
              <Button size="sm" variant="ghost" className="h-7" onClick={() => setAclarando(true)} title="La nota tiene más de 24 h: se aclara con una nota nueva">
                <FilePlus2 className="mr-1.5 h-3.5 w-3.5" aria-hidden />
                Nota aclaratoria
              </Button>
            )
          )}
        </span>
      </footer>
      {editing && <EditClinicalNoteDialog patientId={patientId} session={session} onClose={() => setEditing(false)} />}
      {aclarando && (
        <ClinicalNoteDialog
          patientId={patientId}
          defaults={{
            procedimiento: session.procedimiento,
            observaciones: `Aclaración a la nota del ${formatShortDate(session.fecha)}: `,
          }}
          onClose={() => setAclarando(false)}
        />
      )}
    </div>
  );
}

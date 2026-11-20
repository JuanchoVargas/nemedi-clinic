// ============================================================
// EditClinicalNoteDialog.tsx — Corregir una nota clínica (dentro de 24 h)
//
// La historia clínica no se altera: una nota solo se corrige el mismo día que se
// escribió (24 h) y por su autora o recepción/dueño. Pasado ese plazo la ficha
// ofrece "Nota aclaratoria" en su lugar. El consumo de cabina no se corrige aquí:
// el inventario ya se movió. Se pueden agregar fotos nuevas.
// ============================================================
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { FormDialog } from "@/components/shared/FormDialog";
import { PhotoListUpload } from "@/components/shared/PhotoListUpload";
import { SatisfactionPicker } from "@/components/patient/SatisfactionPicker";
import { useUpdateClinicalNote } from "@/api/clinical-records.api";
import { useToastStore } from "@/stores/toast.store";
import type { EvolutionSession } from "@/types/clinical-record";

const schema = z.object({
  procedimiento: z.string().min(1, "Requerido").max(200),
  observaciones: z.string().min(1, "Describe la sesión").max(4000),
  zonaTratada: z.string().max(150),
  parametros: z.string().max(300),
  indicacionesPost: z.string().max(1000),
  proximaSesionSugerida: z.string(),
  evaluacionPaciente: z.number().int().min(1).max(5).nullable(),
  fotosAntes: z.array(z.string()),
  fotosDespues: z.array(z.string()),
});
type Values = z.infer<typeof schema>;

export function EditClinicalNoteDialog({ patientId, session, onClose }: { patientId: string; session: EvolutionSession; onClose: () => void }) {
  const update = useUpdateClinicalNote(patientId);
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      procedimiento: session.procedimiento,
      observaciones: session.observaciones,
      zonaTratada: session.zonaTratada ?? "",
      parametros: session.parametros ?? "",
      indicacionesPost: session.indicacionesPost ?? "",
      proximaSesionSugerida: session.proximaSesionSugerida ?? "",
      evaluacionPaciente: session.evaluacionPaciente,
      fotosAntes: [],
      fotosDespues: [],
    },
  });

  const onSubmit = async (v: Values) => {
    try {
      await update.mutateAsync({
        noteId: session.noteId,
        body: {
          procedimiento: v.procedimiento.trim(),
          observaciones: v.observaciones.trim(),
          zonaTratada: v.zonaTratada.trim() || undefined,
          parametros: v.parametros.trim() || undefined,
          indicacionesPost: v.indicacionesPost.trim() || undefined,
          proximaSesionSugerida: v.proximaSesionSugerida || undefined,
          evaluacionPaciente: v.evaluacionPaciente ?? undefined,
          adjuntoIds: [...v.fotosAntes, ...v.fotosDespues],
        },
      });
      useToastStore.success("Nota corregida", session.procedimiento);
      onClose();
    } catch {
      // toast global (409 si ya pasaron las 24 h)
    }
  };

  const texto = (name: "zonaTratada" | "parametros", label: string) => (
    <FormField
      control={form.control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FormLabel>{label}</FormLabel>
          <FormControl>
            <Input {...field} />
          </FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
  );

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Corregir nota clínica"
      description="Solo durante las 24 horas siguientes a escribirla. Los productos usados no se corrigen aquí."
      dirty={form.formState.isDirty}
      className="sm:max-w-2xl"
      actions={
        <Button form="edit-note-form" type="submit" disabled={update.isPending}>
          {update.isPending ? "Guardando..." : "Guardar corrección"}
        </Button>
      }
    >
      <Form {...form}>
        <form id="edit-note-form" onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 pt-1">
          <FormField
            control={form.control}
            name="procedimiento"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Procedimiento *</FormLabel>
                <FormControl>
                  <Input {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="observaciones"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Observaciones clínicas *</FormLabel>
                <FormControl>
                  <Textarea rows={4} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            {texto("zonaTratada", "Zona tratada")}
            {texto("parametros", "Parámetros")}
          </div>
          <FormField
            control={form.control}
            name="indicacionesPost"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Indicaciones para la paciente</FormLabel>
                <FormControl>
                  <Textarea rows={2} {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="proximaSesionSugerida"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Próxima sesión sugerida</FormLabel>
                  <FormControl>
                    <Input type="date" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="evaluacionPaciente"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Cómo se sintió la paciente</FormLabel>
                  <SatisfactionPicker value={field.value} onChange={field.onChange} />
                </FormItem>
              )}
            />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="fotosAntes"
              render={({ field }) => <PhotoListUpload label="Agregar fotos: antes" kind="Antes" value={field.value} onChange={field.onChange} />}
            />
            <FormField
              control={form.control}
              name="fotosDespues"
              render={({ field }) => <PhotoListUpload label="Agregar fotos: después" kind="Despues" value={field.value} onChange={field.onChange} />}
            />
          </div>
        </form>
      </Form>
    </FormDialog>
  );
}

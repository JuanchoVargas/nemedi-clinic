// ============================================================
// PatientRipsFields.tsx — Datos que RIPS exige del paciente
//
// Solo se monta cuando la clínica es IPS (useIsIps). El municipio y la EPS aparecen
// según lo elegido: el municipio DANE solo para quien reside en Colombia y la
// entidad solo en los regímenes afiliados (contributivo, subsidiado, especial).
// La obligatoriedad la pone el schema del formulario (patientSchema), no este componente.
// ============================================================

import { useWatch, type Control } from "react-hook-form";
import { FormDescription, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { OptionSelect } from "@/components/shared/OptionSelect";
import {
  COLOMBIA, MUNICIPIOS_FRECUENTES, PAISES, SEXOS, TIPOS_DOCUMENTO, TIPOS_USUARIO, ZONAS, requiereEntidad,
} from "@/lib/rips";

/** Campos RIPS del formulario de paciente (todos texto; "" = sin dato). */
export interface PatientRipsValues {
  tipoDocumento: string;
  sexo: string;
  paisResidencia: string;
  municipioResidencia: string;
  zonaResidencia: string;
  tipoUsuario: string;
  codigoEntidad: string;
  nombreEntidad: string;
}

export function PatientRipsFields({ control }: { control: Control<PatientRipsValues> }) {
  const pais = useWatch({ control, name: "paisResidencia" });
  const tipoUsuario = useWatch({ control, name: "tipoUsuario" });

  return (
    <fieldset className="rounded-xl border border-border p-4 space-y-4" aria-label="Datos RIPS">
      <legend className="px-2 font-heading text-sm font-semibold">Datos RIPS</legend>
      <p className="text-xs text-muted-foreground -mt-2">
        La clínica es prestador habilitado (IPS): el Ministerio exige estos datos en el reporte RIPS.
      </p>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <FormField
          control={control}
          name="tipoDocumento"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Tipo de documento *</FormLabel>
              <OptionSelect value={field.value} onChange={field.onChange} options={TIPOS_DOCUMENTO} />
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name="sexo"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Sexo *</FormLabel>
              <OptionSelect value={field.value} onChange={field.onChange} options={SEXOS} />
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name="paisResidencia"
          render={({ field }) => (
            <FormItem>
              <FormLabel>País de residencia *</FormLabel>
              <OptionSelect value={field.value} onChange={field.onChange} options={PAISES} />
              <FormMessage />
            </FormItem>
          )}
        />
        {pais === COLOMBIA && (
          <FormField
            control={control}
            name="municipioResidencia"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Municipio de residencia (DANE) *</FormLabel>
                <FormControl>
                  <Input {...field} list="dane-municipios" inputMode="numeric" maxLength={5} placeholder="11001" />
                </FormControl>
                <datalist id="dane-municipios">
                  {MUNICIPIOS_FRECUENTES.map((m) => (
                    <option key={m.value} value={m.value}>{m.label}</option>
                  ))}
                </datalist>
                <FormDescription>Código DANE de 5 dígitos (Bogotá = 11001).</FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        )}
        <FormField
          control={control}
          name="zonaResidencia"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Zona de residencia *</FormLabel>
              <OptionSelect value={field.value} onChange={field.onChange} options={ZONAS} />
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name="tipoUsuario"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Régimen de afiliación *</FormLabel>
              <OptionSelect value={field.value} onChange={field.onChange} options={TIPOS_USUARIO} />
              <FormMessage />
            </FormItem>
          )}
        />
        {requiereEntidad(tipoUsuario) && (
          <>
            <FormField
              control={control}
              name="codigoEntidad"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Código de la EPS *</FormLabel>
                  <FormControl><Input {...field} maxLength={10} placeholder="EPS005" /></FormControl>
                  <FormDescription>Código de la entidad ante la Supersalud.</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={control}
              name="nombreEntidad"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Nombre de la EPS</FormLabel>
                  <FormControl><Input {...field} maxLength={150} /></FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </>
        )}
      </div>
    </fieldset>
  );
}

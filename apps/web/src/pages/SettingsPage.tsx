// ============================================================
// SettingsPage.tsx — Configuración de la clínica (/settings[?tab=])
//
// Todo en un solo lugar: Datos (nombre, NIT, contacto, dirección, logo),
// Sedes (con su horario de atención), Políticas (anticipación mínima para
// agendar, exigir consentimiento), Impuestos (IVA por defecto) y Facturación
// electrónica (cuenta del proveedor, emisor y numeración por sede).
// El dueño edita todo; recepción solo ve y administra las sedes.
// Reemplaza a las antiguas "Mi clínica" (/super/tenants) y "Sedes" (/admin/branches).
// ============================================================
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate, useSearch } from "@tanstack/react-router";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ImageUpload } from "@/components/shared/ImageUpload";
import { PageContainer } from "@/components/shared/PageContainer";
import { BranchesSection } from "@/components/settings/BranchesSection";
import { InvoicingSection } from "@/components/settings/InvoicingSection";
import { useClinicSettings, useUpdateClinicSettings } from "@/api/settings.api";
import { usePermissions } from "@/hooks/use-permissions";
import { useToastStore } from "@/stores/toast.store";
import type { ClinicSettings } from "@/types/settings";

const TABS = ["datos", "sedes", "politicas", "impuestos", "facturacion"] as const;
type Tab = (typeof TABS)[number];

const baseSchema = z.object({
  nombre: z.string().min(1, "Requerido"),
  nit: z.string().min(1, "Requerido"),
  telefono: z.string(),
  email: z.string().min(1, "Requerido").email("Correo inválido"),
  direccion: z.string(),
  ciudad: z.string(),
  logoId: z.string().nullable(),
  anticipacionMinimaHoras: z.number().min(0, "Mínimo 0").max(720, "Máximo 720 horas"),
  exigirConsentimiento: z.boolean(),
  ivaProductos: z.number().min(0, "Mínimo 0").max(100, "Máximo 100"),
  ivaServicios: z.number().min(0, "Mínimo 0").max(100, "Máximo 100"),
  codigoHabilitacionReps: z.string(),
});
type Values = z.infer<typeof baseSchema>;

/** El código REPS se valida si viene y es obligatorio si la clínica es IPS (RipsRules.ValidarReps). */
function settingsSchema(esIps: boolean) {
  return baseSchema.superRefine((v, ctx) => {
    const reps = v.codigoHabilitacionReps.trim();
    if (reps && !/^\d{12}$/.test(reps))
      ctx.addIssue({ code: "custom", path: ["codigoHabilitacionReps"], message: "12 dígitos: 10 del prestador y 2 de la sede" });
    if (esIps && !reps)
      ctx.addIssue({ code: "custom", path: ["codigoHabilitacionReps"], message: "Requerido: la clínica es prestador habilitado (IPS)" });
  });
}

export function SettingsPage() {
  const { can } = usePermissions();
  const esDueno = can("tenants.update");
  const navigate = useNavigate();
  const { tab } = useSearch({ strict: false }) as { tab?: string };
  // Recepción solo administra sedes
  const disponibles: Tab[] = esDueno ? [...TABS] : ["sedes"];
  const activa: Tab = disponibles.includes(tab as Tab) ? (tab as Tab) : disponibles[0];
  const { data, isLoading } = useClinicSettings(esDueno);

  return (
    <PageContainer>
      <div className="mb-6">
        <h1 className="text-3xl font-bold">Configuración</h1>
        <p className="text-muted-foreground">Datos de la clínica, sedes y horarios, políticas de agenda, impuestos y facturación electrónica.</p>
      </div>

      <Tabs value={activa} onValueChange={(v) => void navigate({ to: "/settings", search: { tab: v }, replace: true })}>
        <TabsList className="mb-4 flex-wrap">
          {esDueno && <TabsTrigger value="datos">Datos</TabsTrigger>}
          <TabsTrigger value="sedes">Sedes y horarios</TabsTrigger>
          {esDueno && <TabsTrigger value="politicas">Políticas</TabsTrigger>}
          {esDueno && <TabsTrigger value="impuestos">Impuestos</TabsTrigger>}
          {esDueno && <TabsTrigger value="facturacion">Facturación</TabsTrigger>}
        </TabsList>

        <TabsContent value="sedes">
          <BranchesSection />
        </TabsContent>

        {esDueno && (
          <TabsContent value="facturacion">
            <InvoicingSection />
          </TabsContent>
        )}

        {esDueno &&
          (isLoading || !data ? (
            activa !== "sedes" && activa !== "facturacion" && <Skeleton className="h-72 w-full" />
          ) : (
            // key: al guardar llegan valores nuevos y el formulario se remonta con ellos
            <SettingsForm key={JSON.stringify(data)} settings={data} />
          ))}
      </Tabs>
    </PageContainer>
  );
}

function SettingsForm({ settings }: { settings: ClinicSettings }) {
  const update = useUpdateClinicSettings();
  const form = useForm<Values>({
    resolver: zodResolver(settingsSchema(settings.esIps)),
    defaultValues: {
      nombre: settings.nombre,
      nit: settings.nit,
      telefono: settings.telefono ?? "",
      email: settings.email,
      direccion: settings.direccion ?? "",
      ciudad: settings.ciudad ?? "",
      logoId: settings.logoId ?? null,
      anticipacionMinimaHoras: settings.anticipacionMinimaHoras,
      exigirConsentimiento: settings.exigirConsentimiento,
      ivaProductos: settings.ivaProductos,
      ivaServicios: settings.ivaServicios,
      codigoHabilitacionReps: settings.codigoHabilitacionReps ?? "",
    },
  });

  const onSubmit = async (v: Values) => {
    try {
      await update.mutateAsync({
        ...v,
        telefono: v.telefono.trim(),
        direccion: v.direccion.trim() || null,
        ciudad: v.ciudad.trim() || null,
        codigoHabilitacionReps: v.codigoHabilitacionReps.trim() || null,
      });
      useToastStore.success("Configuración guardada");
    } catch {
      // toast global
    }
  };

  const guardar = (
    <div className="flex justify-end">
      <Button type="submit" disabled={update.isPending || !form.formState.isDirty}>
        {update.isPending ? "Guardando..." : "Guardar cambios"}
      </Button>
    </div>
  );

  const numero = (name: "anticipacionMinimaHoras" | "ivaProductos" | "ivaServicios", label: string, description: string, suffix: string) => (
    <FormField
      control={form.control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FormLabel>{label}</FormLabel>
          <div className="flex items-center gap-2">
            <FormControl>
              <Input
                type="number"
                min={0}
                className="w-28"
                value={Number.isFinite(field.value) ? field.value : ""}
                onChange={(e) => field.onChange(e.target.value === "" ? 0 : e.target.valueAsNumber)}
              />
            </FormControl>
            <span className="text-sm text-muted-foreground">{suffix}</span>
          </div>
          <FormDescription>{description}</FormDescription>
          <FormMessage />
        </FormItem>
      )}
    />
  );

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)}>
        <TabsContent value="datos">
          <Card>
            <CardHeader>
              <CardTitle>Datos de la clínica</CardTitle>
              <CardDescription>Aparecen en el menú y en los consentimientos. El emisor de la factura electrónica se configura en Facturación.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <FormField
                control={form.control}
                name="logoId"
                render={({ field }) => (
                  <ImageUpload entityType="Tenant" kind="Logo" entityId={settings.id} value={field.value} onChange={field.onChange} label="Logo de la clínica" />
                )}
              />
              <div className="grid gap-4 sm:grid-cols-2">
                {(
                  [
                    ["nombre", "Nombre *", ""],
                    ["nit", "NIT *", "900123456-7"],
                    ["telefono", "Teléfono", ""],
                    ["email", "Correo *", ""],
                    ["direccion", "Dirección", "Cra. 15 # 93-60"],
                    ["ciudad", "Ciudad", "Bogotá"],
                  ] as const
                ).map(([name, label, placeholder]) => (
                  <FormField
                    key={name}
                    control={form.control}
                    name={name}
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{label}</FormLabel>
                        <FormControl>
                          <Input placeholder={placeholder} {...field} value={field.value ?? ""} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                ))}
              </div>
              <div className="rounded-md border p-3 space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium">Prestador de servicios de salud</span>
                  {settings.esIps ? <Badge variant="success">IPS habilitada</Badge> : <Badge variant="outline">No es IPS</Badge>}
                </div>
                <p className="text-xs text-muted-foreground">
                  {settings.esIps
                    ? "La clínica reporta RIPS: pacientes y procedimientos piden los datos que exige el Ministerio."
                    : "Sin reporte RIPS. Si la clínica está habilitada como IPS, pide a Nemedi que la marque (cambia el plan)."}
                </p>
                {settings.esIps && (
                  <FormField
                    control={form.control}
                    name="codigoHabilitacionReps"
                    render={({ field }) => (
                      <FormItem className="max-w-sm">
                        <FormLabel>Código de habilitación REPS *</FormLabel>
                        <FormControl>
                          <Input {...field} inputMode="numeric" maxLength={12} placeholder="110010123401" />
                        </FormControl>
                        <FormDescription>12 dígitos, tal como aparece en el REPS de la sede.</FormDescription>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                )}
              </div>
              {guardar}
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="politicas">
          <Card>
            <CardHeader>
              <CardTitle>Políticas de agenda</CardTitle>
              <CardDescription>Se aplican al crear y al mover citas, en el calendario y por la API.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              {numero("anticipacionMinimaHoras", "Anticipación mínima para agendar", "0 = se puede agendar para cualquier momento (incluso registrar una cita ya atendida).", "horas")}
              <FormField
                control={form.control}
                name="exigirConsentimiento"
                render={({ field }) => (
                  <FormItem className="flex items-center justify-between gap-4 rounded-md border p-3">
                    <div>
                      <FormLabel>Exigir consentimiento informado</FormLabel>
                      <FormDescription>
                        Una cita de un procedimiento marcado "requiere consentimiento" no se puede iniciar sin uno firmado en el último año.
                      </FormDescription>
                    </div>
                    <FormControl>
                      <Switch checked={field.value} onCheckedChange={field.onChange} aria-label="Exigir consentimiento informado" />
                    </FormControl>
                  </FormItem>
                )}
              />
              {guardar}
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="impuestos">
          <Card>
            <CardHeader>
              <CardTitle>IVA por defecto</CardTitle>
              <CardDescription>
                Se aplica a lo que se vende cuando el producto, el procedimiento o el paquete no tiene un IVA propio. Los precios del catálogo son sin IVA.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              {numero("ivaProductos", "IVA de productos", "La tarifa general es 19 %.", "%")}
              {numero("ivaServicios", "IVA de procedimientos y paquetes", "Los servicios de salud pueden estar excluidos (0 %); consulta con tu contador si tus servicios estéticos lo están.", "%")}
              {guardar}
            </CardContent>
          </Card>
        </TabsContent>
      </form>
    </Form>
  );
}

// ============================================================
// InvoicingSection.tsx — Configuración → Facturación (solo el dueño)
//
// Cuenta del proveedor (Factus): las credenciales se escriben aquí y el backend las
// guarda cifradas; nunca vuelven (solo "guardadas" y una pista del usuario). Para
// cambiarlas se escriben las cuatro. "Probar conexión" usa las del formulario si
// están completas, o las guardadas; trae el emisor y los rangos de numeración, que
// luego se asignan por sede (cada sede su consecutivo de facturas y de notas crédito).
// Los códigos DIAN salen de las tablas de referencia (GET /invoices/catalogs).
// ============================================================
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { CheckCircle2, PlugZap, XCircle } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { OptionSelect } from "@/components/shared/OptionSelect";
import { CatalogCombobox } from "@/components/invoices/CatalogCombobox";
import { useInvoicingCatalogs, useInvoicingSettings, useTestInvoicingConnection, useUpdateInvoicingSettings } from "@/api/invoices.api";
import { formatShortDate } from "@/lib/format-platform";
import { useToastStore } from "@/stores/toast.store";
import { PAYMENT_METHODS } from "@/types/sale";
import type { CatalogItem, InvoicingCatalogs, InvoicingSettings, NumberingRange, TestConnectionResult } from "@/types/invoice";

const NONE = "none";

const schema = z
  .object({
    habilitada: z.boolean(),
    ambiente: z.enum(["Pruebas", "Produccion"]),
    clientId: z.string(),
    clientSecret: z.string(),
    usuario: z.string(),
    contrasena: z.string(),
    emisorNit: z.string().regex(/^\d{5,15}$/, "Solo dígitos, sin el de verificación"),
    emisorDv: z.string().regex(/^\d?$/, "Un dígito"),
    emisorRazonSocial: z.string().min(1, "Requerido"),
    emisorTipoOrganizacion: z.string().min(1),
    emisorResponsabilidad: z.string().min(1),
    emisorMunicipio: z.string().nullable(),
    emisorDireccion: z.string(),
    emisorTelefono: z.string(),
    emisorEmail: z.union([z.literal(""), z.string().email("Correo inválido")]),
    unidadMedida: z.string().min(1),
    codigoEstandar: z.string().min(1),
    ivaCeroExcluido: z.boolean(),
    enviarCorreoAlEmitir: z.boolean(),
    codigosMetodoPago: z.record(z.string(), z.string()),
    sedes: z.array(
      z.object({
        branchId: z.string(),
        branchNombre: z.string(),
        rangoFacturaId: z.number().nullable(),
        prefijoFactura: z.string().nullable(),
        rangoNotaCreditoId: z.number().nullable(),
        prefijoNotaCredito: z.string().nullable(),
      }),
    ),
  })
  .superRefine((v, ctx) => {
    const cred = [v.clientId, v.clientSecret, v.usuario, v.contrasena];
    if (cred.some((c) => c.trim()) && cred.some((c) => !c.trim()))
      ctx.addIssue({ code: "custom", path: ["contrasena"], message: "Para cambiar las credenciales escribe las cuatro" });
  });
type Values = z.infer<typeof schema>;

export function InvoicingSection() {
  const { data: settings, isLoading } = useInvoicingSettings();
  const { data: catalogs } = useInvoicingCatalogs();
  // Aquí y no en el formulario: la prueba refresca la configuración y el formulario se remonta
  const [prueba, setPrueba] = useState<TestConnectionResult | null>(null);
  if (isLoading || !settings || !catalogs) return <Skeleton className="h-96 w-full" />;
  // key: al guardar llegan valores nuevos y el formulario se remonta con ellos
  return <InvoicingForm key={JSON.stringify(settings)} settings={settings} catalogs={catalogs} prueba={prueba} onPrueba={setPrueba} />;
}

function toOptions(items: CatalogItem[]) {
  return items.map((i) => ({ value: i.codigo, label: `${i.codigo} · ${i.nombre}` }));
}

function InvoicingForm({ settings, catalogs, prueba, onPrueba }: {
  settings: InvoicingSettings;
  catalogs: InvoicingCatalogs;
  prueba: TestConnectionResult | null;
  onPrueba: (r: TestConnectionResult) => void;
}) {
  const update = useUpdateInvoicingSettings();
  const test = useTestInvoicingConnection();
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      habilitada: settings.habilitada,
      ambiente: settings.ambiente,
      clientId: "",
      clientSecret: "",
      usuario: "",
      contrasena: "",
      emisorNit: settings.emisorNit,
      emisorDv: settings.emisorDv ?? "",
      emisorRazonSocial: settings.emisorRazonSocial,
      emisorTipoOrganizacion: settings.emisorTipoOrganizacion,
      emisorResponsabilidad: settings.emisorResponsabilidad,
      emisorMunicipio: settings.emisorMunicipio,
      emisorDireccion: settings.emisorDireccion ?? "",
      emisorTelefono: settings.emisorTelefono ?? "",
      emisorEmail: settings.emisorEmail ?? "",
      unidadMedida: settings.unidadMedida,
      codigoEstandar: settings.codigoEstandar,
      ivaCeroExcluido: settings.ivaCeroExcluido,
      enviarCorreoAlEmitir: settings.enviarCorreoAlEmitir,
      codigosMetodoPago: settings.codigosMetodoPago,
      sedes: settings.sedes,
    },
  });

  const rangos = prueba?.rangos ?? [];
  const sedes = useWatch({ control: form.control, name: "sedes" });

  const onTest = async () => {
    const v = form.getValues();
    const nuevas = [v.clientId, v.clientSecret, v.usuario, v.contrasena].every((c) => c.trim());
    try {
      const r = await test.mutateAsync(
        nuevas
          ? { ambiente: v.ambiente, clientId: v.clientId.trim(), clientSecret: v.clientSecret.trim(), usuario: v.usuario.trim(), contrasena: v.contrasena }
          : { ambiente: v.ambiente },
      );
      onPrueba(r);
      if (r.exitosa && !form.getValues("emisorNit") && r.emisorNit) form.setValue("emisorNit", r.emisorNit.replace(/\D/g, ""), { shouldDirty: true });
      if (r.exitosa && !form.getValues("emisorRazonSocial") && r.emisorNombre) form.setValue("emisorRazonSocial", r.emisorNombre, { shouldDirty: true });
    } catch {
      // toast global (400 sin credenciales)
    }
  };

  const onSubmit = async (v: Values) => {
    try {
      await update.mutateAsync({
        ...v,
        clientId: v.clientId.trim() || undefined,
        clientSecret: v.clientSecret.trim() || undefined,
        usuario: v.usuario.trim() || undefined,
        contrasena: v.contrasena || undefined,
        emisorDv: v.emisorDv || null,
        emisorDireccion: v.emisorDireccion.trim() || null,
        emisorTelefono: v.emisorTelefono.trim() || null,
        emisorEmail: v.emisorEmail.trim() || null,
      });
      useToastStore.success("Configuración de facturación guardada");
    } catch {
      // toast global
    }
  };

  const setRango = (index: number, campo: "Factura" | "NotaCredito", id: string) => {
    const rango = rangos.find((r) => String(r.id) === id);
    const sede = { ...sedes[index] };
    if (campo === "Factura") {
      sede.rangoFacturaId = id === NONE ? null : Number(id);
      sede.prefijoFactura = rango?.prefijo ?? null;
    } else {
      sede.rangoNotaCreditoId = id === NONE ? null : Number(id);
      sede.prefijoNotaCredito = rango?.prefijo ?? null;
    }
    form.setValue(`sedes.${index}`, sede, { shouldDirty: true });
  };

  const text = (name: "emisorNit" | "emisorDv" | "emisorRazonSocial" | "emisorDireccion" | "emisorTelefono" | "emisorEmail", label: string, extra?: { placeholder?: string; inputMode?: "numeric" }) => (
    <FormField
      control={form.control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FormLabel>{label}</FormLabel>
          <FormControl>
            <Input {...field} {...extra} />
          </FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
  );

  const secret = (name: "clientId" | "clientSecret" | "usuario" | "contrasena", label: string, type: "text" | "password") => (
    <FormField
      control={form.control}
      name={name}
      render={({ field }) => (
        <FormItem>
          <FormLabel>{label}</FormLabel>
          <FormControl>
            {/* El navegador ignora autocomplete="off" en contraseñas y llenaba estos campos con el login
                de NemediClinic: "new-password" (y nombres propios) lo evitan; lo mismo para gestores. */}
            <Input
              {...field}
              type={type}
              name={`factus-${name}`}
              autoComplete={type === "password" ? "new-password" : "off"}
              autoCorrect="off"
              autoCapitalize="none"
              spellCheck={false}
              data-1p-ignore
              data-lpignore="true"
              placeholder={settings.tieneCredenciales ? "•••••• (guardada)" : ""}
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
  );

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
        <Card>
          <CardHeader>
            <div className="flex flex-wrap items-center gap-2">
              <CardTitle>Facturación electrónica</CardTitle>
              {settings.habilitada ? <Badge variant="success">Activa</Badge> : <Badge variant="outline">Inactiva</Badge>}
              <Badge variant={settings.ambiente === "Produccion" ? "default" : "warning"}>{settings.ambiente === "Produccion" ? "Producción" : "Pruebas"}</Badge>
            </div>
            <CardDescription>
              Proveedor tecnológico: {settings.proveedor}. Las facturas se emiten desde el detalle de una venta o desde un pago en la ficha del paciente.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField
                control={form.control}
                name="habilitada"
                render={({ field }) => (
                  <FormItem className="flex items-center justify-between gap-4 rounded-md border p-3">
                    <div>
                      <FormLabel>Emitir facturas electrónicas</FormLabel>
                      <FormDescription>Apagada, emitir responde que no está configurada.</FormDescription>
                    </div>
                    <FormControl>
                      <Switch checked={field.value} onCheckedChange={field.onChange} aria-label="Emitir facturas electrónicas" />
                    </FormControl>
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="ambiente"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Ambiente</FormLabel>
                    <OptionSelect
                      value={field.value}
                      onChange={field.onChange}
                      options={[
                        { value: "Pruebas", label: "Pruebas (sandbox, sin validez fiscal)" },
                        { value: "Produccion", label: "Producción (ante la DIAN)" },
                      ]}
                    />
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <fieldset className="space-y-3 rounded-md border p-3">
              <legend className="px-1 text-sm font-medium">Credenciales de {settings.proveedor}</legend>
              <p className="text-xs text-muted-foreground">
                {settings.credencialesIlegibles
                  ? "Las credenciales guardadas no se pueden leer: la clave de cifrado del servidor cambió. Escribe las cuatro de nuevo y guarda."
                  : settings.tieneCredenciales
                    ? `Guardadas (usuario ${settings.usuarioPista}, client_id ${settings.clientIdPista}). Déjalas en blanco para conservarlas.`
                    : "Las entrega el proveedor. Se guardan cifradas y no se vuelven a mostrar."}
              </p>
              <div className="grid gap-4 sm:grid-cols-2">
                {secret("clientId", "Client ID", "text")}
                {secret("clientSecret", "Client secret", "password")}
                {secret("usuario", "Usuario", "text")}
                {secret("contrasena", "Contraseña", "password")}
              </div>
              <div className="flex flex-wrap items-center gap-3">
                <Button type="button" variant="outline" disabled={test.isPending} onClick={() => void onTest()}>
                  <PlugZap className="mr-2 h-4 w-4" />
                  {test.isPending ? "Probando..." : "Probar conexión"}
                </Button>
                {!prueba && settings.ultimaPrueba && (
                  <span className="text-xs text-muted-foreground">
                    Última prueba {formatShortDate(settings.ultimaPrueba)}: {settings.ultimaPruebaExitosa ? "exitosa" : "fallida"}
                  </span>
                )}
              </div>
              {prueba && <TestResult r={prueba} />}
            </fieldset>

            <fieldset className="space-y-3 rounded-md border p-3">
              <legend className="px-1 text-sm font-medium">Emisor</legend>
              <div className="grid gap-4 sm:grid-cols-[1fr_6rem_2fr]">
                {text("emisorNit", "NIT *", { inputMode: "numeric", placeholder: "900123456" })}
                {text("emisorDv", "DV", { inputMode: "numeric" })}
                {text("emisorRazonSocial", "Razón social *")}
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="emisorTipoOrganizacion"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Tipo de organización</FormLabel>
                      <OptionSelect value={field.value} onChange={field.onChange} options={toOptions(catalogs.tiposOrganizacion)} />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="emisorResponsabilidad"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Responsabilidad fiscal</FormLabel>
                      <OptionSelect value={field.value} onChange={field.onChange} options={toOptions(catalogs.responsabilidades)} />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="emisorMunicipio"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Municipio (DANE)</FormLabel>
                      <CatalogCombobox tabla="municipios" value={field.value} onChange={field.onChange} placeholder="Busca el municipio" />
                    </FormItem>
                  )}
                />
                {text("emisorDireccion", "Dirección")}
                {text("emisorTelefono", "Teléfono")}
                {text("emisorEmail", "Correo de facturación")}
              </div>
              <p className="text-xs text-muted-foreground">El emisor real es el de la cuenta del proveedor: "Probar conexión" avisa si el NIT no coincide.</p>
            </fieldset>

            <fieldset className="space-y-3 rounded-md border p-3">
              <legend className="px-1 text-sm font-medium">Cómo se arma cada factura</legend>
              <div className="grid gap-4 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="unidadMedida"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Unidad de medida</FormLabel>
                      <CatalogCombobox tabla="unidades" value={field.value} onChange={(v) => field.onChange(v ?? "94")} placeholder="94 · unidad" />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="codigoEstandar"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Código estándar del ítem</FormLabel>
                      <OptionSelect value={field.value} onChange={field.onChange} options={toOptions(catalogs.codigosEstandar)} />
                    </FormItem>
                  )}
                />
              </div>
              <div className="grid gap-4 sm:grid-cols-4">
                {PAYMENT_METHODS.map((m) => (
                  <FormField
                    key={m}
                    control={form.control}
                    name="codigosMetodoPago"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{m}</FormLabel>
                        <OptionSelect
                          value={field.value[m] ?? catalogs.mediosPagoPorDefecto[m]}
                          onChange={(v) => field.onChange({ ...field.value, [m]: v })}
                          options={toOptions(catalogs.mediosPago)}
                        />
                      </FormItem>
                    )}
                  />
                ))}
              </div>
              <FormDescription>Código DIAN con el que sale cada medio de pago de la caja.</FormDescription>
              {(
                [
                  ["ivaCeroExcluido", "IVA 0 % como excluido", "Los servicios al 0 % van como excluidos de IVA (lo usual en servicios de salud)."],
                  ["enviarCorreoAlEmitir", "Enviar la factura al correo del paciente al emitir", "El proveedor manda el PDF y el XML. Siempre se puede reenviar después."],
                ] as const
              ).map(([name, label, description]) => (
                <FormField
                  key={name}
                  control={form.control}
                  name={name}
                  render={({ field }) => (
                    <FormItem className="flex items-center justify-between gap-4 rounded-md border p-3">
                      <div>
                        <FormLabel>{label}</FormLabel>
                        <FormDescription>{description}</FormDescription>
                      </div>
                      <FormControl>
                        <Switch checked={field.value} onCheckedChange={field.onChange} aria-label={label} />
                      </FormControl>
                    </FormItem>
                  )}
                />
              ))}
            </fieldset>

            <fieldset className="space-y-3 rounded-md border p-3">
              <legend className="px-1 text-sm font-medium">Numeración por sede</legend>
              <p className="text-xs text-muted-foreground">
                {rangos.length === 0
                  ? "Prueba la conexión para elegir entre los rangos autorizados de la cuenta."
                  : "Cada sede lleva su propio consecutivo. Sin rango, usa el de otra sede o el único activo de la cuenta."}
              </p>
              {sedes.map((sede, i) => (
                <div key={sede.branchId} className="grid items-end gap-3 sm:grid-cols-[10rem_1fr_1fr]">
                  <Label className="pb-2">{sede.branchNombre}</Label>
                  <RangeSelect label="Facturas" tipo="Factura" rangos={rangos} id={sede.rangoFacturaId} prefijo={sede.prefijoFactura} onChange={(v) => setRango(i, "Factura", v)} />
                  <RangeSelect label="Notas crédito" tipo="NotaCredito" rangos={rangos} id={sede.rangoNotaCreditoId} prefijo={sede.prefijoNotaCredito} onChange={(v) => setRango(i, "NotaCredito", v)} />
                </div>
              ))}
            </fieldset>

            <div className="flex justify-end">
              <Button type="submit" disabled={update.isPending || !form.formState.isDirty}>
                {update.isPending ? "Guardando..." : "Guardar cambios"}
              </Button>
            </div>
          </CardContent>
        </Card>
      </form>
    </Form>
  );
}

function RangeSelect({ label, tipo, rangos, id, prefijo, onChange }: {
  label: string;
  tipo: "Factura" | "NotaCredito";
  rangos: NumberingRange[];
  id: number | null;
  prefijo: string | null;
  onChange: (value: string) => void;
}) {
  const opciones = rangos.filter((r) => r.documento === tipo);
  // Sin probar la conexión, el rango guardado sigue visible como opción
  const guardado = id !== null && !opciones.some((r) => r.id === id);
  return (
    <div className="space-y-1.5">
      <span className="text-xs text-muted-foreground">{label}</span>
      <Select value={id === null ? NONE : String(id)} onValueChange={onChange}>
        <SelectTrigger className="w-full" aria-label={label}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={NONE}>Automático</SelectItem>
          {guardado && <SelectItem value={String(id)}>{`${prefijo ?? ""} (rango ${id})`}</SelectItem>}
          {opciones.map((r) => (
            <SelectItem key={r.id} value={String(r.id)} disabled={!r.vigente}>
              {[r.prefijo, r.desde !== null && r.hasta !== null ? `${r.desde}–${r.hasta}` : null, `va en ${r.actual}`].filter(Boolean).join(" · ") + (r.vigente ? "" : " (vencido)")}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

function TestResult({ r }: { r: TestConnectionResult }) {
  const ok = r.exitosa && !r.nitNoCoincide;
  return (
    <div className={`flex items-start gap-2 rounded-md p-3 text-sm ${ok ? "bg-success-soft" : "bg-destructive/10"}`} role="status">
      {ok ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" /> : <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />}
      <div className="space-y-1">
        <p className="whitespace-pre-line">{r.mensaje}</p>
        {r.exitosa && (
          <p className="text-xs text-muted-foreground">
            Emisor {r.emisorNombre ?? "—"} · NIT {r.emisorNit ?? "—"} · {r.rangos.filter((x) => x.vigente).length} rango(s) vigente(s)
          </p>
        )}
      </div>
    </div>
  );
}

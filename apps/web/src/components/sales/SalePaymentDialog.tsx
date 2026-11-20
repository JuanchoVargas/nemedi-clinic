// ============================================================
// SalePaymentDialog.tsx — Abono a una venta con saldo (cartera)
//
// Monto con tope en el saldo (Zod aquí; el backend responde 422 si alguien se
// lo salta). El abono se aplica a las líneas en orden: si cae en un paquete,
// también se ve en la ficha del paciente.
// ============================================================
import { useMemo } from "react";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { CurrencyInput } from "@/components/ui/CurrencyInput";
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { FormDialog } from "@/components/shared/FormDialog";
import { useAddSalePayment } from "@/api/sales.api";
import { formatCOP } from "@/lib/format-cop";
import { useToastStore } from "@/stores/toast.store";
import { PAYMENT_METHODS, type Sale } from "@/types/sale";

function buildSchema(saldo: number) {
  return z.object({
    monto: z.number({ message: "Escribe el monto" }).positive("El monto debe ser mayor a 0").max(saldo, `El abono supera el saldo (${formatCOP(saldo)})`),
    metodoPago: z.enum(PAYMENT_METHODS),
    referencia: z.string().max(100, "Máximo 100 caracteres"),
  });
}
type Values = z.infer<ReturnType<typeof buildSchema>>;

export function SalePaymentDialog({ sale, onClose }: { sale: Sale; onClose: () => void }) {
  const add = useAddSalePayment();
  const schema = useMemo(() => buildSchema(sale.saldo), [sale.saldo]);
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { monto: sale.saldo, metodoPago: "Efectivo", referencia: "" },
  });
  const metodo = useWatch({ control: form.control, name: "metodoPago" });

  const onSubmit = async (values: Values) => {
    try {
      await add.mutateAsync({
        id: sale.id,
        body: { monto: values.monto, metodoPago: values.metodoPago, referencia: values.referencia.trim() || undefined },
      });
      useToastStore.success("Abono registrado", `Venta #${sale.numero}`);
      onClose();
    } catch {
      // toast global
    }
  };

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title="Registrar abono"
      description={`Venta #${sale.numero} · ${sale.cliente} · saldo ${formatCOP(sale.saldo)}`}
      dirty={form.formState.isDirty}
      actions={
        <Button form="sale-payment-form" type="submit" disabled={add.isPending}>
          {add.isPending ? "Guardando..." : "Registrar abono"}
        </Button>
      }
    >
      <Form {...form}>
        <form id="sale-payment-form" onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 pt-1">
          <FormField
            control={form.control}
            name="monto"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Monto *</FormLabel>
                <FormControl>
                  <CurrencyInput value={field.value} onChange={field.onChange} placeholder="0" />
                </FormControl>
                <FormDescription>Máximo {formatCOP(sale.saldo)}.</FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="metodoPago"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Medio de pago *</FormLabel>
                  <Select value={field.value} onValueChange={field.onChange}>
                    <FormControl>
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {PAYMENT_METHODS.map((m) => (
                        <SelectItem key={m} value={m}>
                          {m}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="referencia"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Referencia</FormLabel>
                  <FormControl>
                    <Input placeholder={metodo === "Efectivo" ? "Recibo (opcional)" : "Número de la operación"} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
        </form>
      </Form>
    </FormDialog>
  );
}

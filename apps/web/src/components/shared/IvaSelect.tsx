// ============================================================
// IvaSelect.tsx — Selector del IVA de un ítem del catálogo
//
// "El de la clínica" deja que decida la configuración (productos 19 %, servicios
// 0 % por defecto); una tarifa lo fija para ese ítem. Ver lib/iva.ts.
// ============================================================
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { IVA_CLINICA, IVA_TARIFAS } from "@/lib/iva";

export function IvaSelect({ value, onChange, id }: { value: string; onChange: (value: string) => void; id?: string }) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger id={id} className="w-full" aria-label="IVA">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={IVA_CLINICA}>El de la clínica</SelectItem>
        {IVA_TARIFAS.map((v) => (
          <SelectItem key={v} value={v}>
            {v} %
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

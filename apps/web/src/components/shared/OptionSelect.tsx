// ============================================================
// OptionSelect.tsx — Select de shadcn sobre una lista { value, label }
//
// Para los catálogos cortos (RIPS, etc.) dentro de un FormField:
//   <OptionSelect value={field.value} onChange={field.onChange} options={SEXOS} />
// ============================================================

import { FormControl } from "@/components/ui/form";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

interface OptionSelectProps {
  value?: string | null;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  placeholder?: string;
}

export function OptionSelect({ value, onChange, options, placeholder = "Selecciona" }: OptionSelectProps) {
  return (
    <Select value={value || undefined} onValueChange={onChange}>
      <FormControl>
        <SelectTrigger className="w-full">
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
      </FormControl>
      <SelectContent>
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

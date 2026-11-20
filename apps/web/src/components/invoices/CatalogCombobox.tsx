// ============================================================
// CatalogCombobox.tsx — Busca en una tabla DIAN larga (municipios, unidades de medida)
//
// Patrón de PatientCombobox: Popover + Command con búsqueda en el servidor.
// ============================================================
import { useState } from "react";
import { Check, ChevronsUpDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { useCatalogSearch } from "@/api/invoices.api";
import { useDebounce } from "@/hooks/use-debounce";
import { cn } from "@/lib/utils";

export function CatalogCombobox({ tabla, value, onChange, placeholder, id }: {
  tabla: "municipios" | "unidades";
  value: string | null;
  onChange: (codigo: string | null) => void;
  placeholder: string;
  id?: string;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const debounced = useDebounce(search, 250);
  const { data } = useCatalogSearch(tabla, debounced);
  // El nombre del valor actual: se busca por su código
  const { data: actual } = useCatalogSearch(tabla, value ?? "");
  const etiqueta = value ? actual?.find((x) => x.codigo === value)?.nombre : undefined;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button id={id} type="button" variant="outline" role="combobox" aria-expanded={open} className="w-full justify-between font-normal">
          <span className="truncate">{value ? `${value} · ${etiqueta ?? ""}` : <span className="text-muted-foreground">{placeholder}</span>}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[var(--radix-popover-trigger-width)] p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput placeholder="Código o nombre..." value={search} onValueChange={setSearch} />
          <CommandList>
            <CommandEmpty>Sin resultados.</CommandEmpty>
            <CommandGroup>
              {data?.map((x) => (
                <CommandItem
                  key={x.codigo}
                  value={x.codigo}
                  onSelect={() => {
                    onChange(x.codigo === value ? null : x.codigo);
                    setOpen(false);
                  }}
                >
                  <Check className={cn("mr-2 h-4 w-4", x.codigo === value ? "opacity-100" : "opacity-0")} />
                  <span className="tabular-nums text-muted-foreground">{x.codigo}</span>
                  <span className="ml-2 truncate">{x.nombre}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

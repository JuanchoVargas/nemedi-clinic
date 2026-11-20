// ============================================================
// CatalogSearch.tsx — Buscador de "Nueva venta"
//
// Busca en el servidor productos con precio, procedimientos y paquetes activos
// (GET /sales/catalog) y los agrupa por tipo. Un clic agrega la línea.
// "Otro concepto" abre una fila para algo fuera del catálogo.
// ============================================================
import { useState } from "react";
import { PackagePlus, Plus, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { CurrencyInput } from "@/components/ui/CurrencyInput";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { useSaleCatalog } from "@/api/sales.api";
import { useDebounce } from "@/hooks/use-debounce";
import { formatCOP } from "@/lib/format-cop";
import { SALE_LINE_LABELS, type SaleCatalogItem, type SaleLineType } from "@/types/sale";

const GROUPS: SaleLineType[] = ["Producto", "Procedimiento", "Paquete"];

export function CatalogSearch({
  onAdd,
  onAddOther,
}: {
  onAdd: (item: SaleCatalogItem) => void;
  onAddOther: (descripcion: string, precio: number, iva: number) => void;
}) {
  const [search, setSearch] = useState("");
  const debounced = useDebounce(search, 250);
  const { data, isLoading } = useSaleCatalog(debounced);
  const [otherOpen, setOtherOpen] = useState(false);

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          type="search"
          placeholder="Buscar producto, procedimiento o paquete..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="bg-card pl-9"
          aria-label="Buscar en el catálogo"
        />
      </div>

      {isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : (
        <div className="grid gap-3 md:grid-cols-3">
          {GROUPS.map((tipo) => {
            const items = (data ?? []).filter((i) => i.tipo === tipo);
            return (
              <div key={tipo} className="min-w-0 rounded-lg border bg-card">
                <p className="border-b px-3 py-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                  {SALE_LINE_LABELS[tipo]}s
                </p>
                {items.length === 0 ? (
                  <p className="px-3 py-3 text-sm text-muted-foreground">Sin resultados.</p>
                ) : (
                  <ul className="max-h-56 overflow-y-auto">
                    {items.map((item) => (
                      <li key={item.id}>
                        <button
                          type="button"
                          onClick={() => onAdd(item)}
                          className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
                          aria-label={`Agregar ${item.nombre}`}
                        >
                          <span className="min-w-0">
                            <span className="block truncate font-medium">{item.nombre}</span>
                            <span className="block truncate text-xs text-muted-foreground">
                              {item.tipo === "Producto" ? `Stock ${item.stock ?? 0} ${item.unidadMedida ?? ""}` : item.detalle}
                            </span>
                          </span>
                          <span className="shrink-0 text-right">
                            <span className="block font-medium tabular-nums">{formatCOP(item.precio)}</span>
                            <span className="block text-xs text-muted-foreground">IVA {item.ivaPorcentaje} %</span>
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            );
          })}
        </div>
      )}

      {otherOpen ? (
        <OtherLineForm
          onCancel={() => setOtherOpen(false)}
          onAdd={(d, p, i) => {
            onAddOther(d, p, i);
            setOtherOpen(false);
          }}
        />
      ) : (
        <Button type="button" variant="outline" size="sm" onClick={() => setOtherOpen(true)}>
          <PackagePlus className="mr-2 h-4 w-4" />
          Otro concepto
        </Button>
      )}
    </div>
  );
}

function OtherLineForm({ onAdd, onCancel }: { onAdd: (d: string, p: number, i: number) => void; onCancel: () => void }) {
  const [descripcion, setDescripcion] = useState("");
  const [precio, setPrecio] = useState(0);
  const [iva, setIva] = useState(0);
  const valido = descripcion.trim().length > 0 && precio > 0 && iva >= 0 && iva <= 100;

  return (
    <div className="grid gap-3 rounded-lg border bg-card p-3 sm:grid-cols-[1fr_9rem_6rem_auto] sm:items-end">
      <div className="space-y-1.5">
        <Label htmlFor="otro-desc">Concepto</Label>
        <Input id="otro-desc" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} placeholder="Bono de regalo, envío..." />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="otro-precio">Precio sin IVA</Label>
        <CurrencyInput id="otro-precio" value={precio} onChange={setPrecio} placeholder="0" />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="otro-iva">IVA %</Label>
        <Input id="otro-iva" type="number" min={0} max={100} value={iva} onChange={(e) => setIva(Number(e.target.value))} />
      </div>
      <div className="flex gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancelar
        </Button>
        <Button type="button" size="sm" disabled={!valido} onClick={() => onAdd(descripcion.trim(), precio, iva)}>
          <Plus className="mr-1 h-4 w-4" />
          Agregar
        </Button>
      </div>
      <p className="text-xs text-muted-foreground sm:col-span-4">Fuera del catálogo: el precio y el IVA los decides tú.</p>
    </div>
  );
}

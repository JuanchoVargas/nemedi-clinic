// ============================================================
// use-sale-draft.ts — Estado de "Nueva venta" (líneas y pagos) fuera del JSX
//
// Guarda lo que el usuario va armando y lo traduce al body del API. Los montos
// de cada línea NO se calculan aquí: los devuelve POST /sales/quote (useSaleQuote).
// Lo único que se suma en la web es lo que el usuario escribió en los pagos,
// para decirle cuánto falta por cobrar.
//
// EQUIVALENTE A: un composable useCarrito() de Vue con su estado reactivo.
// ============================================================
import { useCallback, useMemo, useState } from "react";
import type { PaymentMethod, SaleCatalogItem, SaleLineRequest, SaleLineType, SalePaymentRequest, Sale } from "@/types/sale";

export interface DraftLine {
  key: string;
  tipo: SaleLineType;
  referenciaId?: string;
  descripcion: string;
  detalle?: string | null;
  cantidad: number;
  descuento: number;
  /** Solo "Otro": el resto sale del catálogo. */
  precioUnitario?: number;
  ivaPorcentaje?: number;
  /** Existencia del producto al agregarlo (para avisar si se pide más). */
  stock?: number | null;
  unidad?: string | null;
}

export interface DraftPayment {
  key: string;
  metodoPago: PaymentMethod;
  monto: number;
  referencia: string;
}

const newKey = () => Math.random().toString(36).slice(2, 10);

/** Borrador existente → estado inicial del formulario. */
export function draftFromSale(sale: Sale): DraftLine[] {
  return sale.lineas.map((l) => ({
    key: newKey(),
    tipo: l.tipo,
    referenciaId: l.referenciaId ?? undefined,
    descripcion: l.descripcion,
    cantidad: l.cantidad,
    descuento: l.descuento,
    precioUnitario: l.tipo === "Otro" ? l.precioUnitario : undefined,
    ivaPorcentaje: l.tipo === "Otro" ? l.ivaPorcentaje : undefined,
  }));
}

export function useSaleDraft(initialLines: DraftLine[] = []) {
  const [lines, setLines] = useState<DraftLine[]>(initialLines);
  const [payments, setPayments] = useState<DraftPayment[]>([]);

  /** Agrega un ítem del catálogo; un producto o procedimiento repetido suma cantidad. */
  const addItem = useCallback((item: SaleCatalogItem) => {
    setLines((prev) => {
      const existing = prev.find((l) => l.referenciaId === item.id && l.tipo === item.tipo);
      if (existing && item.tipo !== "Paquete")
        return prev.map((l) => (l === existing ? { ...l, cantidad: l.cantidad + 1 } : l));
      return [
        ...prev,
        {
          key: newKey(),
          tipo: item.tipo,
          referenciaId: item.id,
          descripcion: item.nombre,
          detalle: item.detalle,
          cantidad: 1,
          descuento: 0,
          stock: item.stock,
          unidad: item.unidadMedida,
        },
      ];
    });
  }, []);

  const addOther = useCallback((descripcion: string, precioUnitario: number, ivaPorcentaje: number) => {
    setLines((prev) => [...prev, { key: newKey(), tipo: "Otro", descripcion, cantidad: 1, descuento: 0, precioUnitario, ivaPorcentaje }]);
  }, []);

  const updateLine = useCallback((key: string, patch: Partial<DraftLine>) => {
    setLines((prev) => prev.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }, []);

  const removeLine = useCallback((key: string) => setLines((prev) => prev.filter((l) => l.key !== key)), []);

  const addPayment = useCallback((metodoPago: PaymentMethod, monto: number) => {
    setPayments((prev) => [...prev, { key: newKey(), metodoPago, monto, referencia: "" }]);
  }, []);

  const updatePayment = useCallback((key: string, patch: Partial<DraftPayment>) => {
    setPayments((prev) => prev.map((p) => (p.key === key ? { ...p, ...patch } : p)));
  }, []);

  const removePayment = useCallback((key: string) => setPayments((prev) => prev.filter((p) => p.key !== key)), []);

  /** Body de las líneas para el API (cotización y guardado). */
  const lineRequests = useMemo<SaleLineRequest[]>(
    () =>
      lines.map((l) => ({
        tipo: l.tipo,
        referenciaId: l.referenciaId,
        descripcion: l.tipo === "Otro" ? l.descripcion : undefined,
        cantidad: l.cantidad,
        descuento: l.descuento,
        precioUnitario: l.tipo === "Otro" ? l.precioUnitario : undefined,
        ivaPorcentaje: l.tipo === "Otro" ? l.ivaPorcentaje : undefined,
      })),
    [lines],
  );

  const paymentRequests = useMemo<SalePaymentRequest[]>(
    () =>
      payments
        .filter((p) => p.monto > 0)
        .map((p) => ({ monto: p.monto, metodoPago: p.metodoPago, referencia: p.referencia.trim() || undefined })),
    [payments],
  );

  const totalPagos = useMemo(() => payments.reduce((sum, p) => sum + (p.monto > 0 ? p.monto : 0), 0), [payments]);
  const tienePaquete = lines.some((l) => l.tipo === "Paquete");

  return {
    lines,
    payments,
    addItem,
    addOther,
    updateLine,
    removeLine,
    addPayment,
    updatePayment,
    removePayment,
    lineRequests,
    paymentRequests,
    totalPagos,
    tienePaquete,
  };
}

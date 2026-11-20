// ============================================================
// sales.api.ts — Ventas
//
// Backend: /api/v1/sales (policy Admin: recepción y dueño; la esteticista recibe 403)
// La cotización (POST /quote) es una query: mientras se arma la venta, el backend
// calcula base, IVA y total de cada línea. La web no replica esa aritmética.
// ============================================================

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import type { PagedResponse } from "@/types/api";
import type {
  Sale,
  SaleCatalogItem,
  SaleFilters,
  SaleLineRequest,
  SalePaymentRequest,
  SaleQuote,
  SaveSaleRequest,
  VoidSaleResult,
} from "@/types/sale";

const BASE = "/api/v1/sales";

export function useSales(filters: SaleFilters) {
  return useQuery({
    queryKey: ["sales", "list", filters],
    queryFn: async () => {
      const { data } = await api.get<PagedResponse<Sale>>(BASE, {
        params: {
          ...filters,
          search: filters.search || undefined,
          estado: filters.estado || undefined,
          conSaldo: filters.conSaldo || undefined,
        },
      });
      return data;
    },
    placeholderData: keepPreviousData,
  });
}

export function useSale(id: string | undefined) {
  return useQuery({
    queryKey: ["sales", "detail", id],
    queryFn: async () => {
      const { data } = await api.get<Sale>(`${BASE}/${id}`);
      return data;
    },
    enabled: !!id,
  });
}

/** Ítems vendibles para el buscador (productos con precio, procedimientos y paquetes activos). */
export function useSaleCatalog(search: string) {
  return useQuery({
    queryKey: ["sales", "catalog", search],
    queryFn: async () => {
      const { data } = await api.get<SaleCatalogItem[]>(`${BASE}/catalog`, { params: { search: search || undefined } });
      return data;
    },
    placeholderData: keepPreviousData,
    staleTime: 60 * 1000,
  });
}

/** Totales calculados por el backend para las líneas actuales (sin guardar nada). */
export function useSaleQuote(lineas: SaleLineRequest[]) {
  return useQuery({
    queryKey: ["sales", "quote", lineas],
    queryFn: async () => {
      const { data } = await api.post<SaleQuote>(`${BASE}/quote`, { lineas });
      return data;
    },
    enabled: lineas.length > 0,
    placeholderData: keepPreviousData,
    staleTime: Infinity,
    retry: false,
  });
}

function useInvalidateMoney() {
  const qc = useQueryClient();
  // Una venta mueve inventario, paquetes, pagos y la caja: se refresca todo lo que los muestra
  return () => {
    for (const key of ["sales", "cash", "products", "inventory-movements", "patient-packages", "patients", "dashboard", "reports"])
      qc.invalidateQueries({ queryKey: [key] });
  };
}

export function useSaveSale() {
  const invalidate = useInvalidateMoney();
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveSaleRequest }) => {
      const { data } = id ? await api.put<Sale>(`${BASE}/${id}`, body) : await api.post<Sale>(BASE, body);
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useConfirmSale() {
  const invalidate = useInvalidateMoney();
  return useMutation({
    mutationFn: async ({ id, pagos }: { id: string; pagos: SalePaymentRequest[] }) => {
      const { data } = await api.post<Sale>(`${BASE}/${id}/confirm`, { pagos });
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useAddSalePayment() {
  const invalidate = useInvalidateMoney();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: SalePaymentRequest }) => {
      const { data } = await api.post<Sale>(`${BASE}/${id}/payments`, body);
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useVoidSale() {
  const invalidate = useInvalidateMoney();
  return useMutation({
    mutationFn: async ({ id, motivo }: { id: string; motivo: string }) => {
      const { data } = await api.post<VoidSaleResult>(`${BASE}/${id}/void`, { motivo });
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteDraftSale() {
  const invalidate = useInvalidateMoney();
  return useMutation({
    mutationFn: async (id: string) => {
      await api.delete(`${BASE}/${id}`);
      return { id };
    },
    onSuccess: invalidate,
  });
}

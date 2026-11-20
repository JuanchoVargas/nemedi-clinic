// ============================================================
// invoices.api.ts — Facturación electrónica (Factus)
//
// Backend: /api/v1/invoices (policy Admin; la configuración la escribe solo el dueño)
//   GET    /?filtros[&formato=xlsx]     listado o Excel
//   POST   /  { saleId | paymentId }   emite (501 si la clínica no la configuró)
//   POST   /{id}/retry                 pendiente: reenvía; rechazada: emite de nuevo
//   POST   /{id}/credit-note           anulación o devolución
//   POST   /{id}/send-email            reenvía al correo del paciente (o al que se indique)
//   GET/PUT /settings, POST /settings/test, GET /catalogs[/municipios|unidades]
// El PDF y el XML son adjuntos: se abren con su URL firmada (openSignedFile).
// ============================================================

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/axios";
import { saveBlob } from "@/lib/download-file";
import type { PagedResponse } from "@/types/api";
import type {
  CatalogItem,
  CreditNoteRequest,
  Invoice,
  InvoiceFilters,
  InvoicingCatalogs,
  InvoicingSettings,
  TestConnectionRequest,
  TestConnectionResult,
  UpdateInvoicingSettings,
} from "@/types/invoice";

const BASE = "/api/v1/invoices";

const clean = (f: Partial<InvoiceFilters>) =>
  Object.fromEntries(Object.entries(f).filter(([, v]) => v !== undefined && v !== ""));

export function useInvoices(filters: InvoiceFilters, enabled = true) {
  return useQuery({
    queryKey: ["invoices", "list", filters],
    queryFn: async () => {
      const { data } = await api.get<PagedResponse<Invoice>>(BASE, { params: clean(filters) });
      return data;
    },
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useInvoice(id: string | undefined) {
  return useQuery({
    queryKey: ["invoices", "detail", id],
    queryFn: async () => {
      const { data } = await api.get<Invoice>(`${BASE}/${id}`);
      return data;
    },
    enabled: !!id,
  });
}

export async function downloadInvoices(filters: Partial<InvoiceFilters>) {
  const { data, headers } = await api.get<Blob>(BASE, {
    params: { ...clean({ ...filters, page: undefined, pageSize: undefined }), formato: "xlsx" },
    responseType: "blob",
  });
  saveBlob(data, headers, "facturas.xlsx");
}

function useInvalidate() {
  const qc = useQueryClient();
  // Emitir o acreditar cambia la venta (estado de factura) y el listado
  return () => {
    for (const key of ["invoices", "sales", "patient-packages"]) qc.invalidateQueries({ queryKey: [key] });
  };
}

export function useIssueInvoice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (body: { saleId?: string; paymentId?: string }) => {
      const { data } = await api.post<Invoice>(BASE, body);
      return data;
    },
    onSettled: invalidate,
  });
}

export function useRetryInvoice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.post<Invoice>(`${BASE}/${id}/retry`);
      return data;
    },
    onSettled: invalidate,
  });
}

export function useCreditNote() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CreditNoteRequest }) => {
      const { data } = await api.post<Invoice>(`${BASE}/${id}/credit-note`, body);
      return data;
    },
    onSettled: invalidate,
  });
}

export function useSendInvoiceEmail() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, correo }: { id: string; correo?: string }) => {
      const { data } = await api.post<Invoice>(`${BASE}/${id}/send-email`, { correo: correo || null });
      return data;
    },
    onSuccess: invalidate,
  });
}

// ── Configuración ──

export function useInvoicingSettings(enabled = true) {
  return useQuery({
    queryKey: ["invoices", "settings"],
    queryFn: async () => {
      const { data } = await api.get<InvoicingSettings>(`${BASE}/settings`);
      return data;
    },
    enabled,
  });
}

export function useUpdateInvoicingSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateInvoicingSettings) => {
      const { data } = await api.put<InvoicingSettings>(`${BASE}/settings`, body);
      return data;
    },
    onSuccess: (data) => qc.setQueryData(["invoices", "settings"], data),
  });
}

/** Sin invalidar la configuración: refrescarla remontaría el formulario y borraría lo que se está escribiendo. */
export function useTestInvoicingConnection() {
  return useMutation({
    mutationFn: async (body: TestConnectionRequest) => {
      const { data } = await api.post<TestConnectionResult>(`${BASE}/settings/test`, body);
      return data;
    },
  });
}

export function useInvoicingCatalogs(enabled = true) {
  return useQuery({
    queryKey: ["invoices", "catalogs"],
    queryFn: async () => {
      const { data } = await api.get<InvoicingCatalogs>(`${BASE}/catalogs`);
      return data;
    },
    staleTime: Infinity,
    enabled,
  });
}

/** Municipios DANE o unidades de medida, buscando por código o nombre. */
export function useCatalogSearch(tabla: "municipios" | "unidades", search: string) {
  return useQuery({
    queryKey: ["invoices", "catalogs", tabla, search],
    queryFn: async () => {
      const { data } = await api.get<CatalogItem[]>(`${BASE}/catalogs/${tabla}`, { params: { search: search || undefined } });
      return data;
    },
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  });
}

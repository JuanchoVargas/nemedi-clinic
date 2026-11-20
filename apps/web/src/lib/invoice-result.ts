// ============================================================
// invoice-result.ts — Aviso según cómo quedó un documento electrónico
//
// Emitir responde 200 aunque el proveedor rechace (queda Rechazada con su motivo) o
// no conteste (Pendiente, lo reintenta el job): el toast lo dice en cada caso.
// ============================================================
import { useToastStore } from "@/stores/toast.store";
import { DOCUMENT_LABELS, type Invoice } from "@/types/invoice";

export function reportInvoiceResult(f: Invoice) {
  const nombre = DOCUMENT_LABELS[f.tipoDocumento];
  if (f.estado === "Emitida" || f.estado === "Anulada")
    useToastStore.success(`${nombre} ${f.numero} emitida`, f.advertencias ? "Con notificaciones de la DIAN (no la rechazan)." : undefined);
  else if (f.estado === "Rechazada") useToastStore.error(`${nombre} rechazada`, f.mensaje ?? undefined);
  else useToastStore.warning(`${nombre} en proceso`, "El proveedor no respondió; se reintenta sola cada 15 minutos.");
}

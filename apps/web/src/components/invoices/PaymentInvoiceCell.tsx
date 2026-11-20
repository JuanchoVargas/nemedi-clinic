// ============================================================
// PaymentInvoiceCell.tsx — Factura electrónica de un abono (ficha del paciente → Pagos)
//
// Las facturas del paciente se piden una vez (misma queryKey para todas las filas) y
// cada fila busca la de su pago: emitida → PDF/XML/nota crédito; si no, "Emitir".
// ============================================================
import { Badge } from "@/components/ui/badge";
import { InvoiceActions, IssueInvoiceButton } from "@/components/invoices/InvoiceActions";
import { useInvoices } from "@/api/invoices.api";
import { INVOICE_BADGE } from "@/types/invoice";
import { INVOICE_STATUS_LABELS } from "@/types/sale";

export function PaymentInvoiceCell({ patientId, paymentId }: { patientId: string; paymentId: string }) {
  const { data } = useInvoices({ patientId, tipoDocumento: "Factura", page: 1, pageSize: 100 });
  // La más reciente de ese pago (llegan de la más nueva a la más vieja)
  const factura = data?.items.find((f) => f.paymentId === paymentId);
  const vigente = factura && (factura.estado === "Emitida" || factura.estado === "Pendiente" || factura.estado === "Anulada");

  if (!data) return <span className="text-muted-foreground">—</span>;
  return (
    <div className="flex flex-col items-start gap-1.5">
      {factura && (
        <span className="flex items-center gap-1.5 text-xs">
          <Badge variant={INVOICE_BADGE[factura.estado]}>{INVOICE_STATUS_LABELS[factura.estado]}</Badge>
          {factura.numero}
        </span>
      )}
      {factura?.estado === "Rechazada" && <span className="max-w-60 text-xs text-destructive">{factura.mensaje}</span>}
      {vigente ? <InvoiceActions invoice={factura} compact /> : <IssueInvoiceButton paymentId={paymentId} size="sm" label={factura ? "Emitir de nuevo" : "Emitir factura"} />}
    </div>
  );
}

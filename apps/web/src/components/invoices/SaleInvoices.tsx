// ============================================================
// SaleInvoices.tsx — Factura electrónica dentro del detalle de una venta
//
// Muestra los documentos de la venta (factura, rechazos anteriores y notas crédito)
// con sus acciones, y "Emitir factura" mientras no haya una emitida o en proceso.
// ============================================================
import { FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { InvoiceActions, IssueInvoiceButton } from "@/components/invoices/InvoiceActions";
import { useInvoices } from "@/api/invoices.api";
import { formatCOP } from "@/lib/format-cop";
import { DOCUMENT_LABELS, INVOICE_BADGE } from "@/types/invoice";
import { INVOICE_STATUS_LABELS, type Sale } from "@/types/sale";

export function SaleInvoices({ sale }: { sale: Sale }) {
  const { data } = useInvoices({ saleId: sale.id, page: 1, pageSize: 20 });
  const documentos = data?.items ?? [];
  const vigente = documentos.some((f) => f.tipoDocumento === "Factura" && (f.estado === "Emitida" || f.estado === "Pendiente"));

  return (
    <section className="space-y-2 rounded-lg border border-dashed p-3 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <FileText className="h-4 w-4 text-muted-foreground" />
        <span className="font-medium">Factura electrónica</span>
        <Badge variant={INVOICE_BADGE[sale.facturaEstado] ?? "outline"}>{INVOICE_STATUS_LABELS[sale.facturaEstado] ?? sale.facturaEstado}</Badge>
        {sale.estado === "Confirmada" && !vigente && (
          <span className="ml-auto">
            <IssueInvoiceButton saleId={sale.id} size="sm" label={sale.facturaEstado === "Rechazada" ? "Emitir de nuevo" : "Emitir factura"} />
          </span>
        )}
      </div>
      {documentos.length === 0 ? (
        <p className="text-muted-foreground">Aún sin factura electrónica.</p>
      ) : (
        <ul className="divide-y rounded-md border">
          {documentos.map((f) => (
            <li key={f.id} className="space-y-2 px-3 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{DOCUMENT_LABELS[f.tipoDocumento]}</span>
                <span className="tabular-nums">{f.numero ?? "sin número"}</span>
                <Badge variant={INVOICE_BADGE[f.estado]}>{INVOICE_STATUS_LABELS[f.estado]}</Badge>
                <span className="ml-auto tabular-nums">{formatCOP(f.total)}</span>
              </div>
              {f.cufe && <p className="break-all text-xs text-muted-foreground">CUFE {f.cufe}</p>}
              {f.motivo && <p className="text-xs text-muted-foreground">Motivo: {f.motivo}</p>}
              {f.mensaje && <p className="whitespace-pre-line text-xs text-destructive">{f.mensaje}</p>}
              <InvoiceActions invoice={f} compact />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

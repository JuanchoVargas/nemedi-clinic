// ============================================================
// InvoiceActions.tsx — Botón "Emitir factura" y acciones de un documento emitido
//
// IssueInvoiceButton: emite desde una venta o un pago. El backend responde 200 aunque
// el proveedor rechace (el documento queda Rechazada con su motivo): aquí se avisa.
// InvoiceActions: PDF, XML, reenviar por correo, nota crédito y reintentar, según estado.
// ============================================================
import { useState } from "react";
import { FileCode2, FileDown, FileText, Mail, RefreshCw, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FormDialog } from "@/components/shared/FormDialog";
import { CreditNoteDialog } from "@/components/invoices/CreditNoteDialog";
import { useIssueInvoice, useRetryInvoice, useSendInvoiceEmail } from "@/api/invoices.api";
import { reportInvoiceResult } from "@/lib/invoice-result";
import { openSignedFile } from "@/lib/open-signed-file";
import { useToastStore } from "@/stores/toast.store";
import { DOCUMENT_LABELS, type Invoice } from "@/types/invoice";

export function IssueInvoiceButton({ saleId, paymentId, size = "default", label = "Emitir factura" }: {
  saleId?: string;
  paymentId?: string;
  size?: "default" | "sm";
  label?: string;
}) {
  const issue = useIssueInvoice();
  return (
    <Button
      size={size}
      variant="outline"
      disabled={issue.isPending}
      onClick={async () => {
        try {
          reportInvoiceResult(await issue.mutateAsync(saleId ? { saleId } : { paymentId }));
        } catch {
          // toast global (501 sin configurar, 409 ya facturada)
        }
      }}
    >
      <FileText className="mr-2 h-4 w-4" />
      {issue.isPending ? "Emitiendo..." : label}
    </Button>
  );
}

export function InvoiceActions({ invoice, compact = false }: { invoice: Invoice; compact?: boolean }) {
  const retry = useRetryInvoice();
  const [mailing, setMailing] = useState(false);
  const [crediting, setCrediting] = useState(false);
  const emitida = invoice.estado === "Emitida" || invoice.estado === "Anulada";
  const size = compact ? "sm" : "default";
  const faltanArchivos = emitida && (!invoice.pdfId || !invoice.xmlId);
  const puedeAcreditar = invoice.tipoDocumento === "Factura" && invoice.estado === "Emitida";
  const reintentable = invoice.estado === "Pendiente" || invoice.estado === "Rechazada" || faltanArchivos;

  return (
    <div className="flex flex-wrap gap-2">
      {invoice.pdfId && (
        <Button size={size} variant="outline" onClick={() => void openSignedFile(invoice.pdfId!)}>
          <FileDown className="mr-1.5 h-4 w-4" />
          PDF
        </Button>
      )}
      {invoice.xmlId && (
        <Button size={size} variant="outline" onClick={() => void openSignedFile(invoice.xmlId!)}>
          <FileCode2 className="mr-1.5 h-4 w-4" />
          XML
        </Button>
      )}
      {emitida && (
        <Button size={size} variant="outline" onClick={() => setMailing(true)}>
          <Mail className="mr-1.5 h-4 w-4" />
          Reenviar
        </Button>
      )}
      {puedeAcreditar && (
        <Button size={size} variant="outline" onClick={() => setCrediting(true)}>
          <Undo2 className="mr-1.5 h-4 w-4" />
          Nota crédito
        </Button>
      )}
      {reintentable && (
        <Button
          size={size}
          variant={invoice.estado === "Rechazada" ? "default" : "outline"}
          disabled={retry.isPending}
          onClick={async () => {
            try {
              reportInvoiceResult(await retry.mutateAsync(invoice.id));
            } catch {
              // toast global
            }
          }}
        >
          <RefreshCw className="mr-1.5 h-4 w-4" />
          {retry.isPending ? "Enviando..." : invoice.estado === "Rechazada" ? "Emitir de nuevo" : faltanArchivos ? "Descargar PDF/XML" : "Reintentar"}
        </Button>
      )}
      {mailing && <SendEmailDialog invoice={invoice} onClose={() => setMailing(false)} />}
      {crediting && <CreditNoteDialog invoice={invoice} onClose={() => setCrediting(false)} />}
    </div>
  );
}

function SendEmailDialog({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const send = useSendInvoiceEmail();
  const [correo, setCorreo] = useState(invoice.clienteEmail ?? "");
  const valido = /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo.trim());

  return (
    <FormDialog
      open
      onOpenChange={(o) => !o && onClose()}
      title={`Reenviar ${DOCUMENT_LABELS[invoice.tipoDocumento].toLowerCase()} ${invoice.numero}`}
      description="El proveedor envía el PDF y el XML al correo."
      actions={
        <Button
          disabled={!valido || send.isPending}
          onClick={async () => {
            try {
              await send.mutateAsync({ id: invoice.id, correo: correo.trim() });
              useToastStore.success(`Enviada a ${correo.trim()}`);
              onClose();
            } catch {
              // toast global (el proveedor rechazó el correo)
            }
          }}
        >
          {send.isPending ? "Enviando..." : "Enviar"}
        </Button>
      }
    >
      <div className="space-y-1.5 pt-1">
        <Label htmlFor="correo-factura">Correo</Label>
        <Input id="correo-factura" type="email" value={correo} onChange={(e) => setCorreo(e.target.value)} placeholder="paciente@example.com" />
        {invoice.correoEnviadoA && <p className="text-xs text-muted-foreground">Último envío: {invoice.correoEnviadoA}</p>}
      </div>
    </FormDialog>
  );
}

// ============================================================
// ProductImportPage.tsx — Importar productos y su stock por lote (/inventory/import)
//
// Solo dueño y recepción (ImportsController: policy Admin). Mismo asistente que pacientes.
// ============================================================
import { ImportWizard } from "@/components/import/ImportWizard";

export function ProductImportPage() {
  return <ImportWizard entity="products" title="Importar productos" noun="productos" nounOne="producto" duplicateKey="misma referencia, o mismo nombre si no tiene" backTo="/inventory" />;
}

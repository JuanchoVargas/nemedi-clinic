// ============================================================
// PackageImportPage.tsx — Importar paquetes con sus sesiones (/packages/import)
//
// Solo dueño y recepción (ImportsController: policy Admin). Mismo asistente que pacientes.
// ============================================================
import { ImportWizard } from "@/components/import/ImportWizard";

export function PackageImportPage() {
  return <ImportWizard entity="packages" title="Importar paquetes" noun="paquetes" nounOne="paquete" duplicateKey="mismo nombre" backTo="/packages" />;
}

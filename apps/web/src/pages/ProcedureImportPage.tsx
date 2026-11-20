// ============================================================
// ProcedureImportPage.tsx — Importar procedimientos (/procedures/import)
//
// Solo dueño y recepción (ImportsController: policy Admin). Mismo asistente que pacientes.
// ============================================================
import { ImportWizard } from "@/components/import/ImportWizard";

export function ProcedureImportPage() {
  return <ImportWizard entity="procedures" title="Importar procedimientos" noun="procedimientos" nounOne="procedimiento" duplicateKey="mismo nombre" backTo="/procedures" />;
}

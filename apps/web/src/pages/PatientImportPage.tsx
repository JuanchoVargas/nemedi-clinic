// ============================================================
// PatientImportPage.tsx — Importar pacientes desde Excel/CSV (/patients/import)
//
// Solo dueño y recepción (ImportsController: policy Admin). El asistente es genérico:
// productos, procedimientos y paquetes usan el mismo con otra entidad.
// ============================================================
import { ImportWizard } from "@/components/import/ImportWizard";

export function PatientImportPage() {
  return <ImportWizard entity="patients" title="Importar pacientes" noun="pacientes" nounOne="paciente" duplicateKey="mismo documento" backTo="/patients" />;
}

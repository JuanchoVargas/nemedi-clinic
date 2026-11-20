// ============================================================
// app-short-name.ts — El nombre que se ve bajo el ícono de la app instalada
//
// Caben ~12 caracteres. "Nemedi Clinic" → "NemediClinic" (sin espacios, si cabe);
// si aun así es largo, su primera palabra ("Canal Demo Clinic" → "Canal").
// La misma regla está en vite.config.ts (manifest de respaldo) y en
// BrandingController (manifest del canal): si cambias una, cambia las tres.
// ============================================================
export function appShortName(name: string): string {
  const compact = name.replace(/\s+/g, "");
  if (compact.length <= 12) return compact;
  return name.trim().split(/\s+/)[0];
}

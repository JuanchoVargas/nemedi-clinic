// ============================================================
// use-app-short-name.ts — El nombre bajo el ícono de la app instalada
//
// El del canal del dominio (GET /branding), con la misma regla del manifest.
// ============================================================
import { DEFAULT_BRANDING, useBranding } from "@/api/branding.api";
import { appShortName } from "@/lib/app-short-name";

export function useAppShortName() {
  const { data } = useBranding();
  return appShortName((data ?? DEFAULT_BRANDING).nombreComercial);
}

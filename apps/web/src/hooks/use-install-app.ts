// ============================================================
// use-install-app.ts — ¿Se ofrece "Instalar la app" aquí, y cómo?
//
//   · "prompt": Android con Chrome/Edge y el evento guardado → un botón que
//     abre el diálogo de instalación del navegador.
//   · "ios": iPhone o iPad. Safari no tiene ese evento ni un botón que instale
//     la app: solo se explica el camino (Compartir → "Agregar a inicio").
//   · null: computador, ya instalada (abierta desde el ícono, `appinstalled` o
//     "Ya la agregué" en iPhone), descartada en
//     esta sesión, o un navegador de Android que no ofrece instalarla.
//
// La usan la tarjeta del inicio y el menú de usuario (components/shared/InstallApp.tsx).
// ============================================================
import { isIos, isMobileDevice, isStandalone } from "@/lib/device";
import { usePwaInstallStore } from "@/stores/pwa-install.store";

export type InstallMode = "prompt" | "ios" | null;

export function useInstallApp() {
  const hasPrompt = usePwaInstallStore((s) => s.promptEvent !== null);
  const installed = usePwaInstallStore((s) => s.installed);
  const dismissed = usePwaInstallStore((s) => s.dismissed);
  const install = usePwaInstallStore((s) => s.install);
  const dismiss = usePwaInstallStore((s) => s.dismiss);
  const markInstalled = usePwaInstallStore((s) => s.markInstalled);

  let mode: InstallMode = null;
  if (!installed && !dismissed && isMobileDevice() && !isStandalone()) {
    if (hasPrompt) mode = "prompt";
    else if (isIos()) mode = "ios";
  }
  return { mode, install, dismiss, markInstalled };
}

/**
 * "Tienes la app instalada, ábrela desde tu pantalla de inicio": en el celular, abierta en el
 * navegador, con la app anotada como instalada y sin haber cerrado el aviso en esta sesión.
 */
export function useInstalledAppNotice() {
  const installed = usePwaInstallStore((s) => s.installed);
  const justInstalled = usePwaInstallStore((s) => s.justInstalled);
  const noticeDismissed = usePwaInstallStore((s) => s.noticeDismissed);
  const dismissNotice = usePwaInstallStore((s) => s.dismissNotice);
  const show = installed && !justInstalled && !noticeDismissed && isMobileDevice() && !isStandalone();
  return { show, dismissNotice };
}

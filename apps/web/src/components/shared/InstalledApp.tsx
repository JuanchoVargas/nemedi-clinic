// ============================================================
// InstalledApp.tsx — Después de instalar la app (PWA)
//
//   · InstalledAppConfirmation: al terminar la instalación (`appinstalled`) el
//     navegador no muestra nada claro. Carga en diferido InstalledAppDialog,
//     que dice dónde quedó, con el ícono y el nombre de la pantalla de inicio.
//   · InstalledAppNotice: la app está instalada pero la abrieron en el
//     navegador → aviso discreto para abrirla desde el inicio (se cierra por sesión).
// Van en RootLayout (con y sin sesión), así que aquí solo lo liviano: el diálogo
// va en su propio chunk y no pesa en el login.
// ============================================================
import { lazy, Suspense } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useInstalledAppNotice } from "@/hooks/use-install-app";
import { useAppShortName } from "@/hooks/use-app-short-name";
import { usePwaInstallStore } from "@/stores/pwa-install.store";

const InstalledAppDialog = lazy(() => import("@/components/shared/InstalledAppDialog"));

export function InstalledAppConfirmation() {
  const open = usePwaInstallStore((s) => s.justInstalled);
  if (!open) return null;
  return (
    <Suspense fallback={null}>
      <InstalledAppDialog />
    </Suspense>
  );
}

export function InstalledAppNotice() {
  const { show, dismissNotice } = useInstalledAppNotice();
  const nombre = useAppShortName();
  if (!show) return null;

  return (
    <div
      role="status"
      className="fixed inset-x-3 bottom-3 z-40 mx-auto flex max-w-md items-center gap-3 rounded-xl border bg-card p-2.5 pr-1.5 text-sm shadow-lift"
    >
      <img src="/icons/icon-192.png" alt="" width={36} height={36} className="size-9 shrink-0 rounded-lg" />
      <p className="min-w-0 flex-1">
        Tienes la app <strong className="font-medium">{nombre}</strong> instalada: ábrela desde tu pantalla de inicio.
      </p>
      <Button variant="ghost" size="icon" className="size-8 shrink-0" onClick={dismissNotice} aria-label="Cerrar aviso">
        <X />
      </Button>
    </div>
  );
}

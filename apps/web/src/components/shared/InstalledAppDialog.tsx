// ============================================================
// InstalledAppDialog.tsx — "Listo, la app ya está instalada"
//
// Se carga en diferido (InstalledApp.tsx) solo cuando llega `appinstalled`.
// Muestra el ícono y el nombre tal como quedan en la pantalla de inicio, para
// que la persona sepa qué buscar.
// ============================================================
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useAppShortName } from "@/hooks/use-app-short-name";
import { usePwaInstallStore } from "@/stores/pwa-install.store";

export default function InstalledAppDialog() {
  const confirmSeen = usePwaInstallStore((s) => s.confirmSeen);
  const nombre = useAppShortName();

  return (
    <Dialog open onOpenChange={(o) => !o && confirmSeen()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader className="items-center text-center">
          <div className="mb-2 flex flex-col items-center gap-1.5">
            <img src="/icons/icon-192.png" alt="" width={64} height={64} className="size-16 rounded-2xl shadow-soft" />
            <span className="text-xs font-medium">{nombre}</span>
          </div>
          <DialogTitle>Listo, {nombre} ya está instalada</DialogTitle>
          <DialogDescription>Búscala en tu pantalla de inicio con este ícono y ábrela desde ahí.</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button className="w-full" onClick={confirmSeen}>
            Entendido
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

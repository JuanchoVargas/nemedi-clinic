// ============================================================
// InstallApp.tsx — "Instalar la app" (PWA) en el celular
//
//   · InstallAppCard (pantalla de inicio) e InstallAppMenuItems (menú de usuario):
//     la oferta. En Android un botón que abre el diálogo del navegador; en
//     iPhone/iPad el camino (Compartir → "Agregar a inicio") y "Ya la agregué",
//     que la da por instalada en este navegador (Safari no avisa). Nada en un
//     computador, con la app ya instalada o si la descartaron en esta sesión.
// Qué pasa después de instalar: components/shared/InstalledApp.tsx.
// ============================================================
import { Download, Share, Smartphone, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator } from "@/components/ui/dropdown-menu";
import { useInstallApp } from "@/hooks/use-install-app";
import { cn } from "@/lib/utils";

/** Safari no tiene un botón que instale la app: se explica el camino, sin imitar avisos del sistema. */
function IosSteps({ className }: { className?: string }) {
  return (
    <p className={cn("text-sm text-muted-foreground", className)}>
      Toca <Share aria-hidden="true" className="inline size-4 align-text-bottom text-foreground" />{" "}
      <strong className="font-medium text-foreground">Compartir</strong> y luego{" "}
      <strong className="font-medium text-foreground">Agregar a inicio</strong>.
    </p>
  );
}

/** Tarjeta de la pantalla de inicio, junto a Dashboard, Pacientes y Autenticación. */
export function InstallAppCard() {
  const { mode, install, dismiss, markInstalled } = useInstallApp();
  if (!mode) return null;

  return (
    <Card>
      <CardHeader>
        <Smartphone className="mb-2 h-8 w-8 text-primary" />
        <CardTitle>Instalar la app</CardTitle>
        <CardDescription>Queda en la pantalla de inicio y abre a pantalla completa, como cualquier app.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {mode === "prompt" ? (
          <Button className="w-full" onClick={() => void install()}>
            <Download />
            Instalar la app
          </Button>
        ) : (
          <>
            <IosSteps />
            <Button className="w-full" onClick={markInstalled}>
              Ya la agregué
            </Button>
          </>
        )}
        <Button variant="ghost" className="w-full" onClick={dismiss}>
          Ahora no
        </Button>
      </CardContent>
    </Card>
  );
}

/** Para el menú de usuario: va dentro de DropdownMenuContent. */
export function InstallAppMenuItems() {
  const { mode, install, dismiss, markInstalled } = useInstallApp();
  if (!mode) return null;

  if (mode === "prompt") {
    return (
      <DropdownMenuItem onSelect={() => void install()}>
        <Download />
        Instalar la app
      </DropdownMenuItem>
    );
  }

  return (
    <>
      <DropdownMenuSeparator />
      <DropdownMenuLabel className="font-normal">
        <p className="text-sm font-medium text-foreground">Instalar la app</p>
        <IosSteps className="mt-0.5 text-xs" />
      </DropdownMenuLabel>
      <DropdownMenuItem onSelect={markInstalled}>
        <Smartphone />
        Ya la agregué
      </DropdownMenuItem>
      <DropdownMenuItem onSelect={dismiss}>
        <X />
        Ahora no
      </DropdownMenuItem>
    </>
  );
}

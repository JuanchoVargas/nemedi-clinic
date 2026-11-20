// ============================================================
// RootLayout.tsx — Composición del layout raíz
//
// Dos caras según la sesión:
//   · Autenticado → AppShell (sidebar + header), en su propio chunk: se descarga
//     después del login, no con él.
//   · Público (/, /login) o con clave temporal → header mínimo con la marca + pie.
// También aplica la marca del canal (useApplyBranding) y suelta los avisos
// que el guard de rutas encoló antes de que existiera el Toaster. La confirmación de
// "app instalada" y el aviso de abrirla desde el inicio van aquí: sirven con y sin sesión.
//
// EQUIVALENTE A: App.vue (la parte de template) en SINERGIA
// ============================================================

import { lazy, Suspense, useEffect } from "react";
import { Link, Outlet, useLocation } from "@tanstack/react-router";
import { Footer } from "@/components/layout/Footer";
import { InstalledAppConfirmation, InstalledAppNotice } from "@/components/shared/InstalledApp";
import { useApplyBranding } from "@/hooks/use-apply-branding";
import { useAuthStore } from "@/stores/auth.store";
import { useToastStore } from "@/stores/toast.store";

const AppShell = lazy(() => import("@/components/layout/AppShell"));

export function RootLayout() {
  const branding = useApplyBranding();
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated());
  const mustChangePassword = useAuthStore((s) => !!s.user?.mustChangePassword);
  const pathname = useLocation({ select: (l) => l.pathname });

  // Avisos que el guard de rutas encoló antes de que existiera el Toaster ("No tienes permisos…")
  useEffect(() => {
    useToastStore.flushDeferred();
  }, [pathname]);

  // Sin sesión, o con clave temporal (solo puede ver /change-password): layout mínimo, sin menú
  if (!isAuthenticated || mustChangePassword) {
    return (
      <div className="flex min-h-screen flex-col">
        <header className="border-b bg-card">
          <div className="container mx-auto flex h-14 items-center px-4">
            <Link to="/" className="inline-flex items-center gap-2 font-heading text-lg font-bold">
              {branding.logoUrl && <img src={branding.logoUrl} alt="" className="h-7 w-auto object-contain" />}
              {branding.nombreComercial}
            </Link>
          </div>
        </header>
        <main key={pathname} className="page-fade flex-1">
          <Outlet />
        </main>
        <Footer />
        <InstalledAppConfirmation />
        <InstalledAppNotice />
      </div>
    );
  }

  return (
    <>
      <Suspense fallback={<div className="min-h-screen bg-background" />}>
        <AppShell />
      </Suspense>
      <InstalledAppConfirmation />
      <InstalledAppNotice />
    </>
  );
}

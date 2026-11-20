// ============================================================
// AppShell.tsx — Cara autenticada del layout (se carga aparte)
//
// Sidebar colapsable + header (breadcrumb, buscador, usuario), aviso de cuenta
// suspendida y transición de página. Va en su propio chunk (React.lazy en
// RootLayout): la pantalla de login no descarga el menú, la paleta de comandos
// ni `motion`. <MotionConfig reducedMotion="user"> respeta prefers-reduced-motion.
// ============================================================
import { Outlet, useLocation } from "@tanstack/react-router";
import { MotionConfig } from "motion/react";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";
import { TooltipProvider } from "@/components/ui/tooltip";
import { AppHeader } from "@/components/layout/AppHeader";
import { AppSidebar } from "@/components/layout/AppSidebar";
import { PageTransition } from "@/components/shared/motion";
import { SuspendedBanner } from "@/components/shared/SuspendedBanner";

export default function AppShell() {
  const pathname = useLocation({ select: (l) => l.pathname });
  return (
    <MotionConfig reducedMotion="user">
      <TooltipProvider delayDuration={200}>
        <SidebarProvider>
          <AppSidebar />
          <SidebarInset className="min-w-0 bg-background">
            <AppHeader />
            <SuspendedBanner />
            <div className="flex-1">
              <PageTransition routeKey={pathname}>
                <Outlet />
              </PageTransition>
            </div>
          </SidebarInset>
        </SidebarProvider>
      </TooltipProvider>
    </MotionConfig>
  );
}

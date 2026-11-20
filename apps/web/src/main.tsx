// ============================================================
// main.tsx — Entry point: monta React + providers globales
//
// EQUIVALENTE A: main.js en SINERGIA
// Orden de providers importa: QueryClientProvider envuelve a RouterProvider
// para que los queries tengan acceso al cliente. ThemeProvider va por fuera:
// el Toaster de sonner lee el tema.
// ============================================================

import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { SHOW_DEVTOOLS } from "@/lib/devtools";
import { RouterProvider } from "@tanstack/react-router";
import { ThemeProvider } from "next-themes";
import { Toaster } from "@/components/ui/sonner";
import { queryClient } from "@/lib/query-client";
import { router } from "@/router";
import { listenForInstallPrompt } from "@/stores/pwa-install.store";
import "@/styles/globals.css";

// Antes de montar: Chrome puede ofrecer instalar la app antes de que exista el componente que lo muestra
listenForInstallPrompt();

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {/* Tema claro/oscuro: clase .dark en <html>, preferencia guardada por next-themes */}
    <ThemeProvider attribute="class" defaultTheme="light" enableSystem={false} disableTransitionOnChange>
      <QueryClientProvider client={queryClient}>
        {/* El TooltipProvider vive en AppShell: solo la app autenticada usa tooltips */}
        <RouterProvider router={router} />
        <Toaster richColors position="top-right" />
        {SHOW_DEVTOOLS && <ReactQueryDevtools initialIsOpen={false} />}
      </QueryClientProvider>
    </ThemeProvider>
  </React.StrictMode>,
);

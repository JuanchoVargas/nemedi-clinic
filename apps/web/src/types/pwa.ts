/** Evento `beforeinstallprompt` de Chrome/Edge (Android): aún no está en lib.dom. */
export interface BeforeInstallPromptEvent extends Event {
  /** Abre el diálogo de instalación del navegador. Sirve una sola vez por evento. */
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed"; platform: string }>;
}

// ============================================================
// pwa-install.store.ts — "Instalar la app" (PWA)
//
// Chrome/Edge en Android disparan `beforeinstallprompt` cuando la app se
// puede instalar, a veces antes de que React monte (la cara autenticada se
// carga aparte). Por eso `listenForInstallPrompt()` se llama en main.tsx,
// antes de pintar, y guarda el evento aquí para la tarjeta del inicio y el
// menú de usuario.
//
// Qué se recuerda y dónde (preferencias de interfaz, no datos de negocio):
//   · "Ahora no" (o cancelar el diálogo del navegador): sessionStorage, se
//     vuelve a ofrecer en otra sesión.
//   · Que la app ya está instalada: localStorage. Lo marcan `appinstalled`,
//     abrir la app desde su ícono (display-mode standalone) y, en iPhone, el
//     botón "Ya la agregué" (Safari no avisa cuando se agrega a inicio). En
//     Android la app instalada comparte el almacenamiento con Chrome; en iOS
//     no, así que allí solo cuenta "Ya la agregué".
//   · `justInstalled`: `appinstalled` acaba de llegar en esta pestaña → la
//     confirmación con el ícono (InstalledAppDialog).
// ============================================================

import { create } from "zustand";
import { isStandalone } from "@/lib/device";
import type { BeforeInstallPromptEvent } from "@/types/pwa";

const DISMISSED_KEY = "pwa-install-dismissed";
const INSTALLED_KEY = "pwa-installed";
const NOTICE_KEY = "pwa-installed-notice-dismissed";

function read(storage: () => Storage, key: string): boolean {
  try {
    return storage().getItem(key) === "1";
  } catch {
    return false; // almacenamiento bloqueado
  }
}

function write(storage: () => Storage, key: string) {
  try {
    storage().setItem(key, "1");
  } catch {
    // sin almacenamiento: vale hasta recargar
  }
}

const session = () => sessionStorage;
const local = () => localStorage;

interface PwaInstallState {
  /** El evento guardado: solo existe en Chrome/Edge y cuando la app se puede instalar. */
  promptEvent: BeforeInstallPromptEvent | null;
  /** Instalada (aquí o antes, según lo que se sabe en este navegador). */
  installed: boolean;
  /** `appinstalled` acaba de llegar: falta mostrar la confirmación. */
  justInstalled: boolean;
  /** La oferta se descartó en esta sesión. */
  dismissed: boolean;
  /** El aviso "Tienes la app instalada" se cerró en esta sesión. */
  noticeDismissed: boolean;
  /** Abre el diálogo del navegador; cancelarlo cuenta como descartar. */
  install: () => Promise<void>;
  dismiss: () => void;
  /** iPhone: "Ya la agregué". No se vuelve a ofrecer en este navegador. */
  markInstalled: () => void;
  confirmSeen: () => void;
  dismissNotice: () => void;
}

export const usePwaInstallStore = create<PwaInstallState>((set, get) => ({
  promptEvent: null,
  installed: read(local, INSTALLED_KEY),
  justInstalled: false,
  dismissed: read(session, DISMISSED_KEY),
  noticeDismissed: read(session, NOTICE_KEY),

  install: async () => {
    const event = get().promptEvent;
    if (!event) return;
    set({ promptEvent: null }); // prompt() sirve una sola vez por evento
    try {
      await event.prompt();
      const { outcome } = await event.userChoice;
      if (outcome === "dismissed") get().dismiss();
    } catch {
      // el navegador no abrió el diálogo: la oferta ya se retiró
    }
  },

  dismiss: () => {
    write(session, DISMISSED_KEY);
    set({ dismissed: true, promptEvent: null });
  },

  markInstalled: () => {
    write(local, INSTALLED_KEY);
    // Recién la agregó: el aviso de "ábrela desde tu inicio" no hace falta en esta sesión
    write(session, NOTICE_KEY);
    set({ installed: true, promptEvent: null, noticeDismissed: true });
  },

  confirmSeen: () => set({ justInstalled: false }),

  dismissNotice: () => {
    write(session, NOTICE_KEY);
    set({ noticeDismissed: true });
  },
}));

let listening = false;

/** Una sola vez, en main.tsx, antes de montar React. */
export function listenForInstallPrompt() {
  if (listening || typeof window === "undefined") return;
  listening = true;
  // Abierta desde el ícono: queda anotado para cuando la abran desde el navegador
  if (isStandalone()) write(local, INSTALLED_KEY);
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault(); // sin el aviso propio del navegador: la oferta es el botón de la app
    // Chrome solo lo dispara si la app NO está instalada: si estaba anotada, la desinstalaron
    try {
      localStorage.removeItem(INSTALLED_KEY);
    } catch {
      // sin almacenamiento
    }
    usePwaInstallStore.setState({ promptEvent: event as BeforeInstallPromptEvent, installed: false });
  });
  window.addEventListener("appinstalled", () => {
    write(local, INSTALLED_KEY);
    write(session, NOTICE_KEY);
    usePwaInstallStore.setState({ installed: true, justInstalled: true, promptEvent: null, noticeDismissed: true });
  });
}

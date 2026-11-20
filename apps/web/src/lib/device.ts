// ============================================================
// device.ts — Qué dispositivo es y cómo está abierta la app
//
// Por sistema operativo, no por ancho de pantalla: una ventana angosta en un
// computador sigue siendo un computador (useIsMobile mide el viewport y sirve
// para el layout, no para esto).
// ============================================================

function userAgent(): string {
  return typeof navigator === "undefined" ? "" : navigator.userAgent;
}

/** iPhone, iPad o iPod. El iPad se presenta como Mac: se reconoce por la pantalla táctil. */
export function isIos(): boolean {
  if (typeof navigator === "undefined") return false;
  return /iphone|ipad|ipod/i.test(userAgent()) || (/macintosh/i.test(userAgent()) && navigator.maxTouchPoints > 1);
}

/** Celular o tablet (Android o iOS). En un computador, false: aunque la ventana sea angosta. */
export function isMobileDevice(): boolean {
  return /android/i.test(userAgent()) || isIos();
}

/** Abierta como app instalada (desde el ícono de inicio), no en una pestaña del navegador. */
export function isStandalone(): boolean {
  if (typeof window === "undefined") return false;
  return (
    window.matchMedia("(display-mode: standalone)").matches ||
    window.matchMedia("(display-mode: fullscreen)").matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

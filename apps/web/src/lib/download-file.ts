// ============================================================
// download-file.ts — Guarda en disco un archivo que devolvió el API
//
// El nombre sale de Content-Disposition (lo arma el backend); si no llega, el de respaldo.
// Lo usan las descargas de Excel (reporte sanitario, arqueo de caja, reportes).
// ============================================================
export function saveBlob(data: Blob, headers: Record<string, unknown>, fallbackName: string): void {
  const disposition = String(headers["content-disposition"] ?? "");
  const nombre = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName;

  const url = URL.createObjectURL(data);
  const a = document.createElement("a");
  a.href = url;
  a.download = decodeURIComponent(nombre);
  a.click();
  URL.revokeObjectURL(url);
}

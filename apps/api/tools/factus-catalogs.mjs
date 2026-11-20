// ============================================================
// factus-catalogs.mjs — Regenera los catálogos DIAN que usa la facturación con Factus
//
//   node apps/api/tools/factus-catalogs.mjs
//
// La API v2 de Factus NO expone las tablas de referencia (municipios, unidades de medida, tipos
// de documento, métodos de pago, tributos…): /v2/municipalities y similares responden 404 y la
// colección oficial de Postman no las trae. Solo existen en la documentación oficial. Este script
// las baja de ahí y escribe Invoicing/factus-catalogs.json, que la API carga una vez y cachea
// (FactusCatalogService). Así ningún código DIAN queda escrito en C#: cuando la DIAN o Factus
// cambien una tabla, se corre el script y se versiona el JSON.
// ============================================================
import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const DOCS = "https://developers.factus.com.co/tablas-de-referencia";
const OUT = resolve(dirname(fileURLToPath(import.meta.url)), "../src/NemediClinic.Infrastructure/Invoicing/factus-catalogs.json");

// Título de la tabla en la documentación → clave estable en el JSON
const TABLES = {
  "Códigos de tipos de documentos de identidad": "identificationDocuments",
  "Código de tipos de organizaciones": "legalOrganizations",
  "Códigos de tributos clientes": "customerTributes",
  "Códigos de impuestos": "taxes",
  "Códigos de métodos de pago": "paymentMethods",
  "Códigos de formas de pago": "paymentForms",
  "Códigos de corrección (notas crédito)": "creditNoteCorrections",
  "Códigos de estándar de identificación del producto": "standardCodes",
  "Responsabilidades fiscales": "responsibilities",
};

const decode = (s) =>
  s.replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(Number(d)))
    .replace(/&quot;/g, '"').replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&amp;/g, "&");
const text = (html) => decode(html.replace(/<[^>]+>/g, "")).replace(/\s+/g, " ").trim();

async function page(path) {
  const response = await fetch(`${DOCS}/${path}/`);
  if (!response.ok) throw new Error(`${path}: HTTP ${response.status}`);
  return response.text();
}

/** El bloque de código JSON de la página (Expressive Code lo guarda completo en data-code). */
function codeBlock(html, key) {
  for (const [, raw] of html.matchAll(/data-code="([^"]*)"/g)) {
    const json = decode(raw).replace(/\u007f/g, "\n");
    if (json.includes(`"${key}"`)) return JSON.parse(json)[key];
  }
  throw new Error(`No se encontró el bloque JSON con "${key}"`);
}

function tables(html) {
  const result = {};
  for (const match of html.matchAll(/<table[\s\S]*?<\/table>/g)) {
    const before = text(html.slice(Math.max(0, match.index - 800), match.index));
    const title = /Sección titulada «([^»]+)»\s*$/.exec(before)?.[1];
    const key = title && TABLES[title];
    if (!key) continue;
    result[key] = [...match[0].matchAll(/<tr>([\s\S]*?)<\/tr>/g)]
      .map(([, row]) => [...row.matchAll(/<td[^>]*>([\s\S]*?)<\/td>/g)].map(([, cell]) => text(cell)))
      .filter((cells) => cells.length >= 2)
      .map(([code, name]) => ({ code, name: name.replace(/\s*\*+$/, "") })); // "NUIP*": asterisco de nota al pie
  }
  const missing = Object.values(TABLES).filter((k) => !result[k]?.length);
  if (missing.length) throw new Error(`Faltan tablas en la documentación: ${missing.join(", ")}`);
  return result;
}

const [general, municipios, unidades] = await Promise.all([page("tablas"), page("municipios"), page("unit-measures")]);
const catalogs = {
  source: `${DOCS}/`,
  generatedAt: new Date().toISOString().slice(0, 10),
  ...tables(general),
  municipalities: codeBlock(municipios, "municipalities").map((m) => ({ code: m.code, name: `${m.name} (${m.department.name})` })),
  unitMeasures: codeBlock(unidades, "unitMeasures").map((u) => ({ code: String(u.code), name: u.name })),
};
writeFileSync(OUT, JSON.stringify(catalogs, null, 1) + "\n");
console.log(`${OUT}\n` + Object.entries(catalogs).filter(([, v]) => Array.isArray(v)).map(([k, v]) => `  ${k}: ${v.length}`).join("\n"));

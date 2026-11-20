// ============================================================
// SatisfactionPicker.tsx — Cómo se sintió la paciente (1 a 5)
//
// Lo usan la nota nueva y la corrección de una nota.
// ============================================================

/**
 * Cómo se sintió la paciente, de 1 a 5. Es un grupo de radio para que funcione con el
 * teclado; volver a pulsar el valor elegido lo desmarca (el campo es opcional).
 */
export function SatisfactionPicker({ value, onChange }: { value: number | null; onChange: (v: number | null) => void }) {
  return (
    <div role="radiogroup" aria-label="Cómo se sintió la paciente" className="flex h-9 items-center gap-1.5">
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          role="radio"
          aria-checked={value === n}
          aria-label={`${n} de 5`}
          onClick={() => onChange(value === n ? null : n)}
          className={`h-7 w-7 rounded-full border text-xs font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring ${
            value != null && n <= value ? "border-sand bg-sand text-sand-foreground" : "border-input text-muted-foreground hover:bg-muted"
          }`}
        >
          {n}
        </button>
      ))}
      {value != null && (
        <button type="button" onClick={() => onChange(null)} className="ml-1 text-xs text-muted-foreground underline-offset-2 hover:underline">
          Quitar
        </button>
      )}
    </div>
  );
}

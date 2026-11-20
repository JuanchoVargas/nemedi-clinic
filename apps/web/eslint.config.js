import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
    rules: {
      // La API entrega y recibe hora de la clínica sin sufijo. toISOString() la pasaría a UTC
      // (5 h corrida en Bogotá). Mismo criterio que BannedSymbols.txt del backend.
      'no-restricted-properties': ['error', {
        property: 'toISOString',
        message: 'Usa toLocalIso / toLocalDate de src/lib/dates.ts: la API trabaja en hora de la clínica, no en UTC.',
      }],
    },
  },
  {
    // Componentes generados por shadcn: exportan variantes (cva) y hooks junto
    // al componente. Fast refresh no aplica a esos archivos.
    files: ['src/components/ui/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
])

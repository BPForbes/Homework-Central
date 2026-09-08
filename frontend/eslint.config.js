import js from '@eslint/js'
import globals from 'globals'
import html from 'eslint-plugin-html'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

// Pinned: no-var / prefer-const. eslint:recommended does not include them.
const noVarRules = {
  'no-var': 'error',
  'prefer-const': 'error',
}

export default tseslint.config(
  // dist is build output. public/ is served as written, so it is not ignored.
  { ignores: ['dist'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx,mts,cts}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      ...noVarRules,
    },
  },
  {
    files: ['**/*.{js,cjs,mjs,jsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: { ...globals.browser, ...globals.node },
    },
    rules: noVarRules,
  },
  {
    // Inline <script> in HTML (theme anti-flash bootstrap). The html plugin
    // extracts the script so no-var applies to the parsed source.
    files: ['**/*.{html,htm,xhtml}'],
    plugins: { html },
    languageOptions: {
      ecmaVersion: 2020,
      sourceType: 'script',
      globals: globals.browser,
    },
    rules: noVarRules,
  },
)

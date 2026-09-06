import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
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
      // The C# side blocks `var` through csharp_style_var_* in .editorconfig;
      // eslint:recommended does not carry no-var, so TypeScript needs it named
      // explicitly for the same rule to hold on both sides of the app.
      'no-var': 'error',
      'prefer-const': 'error',
    },
  },
  {
    // The block above is scoped to {ts,tsx}, which left plain JavaScript
    // unlinted. Only the two var rules are applied here: the TypeScript
    // recommended set is deliberately not extended onto config files.
    files: ['**/*.{js,cjs,mjs,jsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: { ...globals.browser, ...globals.node },
    },
    rules: {
      'no-var': 'error',
      'prefer-const': 'error',
    },
  },
)

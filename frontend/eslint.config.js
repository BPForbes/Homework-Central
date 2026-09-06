import js from '@eslint/js'
import globals from 'globals'
import html from 'eslint-plugin-html'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

// `no-var` / `prefer-const` are the JavaScript-side half of the repository-wide
// rule against implicitly typed and re-bindable locals; the C# side is
// csharp_style_var_* in .editorconfig. eslint:recommended does not carry either
// rule, so both are named explicitly. They are repeated per block rather than
// hoisted because each block has a different parser and file set.
const noVarRules = {
  'no-var': 'error',
  'prefer-const': 'error',
}

export default tseslint.config(
  // dist is build output. public/ is deliberately NOT ignored: it is served
  // verbatim, so a script there ships to users exactly as written.
  { ignores: ['dist'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    // .mts and .cts are TypeScript and were previously matched by nothing,
    // which left them with no gate at all rather than a weaker one.
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
    // The block above is scoped to TypeScript, which left plain JavaScript
    // unlinted. Only the two var rules apply here: the TypeScript recommended
    // set is deliberately not extended onto config files.
    files: ['**/*.{js,cjs,mjs,jsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: { ...globals.browser, ...globals.node },
    },
    rules: noVarRules,
  },
  {
    // Inline <script> in HTML. This is the anti-flash theme bootstrap's home,
    // and it has to stay inline to run before first paint, so it cannot be
    // moved into a module that the TypeScript block would cover.
    //
    // The processor hands eslint the real script contents, so the script is
    // parsed rather than pattern-matched. That is the point: a grep over HTML
    // cannot tell a `var` in code from one inside a comment or a string, and
    // every filter added to teach it the difference became a way to hide a
    // `var` from it.
    // The plugin registers its processor by patching the linter rather than
    // exposing a named one, so `plugins` alone wires it up; naming a
    // `processor` here fails config validation. .htm/.html are its default HTML
    // extensions and .xhtml its default XML one, so no settings override.
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

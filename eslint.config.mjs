// Covers tracked web files that live *outside* `frontend/`.
//
// `frontend/eslint.config.js` is the config for the SPA, and eslint will not
// lint above its own base path — a file in `scripts/` comes back as "File
// ignored because outside of base path", which is a warning, not an error, so
// it reads as success. That left `scripts/novar-eslint-probe.mjs` gated by
// nothing at all: the C# half of the no-var rule does not apply, the web half
// stopped at `frontend/`, and the shell gate delegates every web file to
// eslint. One uncovered file is how the rule starts eroding.
//
// `scripts/check-no-var-config.sh --web` asserts that every tracked web file is
// covered by one of the two configs, so adding a web file in a new directory
// fails the gate until it is listed here.
export default [
    {
        files: [
            // This file is itself a tracked web file outside frontend/, and the
            // first version of it did not claim itself — CI caught that, which
            // is the gate working. Root-level configs are listed explicitly
            // because `**/*` does not match a path with no directory part.
            '*.{js,mjs,cjs}',
            'scripts/**/*.{js,mjs,cjs}',
            'tools/**/*.{js,mjs,cjs}',
        ],
        languageOptions: {
            ecmaVersion: 2024,
            sourceType: 'module',
        },
        rules: {
            'no-var': 'error',
            'prefer-const': 'error',
        },
    },
]

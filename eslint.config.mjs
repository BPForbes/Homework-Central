// Web files outside frontend/. eslint started from frontend/ will not lint
// these (it reports "outside of base path" and exits 0).
export default [
    {
        files: [
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

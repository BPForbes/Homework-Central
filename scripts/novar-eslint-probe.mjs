// Asks eslint whether no-var and prefer-const are in force for every tracked
// web file, and whether they actually fire.
//
// Reads newline-separated paths (relative to frontend/) on stdin, prints one
// JSON object on stdout. Run from frontend/ by scripts/check-no-var-config.sh.
//
// Enumerates rather than samples. The previous version resolved the config for
// four representative files, and a config block scoped to src/components/**
// with 'no-var': 'off' was a complete end-to-end escape — the four samples
// still resolved to error, so every gate passed while a var sat in a real
// component. Sampling a configuration cannot show what it does elsewhere.
import { createRequire } from 'node:module'

// eslint is a dependency of frontend/, not of this script's directory, and node
// resolves bare specifiers relative to the importing *file*. A plain
// `import { ESLint } from 'eslint'` therefore fails with ERR_MODULE_NOT_FOUND
// even when run with frontend/ as the cwd. Resolving from the cwd is what makes
// the gate live in scripts/ alongside the others instead of inside the app.
const { ESLint } = createRequire(`${process.cwd()}/`)('eslint')

const chunks = []
for await (const chunk of process.stdin) {
    chunks.push(chunk)
}
const files = Buffer.concat(chunks)
    .toString('utf8')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)

const eslint = new ESLint({ cwd: process.cwd() })
const problems = []

for (const file of files) {
    // An `ignores` entry drops a file from the lint run altogether, and
    // calculateConfigForFile then reports nothing rather than a weak severity.
    // That is the more dangerous shape: a hole instead of a wrong answer. Only
    // build output is legitimately ignored.
    if (await eslint.isPathIgnored(file)) {
        if (!/^dist\//.test(file)) {
            problems.push(`${file}: excluded from linting by an ignores entry`)
        }
        continue
    }

    const config = await eslint.calculateConfigForFile(file)
    const rules = config.rules ?? {}
    for (const name of ['no-var', 'prefer-const']) {
        const entry = rules[name]
        const severity = Array.isArray(entry) ? entry[0] : entry
        if (severity !== 2 && severity !== 'error') {
            problems.push(`${file}: ${name} resolves to ${JSON.stringify(severity)}`)
        }
    }
}

// Configured is not the same as firing. A processor that hands eslint the wrong
// text, or a parser that fails open, leaves the severity correct and the rule
// inert, so plant a plain `var` in each file type and require an error back.
const firingChecks = [
    ['src/__novar_probe__.ts', 'var x = 1;\nexport default x;\n'],
    ['src/__novar_probe__.tsx', 'var x = 1;\nexport default x;\n'],
    ['src/__novar_probe__.js', 'var x = 1;\n'],
]

for (const [filePath, source] of firingChecks) {
    const results = await eslint.lintText(source, { filePath, warnIgnored: false })
    const hits = (results[0]?.messages ?? []).filter((message) => message.ruleId === 'no-var')
    if (hits.length === 0) {
        problems.push(`${filePath}: a plain \`var\` produced no no-var error`)
    }
}

console.log(JSON.stringify({ checked: files.length, problems }))

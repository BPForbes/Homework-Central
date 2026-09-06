// Asks eslint whether no-var and prefer-const are in force for every tracked
// web file, and whether they actually fire.
//
// Reads newline-separated paths on stdin. Prints `checked <n>` and one
// `problem <text>` line per finding, then `verdict ok` or `verdict problems`.
// Run by scripts/check-no-var-config.sh.
//
// Plain lines rather than a JSON object, because the shell then needs no JSON
// parser to read the verdict. It used to print JSON that the caller parsed with
// `python3 -c '...' 2>/dev/null`, unguarded: with python3 off PATH both reads
// came back empty, empty was taken to mean "no problems", and the gate passed
// while announcing "error for all  tracked web files" with the count missing.
// node already has to be present for this file to run at all, so the verdict is
// now rendered by the same interpreter that computes it.
//
// Enumerates rather than samples. An earlier version resolved the config for
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
// Tried from the cwd first, then from frontend/. The web files outside
// frontend/ have to be linted with the repo root as the base path — eslint
// refuses to lint above it — but eslint itself is installed only in
// frontend/node_modules, so resolution and base path point at different
// directories for that run.
function loadESLint() {
    const roots = [`${process.cwd()}/`, `${process.cwd()}/frontend/`]
    const failures = []
    for (const root of roots) {
        try {
            return createRequire(root)('eslint').ESLint
        } catch (error) {
            failures.push(`${root}: ${error.code ?? error.message}`)
        }
    }
    console.log(`problem could not load eslint from ${failures.join(' | ')}`)
    console.log('checked 0')
    console.log('verdict problems')
    process.exit(1)
}

const ESLint = loadESLint()

const configIndex = process.argv.indexOf('--config')
const overrideConfigFile = configIndex === -1 ? undefined : process.argv[configIndex + 1]

const chunks = []
for await (const chunk of process.stdin) {
    chunks.push(chunk)
}
const files = Buffer.concat(chunks)
    .toString('utf8')
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)

const eslint = new ESLint({ cwd: process.cwd(), overrideConfigFile })
const problems = []
let checked = 0

for (const file of files) {
    // An `ignores` entry drops a file from the lint run altogether, and
    // calculateConfigForFile then reports nothing rather than a weak severity.
    // That is the more dangerous shape: a hole instead of a wrong answer.
    //
    // Nothing is exempt here. `dist/` used to be, unconditionally, which meant
    // a tracked path under any dist/ was skipped while still counting toward
    // the "all N files" total — coverage fell as the number rose. Build output
    // is gitignored, so the caller never enumerates it, and a tracked file
    // under dist/ is a thing worth failing on.
    if (await eslint.isPathIgnored(file)) {
        problems.push(`${file}: excluded from linting by an ignores entry`)
        continue
    }

    const config = await eslint.calculateConfigForFile(file)
    const rules = config.rules ?? {}
    let covered = false
    for (const name of ['no-var', 'prefer-const']) {
        const entry = rules[name]
        const severity = Array.isArray(entry) ? entry[0] : entry
        if (severity !== 2 && severity !== 'error') {
            problems.push(`${file}: ${name} resolves to ${JSON.stringify(severity)}`)
        } else {
            covered = true
        }
    }
    if (covered) {
        checked += 1
    }
}

// Configured is not the same as firing. A processor that hands eslint the wrong
// text, or a parser that fails open, leaves the severity correct and the rule
// inert, so plant a plain `var` in each file type and require an error back.
//
// The probe paths are derived from the files actually being checked, one per
// extension, in the same directory as a real file of that type. Fixed paths
// under src/ were wrong for any run whose config claims a different tree: the
// root config covers scripts/ and the repo-root configs, so all three probes
// resolved to no config at all and the run failed for that reason rather than
// on the rule. Deriving the path also means the probe exercises the same config
// block that claims the real file next to it.
const byExtension = new Map()
for (const file of files) {
    const match = /^(.*\/)?[^/]*\.([^./]+)$/.exec(file)
    if (!match) {
        continue
    }
    const [, dir = '', extension] = match
    if (!byExtension.has(extension)) {
        byExtension.set(extension, `${dir}__novar_probe__.${extension}`)
    }
}

const scriptSample = 'var x = 1;\nexport default x;\n'
const markupSample = '<script>var x = 1;</script>\n'
const markupExtensions = new Set(['html', 'htm', 'xhtml'])

const firingChecks = []
for (const [extension, filePath] of byExtension) {
    firingChecks.push([
        filePath,
        markupExtensions.has(extension) ? markupSample : scriptSample,
    ])
}

for (const [filePath, source] of firingChecks) {
    const results = await eslint.lintText(source, { filePath, warnIgnored: false })
    const hits = (results[0]?.messages ?? []).filter((message) => message.ruleId === 'no-var')
    if (hits.length === 0) {
        problems.push(`${filePath}: a plain \`var\` produced no no-var error`)
    }
}

// Finally, lint the real files here, with inline configuration disabled, and
// report any `var` directly.
//
// This is the part that does not depend on package.json. The gate used to prove
// the rule was *configured*, and then delegate finding actual violations to
// `npm run lint:ci`, checking that the script rejected a planted probe. But the
// script is a string in a tracked file, and the probe's name was predictable
// enough to branch on: a `lint:ci` rewritten to
//
//   if ls src/novar_inline_*.scratch.ts; then ls …; exit 1; else eslint .; fi
//
// printed the probe's name and exited 1 whenever the probe existed — satisfying
// both halves of the attribution check — and ran plain `eslint .` otherwise, so
// a `var` in frontend/src/main.tsx hidden behind a blanket disable written in
// its description form survived every gate end to end. No assertion about a
// script's *output* can fix that when the adversary writes the script. Linting
// the files ourselves can.
//
// `allowInlineConfig: false` is the API form of the no-inline-config flag, so a
// blanket disable, a bare next-line disable, and the description form are all
// ignored here regardless of what any npm script does.
//
// Written without any literal directive syntax on purpose: the text scan in
// check-no-var.sh matches that syntax, and prose quoting it is a false positive.
const strict = new ESLint({
    cwd: process.cwd(),
    overrideConfigFile,
    allowInlineConfig: false,
})

// HTML is included: an inline `<script>` in index.html is where the original
// `var`s in this repository actually lived, so excluding it would leave the most
// relevant file unlinted.
const lintable = files
if (lintable.length > 0) {
    let results = []
    try {
        results = await strict.lintFiles(lintable)
    } catch (error) {
        problems.push(`could not lint the tracked files with inline config disabled: ${error.message}`)
    }
    for (const result of results) {
        for (const message of result.messages) {
            if (message.ruleId !== 'no-var' && message.ruleId !== 'prefer-const') {
                continue
            }
            problems.push(
                `${result.filePath}:${message.line}: ${message.ruleId} — ${message.message} ` +
                    '(found by this gate directly, with inline directives ignored)',
            )
        }
    }
}

console.log(`checked ${checked}`)
for (const problem of problems) {
    console.log(`problem ${problem}`)
}
console.log(problems.length === 0 ? 'verdict ok' : 'verdict problems')

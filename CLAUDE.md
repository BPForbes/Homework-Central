# CLAUDE.md

Guidance for Claude Code (or any agent) working in this repository.

## Project

Homework Central is a self-hosted, school-focused chat/communication app: subject-based
chat rooms, staff rooms, an inbox for mentions/replies, role-based permissions, a captcha
gate for account verification, and an admin "Server Maintenance" panel.

- `backend/` — .NET API (`HomeworkCentral.Api`), EF Core migrations, SignalR chat hubs.
- `frontend/` — React + TypeScript + Vite SPA, plain CSS (no Tailwind/component library).
- `scripts/` — local dev stack helpers (`run-dev.sh` / `run-dev.ps1`, etc.); see [`README.md`](./README.md)
  for application setup and [`SETUP.md`](./SETUP.md) for optional local contributor tooling.
- `docs/` — architecture and engineering standards; start at [`docs/README.md`](./docs/README.md).

## Comments, documentation, naming, and readability

Before adding or modifying source comments, XML documentation, Markdown files, naming,
or function structure, read and follow the Comment Documentation Guide at
[`docs/COMMENT_DOCUMENTATION_GUIDE.md`](./docs/COMMENT_DOCUMENTATION_GUIDE.md).

Inspect the implementation, related infrastructure, tests, configuration, and the
current branch diff against the integration base. Improve structure, names, and
explicit type declarations before adding comments.

Hard rules:

- Do not use C# `var`; use explicit local and iteration types.
- Prefer pattern matching over large `if` / `else if` chains for closed-set decisions.
- Prefer **fail-first** control flow: validate and return/throw early; keep the happy path
  unindented at the end of the function.
- Prefer speakable names. Abbreviations that cannot be spoken clearly as words or standard
  domain terms must be renamed (for example prefer `roomId` over `rid`, `eligibleUsers`
  over `eus`). Conventional short forms such as `ct` for `CancellationToken` and small
  loop indices remain acceptable.
- Prefer `Where` / `Select` / `ToDictionary` / `ToHashSet` / `map` / `filter` (and similar)
  for transforming or selecting members of collections when that is clearer than a
  hand-written `for` loop. Use an explicit loop when the body has multi-step side effects,
  early exits that do not map cleanly, or performance-critical inner kernels.
- Comments must explain project-specific intent, constraints, trust boundaries, state
  ownership, lifecycle behavior, or non-obvious implementation decisions.
- Comments must not be self-referential and must not mention an AI agent, prompt,
  conversation, authoring process, or temporary branch state.
- Prefer updating an authoritative existing Markdown document over creating a duplicate.
- Functions with high cognitive or cyclomatic complexity, excessive nesting, or a
  structural readability score below the accepted threshold must be split into cohesive,
  precisely named subfunctions unless an approved exception applies.
- Do **not** put time/space Big-O comments on individual functions. Record asymptotic
  notes in [`docs/runtime.md`](./docs/runtime.md) (or a short cross-link from the owning
  feature doc).

## Asymptotic analysis (time and space)

When generating or changing algorithms, hot paths, loops over collections, or
data-structure choices, perform asymptotic analysis before finishing:

1. State the **time** and **space** complexity in Big-O using meaningful variables
   (messages, users, mentions, rooms, vocabulary size, batch size, feature width, etc.).
2. Ask: **can this be improved** without breaking correctness, trust boundaries, or
   existing API contracts?
3. Prefer linear (or better) expected cost for lookups that are naturally key-based;
   flag nested scans that yield `O(n²)` where a dictionary/set/automaton would be `O(n)`.
4. If the change alters a documented hot path, update
   [`docs/runtime.md`](./docs/runtime.md#asymptotic-analysis-hot-paths).

Cognitive/cyclomatic readability limits (Comment Documentation Guide) are separate
from asymptotic cost. Both must be considered.

## Optional local tooling

When CodeGraph / Graphify are installed (see [`SETUP.md`](./SETUP.md)):

- Prefer `codegraph search <term>` over broad directory reads.
- Do not stage generated local directories (`.codegraph/`, `.code-review-graph/`,
  `claude-mem/`, `node_modules/`).
- Confirm destructive actions (deletes, force-pushes, hard resets) with the user.

## UI and styling work

**Before touching any color, animation, spacing, or component style in `frontend/`, read
[`design.md`](./design.md).** It is the source of truth for the design system: color
tokens, typography, motion/animation rules, and the rationale behind them. Every visual
value in `frontend/src/index.css` should trace back to a token defined there.

Do not hardcode a hex color, shadow, or transition timing in a component or a new CSS rule
— reuse or extend the tokens in `frontend/src/index.css`'s `:root` / `:root[data-theme='dark']`
blocks, and update `design.md` if you add a genuinely new token.

Key implementation entry points:
- `frontend/src/index.css` — all design tokens (light + dark) and every component style.
- `frontend/src/context/ThemeContext.tsx` — light/dark theme state, persisted to
  `localStorage`, toggled via `<ThemeToggle />`.
- `index.html` — inline anti-flash script that applies the persisted/preferred theme
  before first paint.

## Conventions

- No Tailwind, no CSS-in-JS — plain CSS with custom properties, styled by class name.
- FontAwesome (`@fortawesome/*`) for icons.
- Keep component structure changes and pure styling changes separate where possible;
  most visual work in this app can be done at the CSS-token level without touching JSX.
- CI rejects unparameterized EF raw SQL (`FromSqlRaw` / `ExecuteSqlRaw` / `SqlQueryRaw`)
  in `backend/HomeworkCentral.Api`.

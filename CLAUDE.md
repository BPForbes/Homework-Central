# CLAUDE.md

Guidance for Claude Code (or any agent) working in this repository.

## Project

Homework Central is a self-hosted, school-focused chat/communication app: subject-based
chat rooms, staff rooms, an inbox for mentions/replies, role-based permissions, a captcha
gate for account verification, and an admin "Server Maintenance" panel.

- `backend/` — .NET API (`HomeworkCentral.Api`), EF Core migrations, SignalR chat hubs.
- `frontend/` — React + TypeScript + Vite SPA, plain CSS (no Tailwind/component library).
- `scripts/` — local dev stack helpers (`run-dev.sh` / `run-dev.ps1`, etc.); see `SETUP.md`.

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

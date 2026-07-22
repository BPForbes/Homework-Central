# Homework Central — Local contributor tooling

This file documents **optional local indexing and session-memory tools**
(CodeGraph, Graphify, Claude-Mem) used by some contributors to search and
navigate the repository efficiently.

**Application local setup** — Docker, dev scripts, ports, and troubleshooting —
is in [`README.md`](README.md). Agent entry rules live in [`AGENTS.md`](AGENTS.md)
and [`CLAUDE.md`](CLAUDE.md). Architecture and security baselines live under
[`docs/`](docs/README.md).

**GitHub CodeQL** (C# + JavaScript/TypeScript security and quality scanning) runs
via `.github/workflows/codeql.yml`. Enablement and branch-protection notes are
under
[Analyzer and CI expectations](docs/COMMENT_DOCUMENTATION_GUIDE.md#analyzer-and-ci-expectations)
in the Comment Documentation Guide.

---

## Quick setup

Run once after cloning if these tools are desired:

```bash
# Phase 1: CodeGraph
npm install -g @colbymchenry/codegraph
codegraph install
codegraph init

# Phase 2: Graphify
pip install graphifyy code-review-graph
python -m graphify --install-claude
python -m graphify hook install
python -m code_review_graph build

# Phase 3: Claude-Mem (optional)
npm install -g bun
git clone https://github.com/thedotmack/claude-mem.git
cd claude-mem && bun install && bun run build && bun run cursor:setup
```

---

## What these tools do

| Tool | Purpose | Efficiency gain |
|---|---|---|
| **CodeGraph** | Indexes the codebase locally for precise searches | ~57% token savings |
| **Graphify** | Builds a knowledge graph of the project | ~71.5x efficiency |
| **Claude-Mem** | Persists session context across work sessions | Avoids reloading prior context |

---

## Usage

After install, these tools integrate with Cursor, Claude Code, and Codex and
optimize search and context in the background. Prefer `codegraph search <term>`
over broad directory reads when looking up symbols. Before large refactors, run
`python -m graphify hook install` to capture a pre-change knowledge graph.

---

## Local-only files

The following directories are generated locally and **must not** be committed
(see `.gitignore`):

- `.codegraph/` — CodeGraph index
- `.code-review-graph/` — Graphify knowledge graph
- `claude-mem/` — Session memory
- `node_modules/` — Dependencies

Each contributor installs these tools locally on their own machine.

---

## Security baselines

Homework Central XSS, SQL, and related web-security expectations are maintained
in [`docs/identity.md`](docs/identity.md). Treat that document as authoritative;
do not duplicate security policy here.

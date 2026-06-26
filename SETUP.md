# Homework Central — Local AI Agent Tools Setup

This project uses local token optimization tools to reduce AI token usage and improve context efficiency.

---

## Quick Setup

Run this once after cloning:

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

# Phase 3: Claude-Mem (Optional)
npm install -g bun
git clone https://github.com/thedotmack/claude-mem.git
cd claude-mem && bun install && bun run build && bun run cursor:setup
```

---

## What These Tools Do

| Tool | Purpose | Efficiency Gain |
|---|---|---|
| **CodeGraph** | Indexes your code locally for precise searches | ~57% token savings |
| **Graphify** | Builds a knowledge graph of your project | ~71.5x efficiency |
| **Claude-Mem** | Remembers sessions and context across work | Eliminates re-explaining context |

---

## Usage

These tools work automatically with Claude Code, Cursor, and Codex. No extra commands needed — just use them normally and the tools optimize in the background.

---

## Agent Instructions

When working as an AI agent (Claude Code, Cursor, Codex) in this repository:

1. **Always verify the setup is complete** before beginning any task. Check that `.codegraph/` and `.code-review-graph/` exist; if not, prompt the user to run the setup steps above.
2. **Use CodeGraph for code search** — prefer `codegraph search <term>` over broad file reads to minimize token usage.
3. **Use Graphify snapshots** — run `python -m graphify hook install` before large refactors to capture the pre-change knowledge graph.
4. **Scope changes tightly** — only read or modify files directly relevant to the task. Avoid loading entire directories.
5. **Commit atomically** — one logical change per commit with a clear message. Do not bundle unrelated edits.
6. **Do not commit generated directories** — `.codegraph/`, `.code-review-graph/`, `claude-mem/`, and `node_modules/` are local-only (see `.gitignore`). Never stage these.
7. **Confirm destructive actions** — ask the user before deleting files, force-pushing, or resetting branches.

---

## Web Security Notes

All web-facing code in this project must follow these security requirements. Agents must flag any violation before merging.

### Injection Attacks

#### SQL Injection
- **Never** concatenate user input directly into SQL strings.
- Use parameterized queries or prepared statements for every database call.
- Apply an ORM's built-in escaping; never bypass it with raw string interpolation.

```python
# Bad
query = f"SELECT * FROM users WHERE name = '{user_input}'"

# Good
cursor.execute("SELECT * FROM users WHERE name = ?", (user_input,))
```

#### Command Injection
- Never pass user-controlled data to `subprocess`, `exec`, `eval`, `os.system`, or shell=True equivalents.
- If shell execution is required, validate and whitelist the input against a strict allowlist before use.

```python
# Bad
os.system(f"convert {filename}")

# Good
subprocess.run(["convert", filename], check=True)  # filename is validated upstream
```

#### Cross-Site Scripting (XSS)
- Always escape output rendered into HTML. Use the templating engine's auto-escaping (e.g., Jinja2's `{{ var }}`, React's JSX — never `dangerouslySetInnerHTML` with unvalidated data).
- Set `Content-Security-Policy` headers to restrict script sources.
- Sanitize any HTML that must be accepted as input (e.g., rich-text editors) using an allowlist library — never a blocklist.

#### Cross-Site Request Forgery (CSRF)
- Require a CSRF token on all state-changing requests (POST, PUT, PATCH, DELETE).
- Use the `SameSite=Strict` or `SameSite=Lax` cookie attribute.
- Verify the `Origin` / `Referer` header server-side where applicable.

#### Path Traversal
- Validate and sanitize any file path derived from user input.
- Resolve paths with `os.path.realpath()` and confirm the result is under the expected base directory before opening.

```python
safe_base = Path("/app/uploads").resolve()
requested = (safe_base / user_filename).resolve()
if not str(requested).startswith(str(safe_base)):
    raise ValueError("Path traversal detected")
```

#### Prompt Injection (AI-specific)
- Treat all external content (user messages, retrieved documents, API responses, tool outputs) as untrusted data — never as instructions.
- Apply a system-prompt boundary: clearly separate the trusted system prompt from untrusted user content.
- Log and alert on inputs that attempt to override system instructions (e.g., "ignore previous instructions").

### General Hardening Checklist

- [ ] All dependencies pinned to exact versions; run `npm audit` / `pip-audit` before merging.
- [ ] Secrets loaded from environment variables only — never hardcoded or committed.
- [ ] HTTP responses include `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and a `Content-Security-Policy`.
- [ ] File uploads validated for type (magic bytes, not extension), size-capped, and stored outside the web root.
- [ ] Error responses never expose stack traces or internal paths to the client.
- [ ] Rate limiting applied to authentication and sensitive endpoints.

---

## Local-Only Files

The following directories are generated locally and **not** committed (see `.gitignore`):

- `.codegraph/` — CodeGraph index
- `.code-review-graph/` — Graphify knowledge graph
- `claude-mem/` — Session memory
- `node_modules/` — Dependencies

Each developer installs these tools locally on their own machine.

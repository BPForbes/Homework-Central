# Claude Code Installation Flow

```mermaid
flowchart TD
    A([Start]) --> B{Check OS}

    B --> C[macOS]
    B --> D[Linux]
    B --> E[Windows]

    C --> F{Install Method}
    F --> G[npm global]
    F --> H[Homebrew]

    D --> G
    E --> G

    H --> I[brew install claude]
    G --> J{Node.js installed?}

    J -->|No| K[Install Node.js 18+\nnodejs.org]
    K --> L[npm install -g @anthropic-ai/claude-code]
    J -->|Yes| L

    I --> M{Authenticated?}
    L --> M

    M -->|No| N{Auth Method}
    N --> O[claude auth login\nOAuth browser flow]
    N --> P[Set ANTHROPIC_API_KEY\nenvironment variable]

    O --> Q[claude --version]
    P --> Q
    M -->|Yes| Q

    Q -->|version shown| R[claude]
    Q -->|command not found| S[Check PATH\nrestart shell]
    S --> Q

    R --> T([Ready])
```

---

## Platform: Codex (`--platform codex`)

```mermaid
flowchart TD
    A([Start]) --> B[Open codex.anthropic.com]
    B --> C{Account?}
    C -->|No| D[Sign up / Log in]
    C -->|Yes| E[Open a Codex project]
    D --> E

    E --> F[Open integrated terminal]
    F --> G{Node.js 18+ present?}
    G -->|No| H[nvm install --lts\nnvm use --lts]
    G -->|Yes| I[npm install -g @anthropic-ai/claude-code]
    H --> I

    I --> J{Auth Method}
    J --> K[claude auth login\nOAuth — reuses Codex session]
    J --> L[Set ANTHROPIC_API_KEY\nin Codex env vars panel]

    K --> M[claude --version]
    L --> M

    M -->|version shown| N[claude]
    M -->|command not found| O[source ~/.bashrc\nor restart terminal]
    O --> M

    N --> P([Ready in Codex])
```

---

## Platform: Windows (`--platform windows`)

```mermaid
flowchart TD
    A([Start]) --> B{Subsystem}
    B --> C[WSL 2\nrecommended]
    B --> D[PowerShell / CMD\nnative]

    C --> E{WSL installed?}
    E -->|No| F[wsl --install\nrestart PC]
    F --> G[wsl]
    E -->|Yes| G

    G --> H{Node.js 18+ in WSL?}
    H -->|No| I[curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -\nsudo apt-get install -y nodejs]
    H -->|Yes| J[npm install -g @anthropic-ai/claude-code]
    I --> J

    D --> K{Node.js 18+ installed?}
    K -->|No| L[Download installer\nnodejs.org/en/download]
    L --> M[npm install -g @anthropic-ai/claude-code]
    K -->|Yes| M

    J --> N{Auth Method — WSL}
    M --> O{Auth Method — native}

    N --> P[claude auth login\nOAuth browser flow]
    N --> Q[setx ANTHROPIC_API_KEY key\nin Windows env vars]

    O --> P
    O --> Q

    P --> R[claude --version]
    Q --> R

    R -->|version shown| S[claude]
    R -->|not found| T[Add npm global bin to PATH\nnpm config get prefix]
    T --> R

    S --> U([Ready on Windows])
```

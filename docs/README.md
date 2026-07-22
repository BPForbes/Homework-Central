# Project documentation

Homework Central documentation explains project-specific architecture, trust
boundaries, operations, deployment, design, and engineering standards.
Application setup remains in [`README.md`](../README.md). Optional local
contributor tooling install steps remain in [`SETUP.md`](../SETUP.md).

Documentation is **feature-level**: one Markdown file per module, not one file
per class, endpoint, TSX panel, or complexity finding. See
[`COMMENT_DOCUMENTATION_GUIDE.md`](./COMMENT_DOCUMENTATION_GUIDE.md#feature-level-documents).

## Engineering standards

- [Comment Documentation Guide](./COMMENT_DOCUMENTATION_GUIDE.md) (`docs/COMMENT_DOCUMENTATION_GUIDE.md`)

## Feature-level architecture

| Module | Document | Owns |
|---|---|---|
| Identity | [identity.md](./identity.md) | Auth, sessions, captcha, account classes, tenancy |
| Chat | [chat.md](./chat.md) | Rooms, messages, SignalR, uploads, ClamAV, downloads |
| Tickets | [tickets.md](./tickets.md) | Portals, preface checks, votes, neural scoring, NeuralNet admin |

Local Docker Compose profiles, WSL caps, and ClamAV resource notes live in
[`README.md`](../README.md). Assessment queue capacity is noted in
[`tickets.md`](./tickets.md).

## Design and deployment

- [UI design system](../design.md)
- [Kubernetes workloads](../deploy/kubernetes/README.md)

## Authoritative sources

| Topic | Canonical document |
|---|---|
| Comment Documentation Guide (comments, naming, readability, XML/Markdown rules) | [`COMMENT_DOCUMENTATION_GUIDE.md`](./COMMENT_DOCUMENTATION_GUIDE.md) |
| Authentication, sessions, account classes, tenant visibility | [`identity.md`](./identity.md) |
| Chat rooms, messages, uploads, scanning, downloads | [`chat.md`](./chat.md) |
| Ticket portals, Trial Tutor, votes, AI scoring | [`tickets.md`](./tickets.md) |
| Docker Compose profiles, WSL caps, ClamAV resource notes | [`README.md`](../README.md) |
| Design tokens and motion | [`design.md`](../design.md) |
| Kubernetes deployment | [`deploy/kubernetes/README.md`](../deploy/kubernetes/README.md) |

When documents overlap, update the canonical module file and link from secondary
notes. Do not invent parallel standards or per-item Markdown files.

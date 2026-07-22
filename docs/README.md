# Project documentation

Homework Central documentation explains project-specific architecture, trust
boundaries, operations, and engineering standards. Application setup remains in
[`README.md`](../README.md). AI agent tooling setup remains in
[`SETUP.md`](../SETUP.md).

## Engineering standards

- [Comment, documentation, readability, and naming standard](./COMMENT_STANDARD.md)

## Architecture

- [Authentication and sessions](./auth-and-sessions.md)
- [Tenancy and account-class isolation](./tenancy-isolation.md)
- [Chat room access](./chat-room-access.md)
- [Tickets, media, and assessment](./tickets-assessment.md)
- [Ticket AI confidence scoring](./ticket-ai-scoring.md)
- [Uploads and malware scanning](./uploads-and-scanning.md)

## Infrastructure and operations

- [System efficiency and service choices](./system-efficiency.md)
- [Windows Docker resources](./windows-docker-resources.md)
- [Kubernetes workloads](../deploy/kubernetes/README.md)

## Design

- [UI design system](../design.md)

## Authoritative sources

| Topic | Canonical document |
|---|---|
| Comments, naming, readability, XML/Markdown rules | [`COMMENT_STANDARD.md`](./COMMENT_STANDARD.md) |
| Account classes and resource visibility | [`tenancy-isolation.md`](./tenancy-isolation.md) |
| JWT, captcha, refresh cookies, developer login | [`auth-and-sessions.md`](./auth-and-sessions.md) |
| Chat categories, rooms, and access bits | [`chat-room-access.md`](./chat-room-access.md) |
| Ticket portals, Trial Tutor, votes | [`tickets-assessment.md`](./tickets-assessment.md) |
| Neural monitors, Ollama blend, promotion | [`ticket-ai-scoring.md`](./ticket-ai-scoring.md) |
| Attachment inspect/scan/download | [`uploads-and-scanning.md`](./uploads-and-scanning.md) |
| Design tokens and motion | [`design.md`](../design.md) |

When documents overlap, update the canonical file and link from secondary notes.
Do not invent parallel standards.

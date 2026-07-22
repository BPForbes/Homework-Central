# Comment, documentation, readability, and naming standard

## Purpose

This standard defines how Homework Central code and documentation explain project
behavior, architecture, trust boundaries, operations, and maintenance decisions.

It applies to:

- C#/.NET backend code, XML documentation, EF Core migrations, and tests.
- TypeScript/React frontend code, HTML, CSS, and tests.
- SQL, Docker, deployment scripts, CI configuration, and local tooling.
- Markdown under the repository root and `docs/`.
- AI-generated or AI-assisted contributions.

The goal is durable clarity: future maintainers should understand Homework
Central's intent, invariants, and operational risks without reconstructing them
from branch diffs, issue threads, or commit history.

## Intended audience

Write for developers who already know C#/.NET, TypeScript, React, HTML, CSS,
SQL, Docker, and testing basics, but do not yet know Homework Central's:

- account classes and tenant isolation model;
- role, feature, and subject-expertise permission bits;
- chat, ticket, upload, captcha, and notification flows;
- AI scoring and neural promotion boundaries;
- deployment, local development, and database conventions;
- security assumptions and trust boundaries.

Do not teach basic programming syntax. Explain the project-specific reason,
constraint, or risk that is not obvious from the code alone.

## Core principles

| Principle | Standard |
|---|---|
| Explain the project | Document Homework Central behavior, decisions, and invariants; skip language tutorials. |
| Prefer readable code | Rename, simplify, or decompose before adding comments. |
| Prefer naming over comments | A precise name is stronger than a sentence explaining an imprecise name. |
| Document why and constraints | Comments should explain policy, risk, invariants, or non-obvious tradeoffs. |
| Keep comments current | A stale comment is a defect. Update comments with the code they describe. |
| Avoid process history | Do not mention AI assistance, prompts, branches, PRs, "I added", or "temporary for main" in durable docs or comments. |
| Avoid self-reference | Do not write comments such as "this method", "here", "here we", or "we use". State the rule directly. |
| Prefer existing docs | Update the canonical Markdown file instead of creating overlapping documentation. |
| Treat diffs as discovery | Branch comparisons can reveal missing docs, but branch-specific observations are not durable docs. |

## Required review process

Before approving or merging changes, reviewers must check:

1. **Names first** — confusing names are fixed before comments are added.
2. **Explicit C# locals** — C# local variables use explicit types; `var` is not allowed.
3. **Comment value** — comments explain project intent, invariants, trust boundaries,
   or operational behavior.
4. **No process language** — comments and docs do not reference AI, prompts, branch
   state, personal authorship, or implementation chronology.
5. **Complexity thresholds** — functions exceeding warning bands are reviewed for
   simplification; functions exceeding hard limits are refactored.
6. **Canonical docs** — user-facing or operator-facing knowledge is added to the
   most relevant existing Markdown file.
7. **Cross-references** — source comments point to stable docs only when the code
   cannot carry the full rationale safely.
8. **Tests as documentation** — behavior encoded in comments or Markdown has
   corresponding tests when it is executable behavior.

## Documentation decision guide

Use the smallest durable documentation surface that captures the information.

| Need | Preferred location | Examples |
|---|---|---|
| Clarify a non-obvious line or block | Inline comment near the code | Tenant filter intentionally omits tenant ID for shared chat traffic. |
| Explain public API contract | XML doc or exported TypeScript type doc | `IChatRoomAccessService.CanAccessRoom` authorization contract. |
| Explain architecture or trust boundary | Existing Markdown in `docs/` | `docs/tenancy-isolation.md`, `docs/chat-room-access.md`. |
| Record a decision and alternatives | ADR | Choice to keep built-in chat rooms catalog-driven. |
| Describe operations or incident response | Runbook | Restoring Postgres, rotating captcha secrets, handling failed migrations. |
| Explain local setup | `README.md`, `SETUP.md`, or script comments | Required tools, ports, reset commands. |
| Explain UI design tokens | `design.md` and `frontend/src/index.css` tokens | Theme, spacing, animation, and component visual rules. |
| Preserve security expectations | Security-focused doc plus tests | XSS baseline, SQL injection baseline, upload validation. |

When several files could own the information, choose the document closest to the
runtime boundary:

- account class and tenant rules -> `docs/tenancy-isolation.md`;
- chat room visibility -> `docs/chat-room-access.md`;
- ticket assessment and AI scoring -> `docs/tickets-assessment.md` or
  `docs/ticket-ai-scoring.md`;
- environment setup -> `README.md`, `SETUP.md`, or `docs/windows-docker-resources.md`;
- design tokens and visual language -> `design.md`.

## Naming before commenting

If a comment only explains what a poorly named symbol means, rename the symbol.

| Weak | Better | Reason |
|---|---|---|
| `data` | `ticketAssessmentAnswers` | Identifies domain and shape. |
| `flag` | `hasGroupMessagesFeature` | Explains permission bit purpose. |
| `items` | `accessibleChatRooms` | Identifies filtered collection. |
| `score` | `evidenceConfidence` | Distinguishes AI evidence from ticket priority. |
| `id` | `tenantId` | Identifies security boundary. |

Comments are acceptable after naming is strong when they explain why the name
exists or what invariant it protects.

## Variable naming

Variable names must identify role, domain, and unit when relevant.

| Rule | Good | Avoid |
|---|---|---|
| Include domain | `sandboxTenantName` | `name` |
| Include permission meaning | `requiredRoleBit` | `bit` |
| Include unit | `captchaTtlSeconds` | `ttl` |
| Include source when comparing | `jwtAccountClass`, `resourceAccountClass` | `left`, `right` |
| Include lifecycle state | `pendingUploadScan` | `scan` |
| Avoid abbreviations except common project terms | `moderationConceptSlug` | `mcs` |

Single-letter variables are allowed only for conventional math in a small local
scope, such as neural network formulas in tests or training helpers. Prefer
domain names even there when the value crosses more than a few lines.

## Loop counters and indexes

Loop counters are acceptable when they are the clearest expression of index-based
logic, especially in arrays, dense feature vectors, migrations, or deterministic
training loops.

| Context | Preferred name |
|---|---|
| Generic short loop over a local array | `index` |
| Nested loops | `rowIndex`, `columnIndex` |
| Feature vectors | `featureIndex` |
| Chat message windows | `messageIndex` |
| Migration batches | `batchIndex` |
| Neural training epochs | `epochIndex` |

Avoid `i`, `j`, and `k` unless the loop body is tiny, has no domain state, and is
immediately recognizable as numeric iteration.

## Function naming

Functions must name the observable behavior and domain boundary.

| Function kind | Pattern | Example |
|---|---|---|
| Command | Verb + object | `CreateTicketAsync` |
| Query | `Get`, `Find`, `List`, `Can`, `Has` | `GetAccessibleChatRoomsAsync` |
| Policy check | `Can` / `Should` + decision | `CanViewScopedResource` |
| Conversion | `To` + target | `ToChatRoomDto` |
| Validation | `Validate` + subject | `ValidateUploadMetadata` |
| Event handling | `Handle` + event | `HandleTicketMessageCreatedAsync` |

Avoid function names that hide side effects:

- `GetOrCreateSandboxTenantAsync` is acceptable because creation is named.
- `GetSandboxTenantAsync` must not create a database.
- `CalculateTicketScoreAsync` must not persist score changes.
- `PromoteNeuralMonitorAsync` must not silently enable live scoring unless the
  name or return type exposes that decision.

## Boolean naming

Boolean names must read naturally in conditionals and reveal polarity.

| Good | Avoid |
|---|---|
| `isVerifiedAccount` | `verified` |
| `hasSubjectExpertise` | `subject` |
| `canAccessStaffRoom` | `access` |
| `shouldEscalateTicket` | `escalate` |
| `requiresCaptcha` | `captcha` |
| `isDeveloperTraffic` | `dev` |

Avoid negative booleans when possible. `isBlocked` is clearer than
`isNotAllowed` when the domain state is a block. Do not combine negative names
with negated conditions.

## Collection naming

Collection names must be plural or otherwise indicate multiplicity.

| Good | Avoid |
|---|---|
| `chatMessages` | `chatMessageList` |
| `accessibleRooms` | `rooms` when unfiltered and filtered rooms coexist |
| `pendingTicketReviews` | `reviews` when completed reviews are nearby |
| `tenantDatabaseNames` | `names` |
| `uploadScanResultsByBlobName` | `results` |

Dictionaries and maps should name both key and value when that matters:

- `chatRoomsByKey`
- `subjectExpertiseByCategory`
- `ticketWatchesByUserId`
- `sandboxTenantsByDatabaseName`

## Names must match behavior

Names are contracts. A symbol name must stay true after implementation changes.

Required corrections:

- A function named `Validate...` must not mutate persistent state.
- A function named `List...` must not apply hidden security-sensitive filtering
  unless the filter is named or documented in the contract.
- A property named `TenantId` must not contain a database name.
- A property named `Confidence` must state whether it is model confidence,
  evidence confidence, reviewer confidence, or blended confidence.
- A class named `Shared...` must not enforce per-tenant isolation.

If behavior must remain surprising because of an external contract, document the
contract at the boundary and add tests.

## Explicit local types (no var)

C# code must use explicit local variable types. Do not use `var` for local
variables, `foreach`, `using`, deconstruction targets, or LINQ intermediate
values.

```csharp
ChatRoomDefinition room = ChatRoomCatalog.GetRequired(roomKey);
IReadOnlyList<ChatRoomDefinition> accessibleRooms = await chatRoomAccess.GetAccessibleRoomsAsync(user);
Dictionary<string, TicketUserWatch> watchesByUserId = await LoadWatchesByUserIdAsync(ticketId);
```

Avoid:

```csharp
var room = ChatRoomCatalog.GetRequired(roomKey);
var accessibleRooms = await chatRoomAccess.GetAccessibleRoomsAsync(user);
var watchesByUserId = await LoadWatchesByUserIdAsync(ticketId);
```

Rationale:

- Homework Central has many security-sensitive shapes with similar names:
  tenant IDs, tenant database names, account classes, role bits, feature bits,
  expertise bits, and neural score types.
- Explicit types make review safer and reduce ambiguity in diffs.
- Type names document domain boundaries without comments.

The same expectation applies to code samples in Markdown unless the sample is
copied from generated framework output that cannot reasonably be edited.

## Target-typed expressions and anonymous types

Target-typed C# expressions are allowed only when the target type is visible on
the same line or immediately adjacent.

Allowed:

```csharp
ChatRoomDefinition room = new("general:lobby", "General", ChatCategoryKind.General);
List<string> tenantDatabaseNames = [];
TicketScoreDelta delta = new()
{
    EvidenceConfidence = evidenceConfidence,
    Relevance = relevance
};
```

Avoid target typing when the type is distant, overloaded, security-sensitive, or
easy to confuse:

```csharp
return new(accountClass, tenantDatabaseName);
```

Prefer:

```csharp
return new ResourceVisibilityScope(accountClass, tenantDatabaseName);
```

Anonymous types are allowed for local LINQ projections, JSON test payloads, and
structured logs when they do not cross a public boundary. Use named DTOs for API
contracts, SignalR payloads, persisted data, and reusable test fixtures.

## Explicit collection and numeric types

Use explicit collection interfaces to communicate mutability and ordering.

| Use | When |
|---|---|
| `IReadOnlyList<T>` | Ordered results that callers must not mutate. |
| `List<T>` | Local construction or intentional mutation. |
| `IReadOnlySet<T>` | Membership checks with no ordering contract. |
| `HashSet<T>` | Local mutable membership set. |
| `IReadOnlyDictionary<TKey, TValue>` | Lookup contract without mutation. |
| `Dictionary<TKey, TValue>` | Local mutable map. |

Numeric types must match domain meaning:

- `double` for neural scores, confidence values, and normalized values in `[0, 1]`.
- `decimal` for money-like values if such values are introduced.
- `TimeSpan` for durations in code; use unit-suffixed names at config boundaries.
- `long` for file sizes and byte counts.
- enum or flags types for role, feature, expertise, account class, and category
  state instead of raw integers.

Do not hide numeric unit conversions in comments. Encode the unit in the type or
name.

## Function readability and complexity

Readable functions have one clear purpose, a small number of inputs, shallow
control flow, and explicit side effects. Review functions as units of behavior,
not as line-count contests.

Reviewers should evaluate:

- Can the function purpose be stated in one sentence?
- Are all parameters necessary and cohesive?
- Are authorization, validation, persistence, network calls, and response mapping
  mixed together?
- Are there multiple levels of nested branching?
- Are boolean expressions readable without a truth table?
- Do names make comments unnecessary?
- Does the function expose or hide side effects?

## Structural readability score

Use the structural readability score as a review heuristic. It is not a
replacement for judgment, but it makes complexity discussions concrete.

Start at `100`, then subtract:

| Factor | Weight |
|---|---:|
| Cognitive complexity | `2.0 × cognitive complexity` |
| Cyclomatic complexity above 4 | `1.5 × (cyclomatic - 4)` |
| Physical lines above 30 | `0.5 × (lines - 30)` |
| Nesting depth above 2 | `6 × (nesting - 2)` |
| Parameters above 3 | `4 × (params - 3)` |
| Boolean terms above 3 | `3 × (boolean terms - 3)` |
| Side-effect categories above 1 | `6 × (side-effect categories - 1)` |
| C# `var` local usage | `5 × occurrences` |
| Weak or misleading names | `5-20` based on severity |

Conceptual formula:

```text
readability =
  100
  - cognitiveComplexity * 2.0
  - max(0, cyclomaticComplexity - 4) * 1.5
  - max(0, lineCount - 30) * 0.5
  - max(0, nestingDepth - 2) * 6
  - max(0, parameterCount - 3) * 4
  - max(0, booleanTermCount - 3) * 3
  - max(0, sideEffectCategoryCount - 1) * 6
  - varOccurrenceCount * 5
  - weakNamingPenalty
```

Side-effect categories include database writes, database reads, file/blob I/O,
network calls, external process calls, cache mutation, SignalR sends, logging
that affects operations, time/randomness, and global/static state mutation.

Thresholds:

| Score | Meaning | Action |
|---:|---|---|
| `85-100` | Clear | Normal review. |
| `75-84` | Acceptable | Consider small naming or extraction improvements. |
| `60-74` | Warning | Refactor unless there is a documented readability exception. |
| `<60` | Failing | Refactor before merge. |

## Implicit-type and weak-naming penalties

Implicit types and weak names are readability defects even when runtime behavior
is correct.

| Defect | Penalty guidance |
|---|---:|
| C# `var` local | `5` each; fix required. |
| Ambiguous domain name (`data`, `item`, `record`) | `5` each. |
| Misleading name hiding side effect | `10-20`; fix required. |
| Boolean with unclear polarity | `5-10`. |
| Collection name hiding filtering or grouping | `5-10`. |
| Unit omitted from primitive numeric value | `5-10`. |

Penalties are cumulative. A short function can fail readability if names conceal
tenant, account-class, authorization, or AI scoring semantics.

## Hard complexity limits

Functions that exceed any hard limit must be refactored before merge unless an
approved readability exception is documented near the function and in review.

| Metric | Warning band | Hard limit |
|---|---:|---:|
| Cognitive complexity | `11-15` | `>15` |
| Cyclomatic complexity | `8-10` | `>10` |
| Nesting depth | `3-4` | `>4` |
| Physical lines | `61-80` | `>80` |
| Parameters | `5-6` | `>6` |
| Boolean terms in one condition | `5-7` | `>7` |
| Side-effect categories | `3` | `>3` |
| Structural readability score | `60-74` | `<60` |

Hard limits apply to production code, test helpers, migrations, scripts, and
frontend code. Generated code is exempt when it is not edited by hand.

## Function decomposition

Decompose functions around stable responsibilities, not arbitrary line counts.

Preferred seams:

- authorization and visibility decisions;
- input validation and normalization;
- tenant/account-class scope construction;
- persistence queries;
- mutation commands;
- DTO mapping;
- neural feature extraction;
- scoring and blending;
- upload inspection and storage;
- SignalR group routing;
- React state derivation;
- rendering components;
- deployment preflight checks.

Avoid extraction that creates vague helper names such as `ProcessData`,
`HandleStuff`, or `DoChecks`. An extracted function should make the caller read
like a policy narrative:

```csharp
ResourceVisibilityScope scope = accessScopeAccessor.GetRequiredScope(user);
ChatRoomDefinition room = chatRoomCatalog.GetRequired(roomKey);

if (!chatRoomAccess.CanAccessRoom(scope, room))
{
    return Forbid();
}
```

## Readability exceptions

Exceptions are rare and must be explicit. Acceptable cases include:

- generated framework code that should not be manually edited;
- EF Core migrations where generated operations are clearer left intact;
- dense numeric initialization for neural monitor fixtures;
- table-driven tests with large but simple data sets;
- short interop code constrained by an external API.

An exception must include:

- the metric exceeded;
- why decomposition would reduce clarity or safety;
- the guardrail that keeps the code maintainable, such as tests, table format,
  or linked Markdown.

Do not use exceptions to keep mixed authorization, persistence, and response
mapping in one function.

## Documentation adequacy score

Use this score for Markdown, XML docs, and important inline comments.

Start at `100`, then subtract:

| Gap | Penalty |
|---|---:|
| Missing trust boundary or authorization rule | `25` |
| Missing operational impact or rollback guidance | `20` |
| Missing data ownership or tenancy detail | `20` |
| Missing failure mode for external dependency | `15` |
| Duplicates another doc instead of linking | `15` |
| Contains stale branch/process language | `15` |
| Uses vague names without project terms | `10` |
| Lacks tests or examples for executable behavior | `10` |

Thresholds:

| Score | Action |
|---:|---|
| `85-100` | Adequate. |
| `70-84` | Improve before merge when the documented area is security, operations, or architecture. |
| `<70` | Not adequate for durable project documentation. |

## What usually requires documentation

Document these when introduced or changed:

- authorization and permission decisions;
- account-class, tenant, sandbox, and developer persona boundaries;
- EF Core global query filters and data ownership rules;
- cross-tenant, shared-community, or staff-only data behavior;
- upload validation, storage, scanning, and cleanup;
- captcha, authentication, token, and session behavior;
- ticket intake, tracking templates, watches, and moderation workflows;
- AI scoring, neural promotion, reviewer fallback, sampling, and confidence math;
- SignalR group membership, message delivery, and typing presence behavior;
- cache invalidation and background processing;
- external services, Docker services, ports, secrets, and environment variables;
- migrations with data transformation or irreversible changes;
- production runbooks and incident response procedures;
- ADR-worthy architecture choices and rejected alternatives.

## What usually does not require documentation

Do not comment or document:

- syntax that is obvious to a developer in the language;
- restatements of names;
- comments explaining every property assignment in simple DTO mapping;
- historical notes about who wrote code or why a branch changed;
- temporary observations from a branch diff;
- TODOs without an owner-neutral trigger or condition;
- jokes, apologies, uncertainty markers, or chatty narration;
- comments that duplicate tests without adding domain context.

## Inline comments

Inline comments are for local context that cannot be expressed cleanly through
names, types, or decomposition.

Good inline comments:

- explain a security or trust-boundary invariant;
- explain a non-obvious formula or threshold;
- identify an external protocol constraint;
- point to a canonical Markdown document for extended rationale;
- warn about a failure mode next to the code that could trigger it.

Style rules:

- Write complete, direct sentences.
- State the invariant, not the code mechanics.
- Avoid self-reference such as "this method", "here", "here we", or "below".
- Avoid authoring-process language.
- Keep comments close to the code they constrain.
- Delete comments made obsolete by clearer names or simpler structure.

Example:

```csharp
// Shared chat traffic is isolated by account class only; per-tenant matching
// would split the community room model. See docs/chat-room-access.md.
query = query.Where(message => message.OwnerAccountClass == scope.AccountClass);
```

Avoid:

```csharp
// Here we filter messages.
query = query.Where(message => message.OwnerAccountClass == scope.AccountClass);
```

## XML documentation

XML documentation is required for:

- public interfaces and classes that define project contracts;
- authorization, tenant, account-class, and feature-bit abstractions;
- DTOs or options whose property names are not enough to prevent misuse;
- extension points used by custom portals, monitors, or deployment hooks;
- non-obvious return values, units, ranges, or failure modes.

XML documentation should include:

- what project contract the member exposes;
- security or tenancy implications;
- units and valid ranges for primitive values;
- whether the member mutates state, reads state, sends messages, or calls
  external services;
- exceptions only when callers are expected to handle them.

Do not add XML docs that repeat the member name.

Good:

```csharp
/// <summary>
/// Determines whether the current account class may read a resource without
/// crossing real-account and developer-tenant traffic boundaries.
/// </summary>
bool CanQuery(ResourceVisibilityScope scope, IScopedResource resource);
```

Avoid:

```csharp
/// <summary>
/// Checks whether this method can query the resource.
/// </summary>
bool CanQuery(ResourceVisibilityScope scope, IScopedResource resource);
```

## Markdown documentation

Markdown files are the durable home for architecture, operations, setup, and
cross-cutting standards.

Markdown standards:

- Start with a clear H1.
- Explain project-specific context before implementation detail.
- Use tables for rules, thresholds, environment variables, ports, and ownership.
- Use bullets for procedures and short policy lists.
- Link to canonical related docs instead of repeating full sections.
- Include command examples that can be copied safely.
- Mark destructive commands clearly.
- Keep headings stable so source comments can link to them.
- Update existing docs when the topic already has an owner.

Avoid creating:

- duplicate setup guides;
- one-off branch notes;
- temporary comparison documents;
- docs that only paraphrase code without explaining project behavior.

## Architecture Decision Records

Use an ADR when a decision is consequential, costly to reverse, or likely to be
revisited.

ADR-worthy decisions include:

- tenant isolation model changes;
- replacing catalog-driven chat rooms with database-backed channels;
- enabling or disabling neural-only ticket scoring;
- changing upload storage, scan, or retention strategy;
- changing authentication, captcha, or token trust boundaries;
- introducing new external infrastructure;
- changing deployment topology or database ownership.

ADR content:

1. Title and status: proposed, accepted, superseded, or rejected.
2. Context: project problem and constraints.
3. Decision: chosen approach.
4. Consequences: benefits, costs, risks, and migration concerns.
5. Alternatives considered.
6. Links to implementation docs, tests, and runbooks.

ADRs should be short. Put operational procedures in runbooks and detailed API
behavior in the relevant feature doc.

## Runbooks

Runbooks describe repeatable operational procedures. They must be safe for an
operator who understands the stack but has not handled the specific Homework
Central incident before.

Runbooks should include:

- purpose and scope;
- symptoms and detection signals;
- prerequisites and required access;
- commands with working directory and environment assumptions;
- expected output or success criteria;
- rollback steps;
- data loss and downtime risks;
- escalation points;
- links to related architecture and security docs.

Use runbooks for:

- failed migrations;
- database restore or reset;
- captcha service failures;
- upload storage cleanup;
- stuck ticket AI processing;
- deployment rollback;
- Docker resource exhaustion;
- secret rotation.

## Cross-referencing Markdown from source

Source comments may link to Markdown when:

- the rationale is too large for a local comment;
- the linked document is canonical and stable;
- the code enforces a rule described in the document;
- the comment states the local invariant before linking.

Use repository-relative paths:

```csharp
// Real and developer traffic must stay separated at query time.
// See docs/tenancy-isolation.md.
```

Do not link to branch comparisons, AI transcripts, ephemeral issue comments, or
personal notes as durable rationale. If an external issue or PR contains lasting
knowledge, move the knowledge into Markdown and optionally cite the issue as a
reference.

## Infrastructure documentation

Infrastructure documentation must capture enough detail to rebuild, operate, and
debug the environment.

Document:

- services, ports, health checks, and dependencies;
- required SDKs, Node versions, Docker expectations, and OS-specific notes;
- environment variables, defaults, and secret handling rules;
- database names, ownership, migrations, and reset behavior;
- local development scripts and destructive flags;
- CI jobs, analyzers, and required checks;
- deployment targets and rollback strategy;
- resource requirements and known bottlenecks.

Do not document secret values. Document names, purpose, generation method, and
rotation procedure.

## Security documentation

Security documentation is required for changes that affect:

- authentication or authorization;
- account verification and captcha;
- account-class or tenant isolation;
- user-generated content and XSS handling;
- SQL query construction;
- uploads, file names, content types, scanning, and retention;
- secrets, tokens, cookies, or headers;
- staff-only features and moderation workflows;
- external AI review or model fallback behavior.

Security docs must identify:

- trusted and untrusted inputs;
- enforcement location;
- bypass risks;
- audit or logging expectations;
- tests that prove the boundary;
- operational action if the boundary fails.

## Branch comparison guidance

Branch diffs are useful for discovery only.

Allowed uses:

- finding changed behavior that needs documentation;
- locating new files or renamed concepts;
- identifying tests that should be updated;
- spotting drift between code and Markdown.

Disallowed durable docs:

- "unlike main";
- "on this branch";
- "newly added";
- "AI changed";
- "from the prompt";
- "temporary until PR merges";
- "current diff shows".

Write the final doc as timeless project policy or architecture. If a behavior is
temporary, document the runtime condition, removal trigger, and owner-neutral
cleanup path.

## TODO, FIXME, HACK, and NOTE

Use these markers sparingly and consistently.

| Marker | Meaning | Required content |
|---|---|---|
| `TODO` | Planned follow-up with a clear trigger | Condition or issue reference; expected direction. |
| `FIXME` | Known defect | Failure mode, impact, and link to tracking issue when available. |
| `HACK` | Deliberate compromise | Constraint forcing the compromise and safe removal condition. |
| `NOTE` | Important invariant near code | Project-specific fact that prevents misuse. |

Rules:

- Do not include personal authorship.
- Do not include AI or prompt references.
- Do not use TODO for vague cleanup.
- Prefer a tracked issue for work that can outlive a small change.
- Remove the marker when the condition no longer exists.

Good:

```csharp
// TODO: Replace in-memory typing presence when SignalR uses a distributed
// backplane; single-server development does not require cross-node presence.
```

Avoid:

```csharp
// TODO: Clean this up later.
```

## React, CSS, database, and deployment comments

### React and TypeScript

- Prefer descriptive component, hook, and state names over comments.
- Comment state transitions only when the user flow or server contract is not
  obvious.
- Document security-sensitive rendering, especially sanitized content and any
  future `dangerouslySetInnerHTML`.
- Use TypeScript types for API payloads and component props; avoid `any` unless
  the boundary is truly dynamic and validated immediately.
- Keep comments out of JSX when a component extraction or prop rename would be
  clearer.

### CSS

- Follow `design.md` before changing colors, spacing, animation, or component
  style.
- Use design tokens from `frontend/src/index.css`.
- Comment CSS only for project-specific layout constraints, browser workarounds,
  or accessibility behavior.
- Do not hardcode new visual values in comments or rules without updating the
  token source of truth.

### Database and SQL

- EF Core migrations with data movement, irreversible operations, or security
  implications need comments or Markdown notes.
- Raw SQL must explain why LINQ is insufficient and how parameters are protected.
- Document tenancy and account-class implications for new tables and query
  filters.
- Name indexes and constraints by domain purpose when possible.

### Deployment and Docker

- Scripts should state prerequisites, destructive behavior, and expected
  environment variables.
- Docker comments should explain service relationships, health checks, or
  resource constraints.
- Deployment docs must identify rollback behavior and data migration risks.

## Tests as documentation

Tests document executable behavior. Prefer readable test names and arrange data
that mirrors Homework Central domains.

Test names should state:

- condition;
- action;
- expected project behavior.

Good:

```csharp
public async Task CanQuery_DeniesDeveloperTenantAccessToRealAccountResource()
```

Avoid:

```csharp
public async Task TestAccess()
```

When a Markdown rule describes executable behavior, add or update tests for:

- tenant and account-class isolation;
- chat room access;
- ticket scoring and reviewer fallback;
- upload validation;
- captcha and authentication flows;
- raw SQL safety;
- frontend rendering and permission gating.

Comments in tests should explain non-obvious fixtures or domain-specific
constants, not every assertion.

## Analyzer and CI expectations

CI and analyzers should enforce as much of this standard as practical.

Required expectations:

- C# `var` usage is rejected in hand-authored code.
- Formatting and lint checks run for backend and frontend code.
- Tests cover changed executable behavior.
- Security checks reject unsafe raw SQL in backend application code.
- Markdown links are kept valid when files move.
- Complexity warnings are reviewed before merge.
- Hard complexity limit failures block merge unless an approved exception is
  documented.

Analyzer limitations do not weaken the policy. Reviewers must enforce standards
that tools cannot fully detect, especially misleading names, missing trust-boundary
docs, and authoring-process language.

## AI contributor requirements

AI-assisted contributions must meet the same standard as human-authored changes.

Required behavior:

- Read nearby code and existing docs before editing.
- Update the canonical Markdown file instead of creating duplicate docs.
- Preserve user changes and unrelated working-tree changes.
- Use explicit C# local types; do not introduce `var`.
- Avoid comments or docs that mention AI, prompts, generated status, branch
  comparisons, or personal authorship.
- Prefer project terms over generic placeholders.
- Add tests when changing executable behavior.
- Keep changes scoped to the requested behavior and the affected docs.
- Summaries may mention tooling used, but durable repository content must not.

AI agents must not leave "generated by", "AI note", "prompt asked", or similar
text in source, tests, or Markdown.

## Examples

### Comments

Good:

```csharp
// Developer personas may inspect sandbox traffic only; production account data
// remains outside DevAdmin visibility.
ResourceVisibilityScope scope = accessScopeAccessor.GetRequiredScope(User);
```

Bad:

```csharp
// Here we check if this method should allow the user.
ResourceVisibilityScope scope = accessScopeAccessor.GetRequiredScope(User);
```

Good:

```csharp
// The neural monitor score is bounded so an unavailable reviewer cannot block
// ticket intake or create an unreviewed automatic decision.
double boundedConfidence = Math.Clamp(monitorConfidence, 0.0, 1.0);
```

Bad:

```csharp
// Clamp the value between 0 and 1.
double boundedConfidence = Math.Clamp(monitorConfidence, 0.0, 1.0);
```

### Naming

Good:

```csharp
bool canAccessSandboxTenant = tenantAccess.CanAccessSandboxTenant(user, sandboxTenantName);
IReadOnlyList<ChatRoomDefinition> accessibleChatRooms = await chatAccess.GetAccessibleRoomsAsync(user);
double neuralPromotionConfidence = promotionResult.Confidence;
```

Bad:

```csharp
bool access = tenantAccess.Check(user, name);
var rooms = await chatAccess.GetAccessibleRoomsAsync(user);
double score = promotionResult.Confidence;
```

### Explicit types

Good:

```csharp
TicketAssessment ticketAssessment = await ticketService.GetAssessmentAsync(ticketId);
IReadOnlyDictionary<string, UploadScanResult> scanResultsByBlobName =
    await uploadScanner.ScanPendingUploadsAsync(ticketAssessment.Uploads);
```

Bad:

```csharp
var ticketAssessment = await ticketService.GetAssessmentAsync(ticketId);
var scanResults = await uploadScanner.ScanPendingUploadsAsync(ticketAssessment.Uploads);
```

### Function boundaries

Good:

```csharp
ResourceVisibilityScope scope = accessScopeAccessor.GetRequiredScope(User);
TicketUserWatch watch = await ticketWatchRepository.GetRequiredWatchAsync(ticketId, watchedUserId);
TicketScoreDelta scoreDelta = neuralScoring.CalculateScoreDelta(watch, chatMessage);

await ticketScoreRepository.ApplyScoreDeltaAsync(watch.Id, scoreDelta);
```

Bad:

```csharp
await Process(ticketId, watchedUserId, chatMessage);
```

### Markdown

Good:

```markdown
## Upload scan failure behavior

Uploads remain unavailable until scanning succeeds. A failed scan records the
scanner error category and keeps the blob in quarantine storage for operator
review.
```

Bad:

```markdown
## New branch changes

This branch adds upload scanning because the prompt requested it.
```

## Documentation maintenance

Documentation maintenance is part of the change, not follow-up polish.

Update docs when:

- behavior changes;
- names change;
- trust boundaries change;
- setup or deployment steps change;
- environment variables change;
- migrations affect data ownership or rollback;
- tests encode a policy not documented elsewhere;
- comments become stale after refactoring.

Maintenance rules:

- Prefer editing the canonical doc over adding another file.
- Remove obsolete comments and docs in the same change that makes them obsolete.
- Keep examples aligned with current APIs and names.
- Keep Markdown links repository-relative.
- Avoid duplicating long explanations across files; link instead.
- Review docs during refactors, not only feature work.
- Treat stale docs as defects.

## Review checklist

Use this checklist before submitting or approving a change:

- [ ] Names describe Homework Central domain behavior accurately.
- [ ] C# locals use explicit types; no `var` appears in hand-authored code.
- [ ] Functions stay below hard complexity limits or have an approved exception.
- [ ] Warning-band complexity has been considered for decomposition.
- [ ] Comments explain project-specific why, constraints, or risks.
- [ ] Comments avoid self-reference and authoring-process language.
- [ ] XML docs exist for public contracts and security-sensitive abstractions.
- [ ] Markdown updates went to the canonical existing document.
- [ ] ADRs were added for consequential architecture decisions.
- [ ] Runbooks were added or updated for operational procedures.
- [ ] Security and tenancy implications are documented and tested.
- [ ] Source links to Markdown use stable repository-relative paths.
- [ ] TODO/FIXME/HACK/NOTE markers include a clear condition or issue reference.
- [ ] React, CSS, database, and deployment comments follow their local rules.
- [ ] Tests cover changed executable behavior and use descriptive domain names.
- [ ] AI-assisted content contains no AI, prompt, branch-diff, or authorship notes.

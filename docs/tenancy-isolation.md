# Tenancy and Account-Class Isolation

Homework Central separates **real (production) accounts** from **developer personas** and **DevAdmin** at the JWT and authorization layers. Shared features (chat, channels, notifications) must enforce these boundaries defensively, not only by deployment layout.

## Account classes

| Class | Typical source | `tenant_db` claim |
|-------|----------------|-------------------|
| `RealAccount` | Register / login on master | Usually absent (production tenants may add this later) |
| `DeveloperAccount` | Dev login impersonating a persona | Persona database name (e.g. `tenant_math`) |
| `DevAdmin` | Dev login without persona | Absent (master database) |

## Visibility rules

- **RealAccount** — may view `RealAccount` resources in the **same** `tenant_db` only.
- **DeveloperAccount** — may view `DeveloperAccount` resources in the **same** `tenant_db` only.
- **DevAdmin** — may view all developer-tenant resources across dev tenants; **never** real-account data.

These rules are implemented once in:

- `ResourceVisibilityScope` / `IAccessScopeAccessor.CanQuery`
- `ResourceVisibilityHandler` (ASP.NET policy `"ResourceVisibility"`)
- EF Core global query filters on entities implementing `IScopedResource`

Call sites:

```csharp
await authorizationService.AuthorizeAsync(User, resource, AuthorizationPolicyNames.ResourceVisibility);
```

## Security layers for chat

1. **Feature gate** — `PlatformFeatures.GroupMessages` (and related bits) via bitmask.
2. **Room access** — subject expertise bits and staff role bits ([chat-room-access.md](chat-room-access.md)).
3. **Traffic isolation** — `IShareableScopedResource` EF global query filter on `ChatMessage` (real vs. developer/test traffic by `OwnerAccountClass` only; not per-tenant).
4. **Content safety** — `IContentSanitizer` before persisting free text; React escapes JSX by default.

Future tenant-private resources **must** use `IScopedResource` + the `"ResourceVisibility"` policy. Shared community resources (chat messages, future channels) use `IShareableScopedResource` instead.

## XSS baseline

- **Backend:** `HtmlContentSanitizer` (`IContentSanitizer`) for persisted user HTML; `HtmlEncoder.Default.Encode` for non-JSON HTML embedding.
- **Frontend:** Prefer `{text}` in JSX. If rich text is ever required, sanitize with DOMPurify immediately before `dangerouslySetInnerHTML`.
- **CSP:** Response middleware in `Program.cs` sets `Content-Security-Policy`.

## SQL injection baseline

- Use EF Core LINQ for reads/writes (parameterized automatically).
- If raw SQL is unavoidable, use `FromSqlInterpolated` / `ExecuteSqlInterpolated` only.
- CI fails the build if `FromSqlRaw` / `ExecuteSqlRaw` appear in `backend/HomeworkCentral.Api`.

## References

- GitHub issue #10
- PR #9 (per-developer tenant isolation)

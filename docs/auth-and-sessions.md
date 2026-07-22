# Authentication and sessions

## Purpose

Homework Central authentication issues short-lived JWT access tokens for API and
SignalR authorization, plus HttpOnly refresh cookies for browser session
continuity. The same flow carries account-class and tenant-database identity so
real users, developer personas, and DevAdmin stay separated at downstream
authorization boundaries.

## Scope

This document covers current backend behavior for:

- registration and login through `api/auth`;
- refresh-token rotation and logout;
- JWT claim construction and validation;
- developer bypass login through `api/auth/dev`;
- registration captcha effects on default role assignment.

It does not cover password reset, email verification, frontend token storage, or
future production tenant selection flows.

## Terminology

| Term | Meaning |
|---|---|
| Access token | Signed JWT returned in `AuthResponse.AccessToken`; used as the Bearer token for authenticated API calls. |
| Refresh token | Random 64-byte secret returned only as the `refresh_token` HttpOnly cookie and stored in the database as a SHA-256 hash. |
| `account_class` | JWT claim named by `TenancyConstants.AccountClassClaimName`; one of `RealAccount`, `DeveloperAccount`, or `DevAdmin`. |
| `tenant_db` | JWT claim and refresh-scope cookie named by `TenancyConstants.TenantDbClaimName`; present for developer persona sessions. |
| Effective mask | The role, moderation, feature, subject, and status masks rebuilt from role assignments and embedded in auth responses and JWT claims. |
| Developer bypass | Development-only, loopback-only login surface guarded by `HC_DEV_BYPASS` and `ASPNETCORE_ENVIRONMENT=Development`. |

## System context

`AuthController` exposes the normal session endpoints:

| Endpoint | Current behavior |
|---|---|
| `POST /api/auth/register` | Creates a master-database user, assigns default roles based on captcha validation, returns an access token, and sets refresh cookies. |
| `POST /api/auth/login` | Verifies email and password against the master database, returns an access token, and sets refresh cookies. |
| `POST /api/auth/refresh` | Requires a valid refresh cookie and a valid Origin when an Origin header is present; rotates the refresh token and returns a new access token. |
| `POST /api/auth/logout` | Revokes the current refresh token when present, then deletes refresh-scope cookies. |
| `GET /api/auth/me` | Requires Bearer auth, resolves the current user from the token subject and optional `tenant_db` claim, and returns the current `UserDto`. |

`DevAuthController` exposes `api/auth/dev` only when
`DevBypass.IsEnabled` and `DevBypass.IsLocalhost` both pass. Otherwise the
endpoints return `404`, hiding the bypass surface from non-development callers.

## Architecture

### JWT access tokens

`JwtService.GenerateAccessToken` signs tokens with HMAC-SHA256 using
`Jwt:Secret`. Tokens include:

- `sub`: `User.UserId`;
- `email`: `User.Email`;
- `username`: `User.Username`;
- `perm`: effective moderation mask;
- `role_mask`: effective role mask;
- `feature_mask`: effective feature mask;
- `account_class`: resolved account class;
- `jti`: per-token identifier;
- one `ClaimTypes.Role` claim per role name;
- `tenant_db` when the session belongs to a developer persona.

`Program.cs` configures JWT Bearer validation for issuer, audience, signing key,
lifetime, and zero clock skew. SignalR hub calls may pass the access token as the
`access_token` query parameter for paths under `/hubs`.

`AuthResponse.ExpiresIn` is currently `900` seconds from `AuthService`, while
the JWT service reads `Jwt:AccessTokenMinutes` when calculating the token
expiry. Keep those values aligned when changing token lifetime configuration.

### Refresh-token sessions

`AuthService.BuildAuthResponseAsync` creates a refresh token for every
successful register, login, dev login, and refresh. The raw token is written to
the `refresh_token` cookie. Only its SHA-256 hash is persisted in the
`RefreshTokens` table for the database that owns the session.

Refresh tokens rotate on `POST /api/auth/refresh`:

1. The `tenant_db` cookie selects the tenant database for developer persona
   sessions; absence selects the master database.
2. The raw refresh token cookie is hashed.
3. A matching, unrevoked, unexpired row is atomically marked revoked.
4. The associated user and effective mask are loaded.
5. A new refresh token row, refresh cookie, and access token are issued.

The `tenant_db` cookie selects the refresh-token store only. Authorization uses
the signed `tenant_db` claim in the access token and the account-class scope
rules described in [docs/tenancy-isolation.md](tenancy-isolation.md).

### Refresh cookies

Refresh cookies use:

| Cookie | Purpose | Options |
|---|---|---|
| `refresh_token` | Raw refresh secret for `/api/auth/refresh` and `/api/auth/logout`. | `HttpOnly`, `SameSite=Strict`, `Secure` when the request is HTTPS, `Path=/api/auth`, expiry matching the token row. |
| `tenant_db` | Refresh-store selector for developer persona sessions. | Same options as `refresh_token`; deleted for master-database sessions. |

Refresh and logout also validate the `Origin` header when one is present.
Accepted origins must match `Cors:AllowedOrigin`. Missing Origin is allowed for
same-origin and non-browser requests.

### Captcha registration flow

Registration calls `ICaptchaService.ValidateAsync` with `CaptchaAction.Register`
after fast duplicate email and username checks. Captcha validation consumes a
short-lived in-memory challenge and always requires an FCaptcha token. A
high-trust FCaptcha verdict can pass by itself; lower-trust verdicts must also
solve the selected in-house puzzle and pass the risk engine threshold.

Captcha outcome controls the initial role:

| Captcha result | Default role behavior |
|---|---|
| `true` | `AssignDefaultRolesAsync` promotes the new user to `VerifiedUser`. |
| `false` | The new user receives `Guest`. |

A failed captcha does not reject registration in the current implementation.

### Developer login

Developer login has two modes:

- Selecting a developer account without a persona signs in as the seeded
  `DevAdmin` master-database account.
- Selecting a persona verifies that the chosen persona belongs to the selected
  developer catalog entry, optionally provisions the persona, opens that
  persona's tenant database, and signs in the catalog persona user.

Account class is resolved in `AuthService`:

| Session source | `account_class` | `tenant_db` |
|---|---|---|
| Normal register or login | `RealAccount` | Absent |
| Developer persona login | `DeveloperAccount` | Persona database name |
| DevAdmin login | `DevAdmin` | Absent |

## Data and control flow

### Register

1. Normalize email to lowercase and trim username.
2. Reject duplicate email or username before captcha and password hashing.
3. Validate captcha for the register action.
4. Create the user with a BCrypt password hash in the master database.
5. Assign `VerifiedUser` or `Guest` based on captcha outcome.
6. Rebuild the effective mask and issue access and refresh tokens.

### Login

1. Normalize email to lowercase.
2. Load the user, roles, and effective mask from the master database.
3. Verify the password with BCrypt.
4. Rebuild missing masks when needed and issue access and refresh tokens.

### Refresh

1. Read `refresh_token` and optional `tenant_db` cookies.
2. Reject missing, expired, revoked, or unknown refresh tokens.
3. Revoke the old token before issuing the replacement.
4. Return a new access token and set replacement cookies.

### Current user

`GET /api/auth/me` reads the subject from `ClaimTypes.NameIdentifier` or `sub`,
reads `tenant_db` from the signed access token, then resolves the current user in
the corresponding database.

## State ownership

| State | Owner |
|---|---|
| Real users and normal refresh-token rows | Master `AppDbContext`. |
| Developer persona users and persona refresh-token rows | Persona tenant `AppDbContext`. |
| Password hashes | `User.PasswordHash`, created with BCrypt. |
| Captcha challenge records | In-process `IMemoryCache`; challenges are short-lived and single-use. |
| FCaptcha token assessment cache | In-process `IMemoryCache`; cached verification can be consumed during validation. |
| Raw refresh token | Browser cookie only. |
| Refresh-token hash | Database row only. |

## Trust boundaries

- Email, username, password, captcha answers, and FCaptcha tokens are untrusted
  request inputs.
- `Jwt:Secret` signs both access tokens and attachment download tokens; it must
  remain secret and must be long enough for HMAC-SHA256 use.
- The raw refresh token is a bearer secret protected by `HttpOnly` and
  `SameSite=Strict`. The database stores only the token hash.
- The `tenant_db` refresh cookie is not an authorization proof. It can only
  direct refresh-token lookup to a database where the matching token hash must
  already exist.
- Dev bypass endpoints require development environment, explicit bypass flag,
  and loopback caller checks.
- `account_class` and `tenant_db` claims feed resource visibility rules. See
  [docs/tenancy-isolation.md](tenancy-isolation.md) for the canonical isolation
  model.

## Configuration

| Key | Purpose | Current default or behavior |
|---|---|---|
| `Jwt:Secret` | HMAC key for JWTs and attachment access tokens. | Required through configuration, environment variable, or user secrets. |
| `Jwt:Issuer` | JWT issuer. | `HomeworkCentral`. |
| `Jwt:Audience` | JWT audience. | `HomeworkCentralUsers`. |
| `Jwt:AccessTokenMinutes` | JWT expiry used by `JwtService`. | `15` in `appsettings.json`. |
| `Jwt:RefreshTokenDays` | Refresh-token expiry. | `7` in `appsettings.json`. |
| `Cors:AllowedOrigin` | Accepted Origin for refresh and logout. | `http://localhost:5173` in `appsettings.json`. |
| `FCaptcha:*` | Self-hosted FCaptcha URLs, site key, secret, and allow threshold. | Secret is required; development defaults are rejected outside Development. |
| `Risk:*` | Captcha risk thresholds and penalties. | Register uses `Risk:RegisterBaseThreshold`. |
| `HC_DEV_BYPASS` | Enables dev auth bypass when set to `1` or `true` in Development. | Disabled unless explicitly set. |

`IpRateLimiting` in `appsettings.json` currently rate-limits login, register,
captcha challenge, role verification, and FCaptcha assessment endpoints.

## Failure handling

| Condition | Response behavior |
|---|---|
| Duplicate email or username during register | `409 Conflict` with a user-facing message. |
| Invalid login credentials | `401 Unauthorized`. |
| Missing refresh cookie | `401 Unauthorized`. |
| Invalid, expired, revoked, or wrong-database refresh token | `401 Unauthorized`. |
| Refresh or logout Origin mismatch | `403 Forbid`. |
| Missing or invalid access-token subject on `/me` | `401 Unauthorized`. |
| User missing during `/me` lookup | `404 NotFound`. |
| Dev bypass disabled or non-loopback caller | `404 NotFound`. |
| Invalid dev account or persona relationship | `401 Unauthorized` or `400 BadRequest`, depending on the failure. |
| Expired, replayed, or incorrect captcha challenge | Captcha validation returns `false`; registration continues as `Guest`. |

## Planned or adjacent work

No inspected code implements production tenant selection for normal login.
[docs/tenancy-isolation.md](tenancy-isolation.md) notes that `RealAccount`
sessions usually omit `tenant_db` today and may add production tenant scope
later. That future behavior belongs in the tenancy document and the auth code
that sets signed claims.

## Implementation references

- [backend/HomeworkCentral.Api/Services/AuthService.cs](../backend/HomeworkCentral.Api/Services/AuthService.cs)
- [backend/HomeworkCentral.Api/Services/JwtService.cs](../backend/HomeworkCentral.Api/Services/JwtService.cs)
- [backend/HomeworkCentral.Api/Services/IJwtService.cs](../backend/HomeworkCentral.Api/Services/IJwtService.cs)
- [backend/HomeworkCentral.Api/Controllers/AuthController.cs](../backend/HomeworkCentral.Api/Controllers/AuthController.cs)
- [backend/HomeworkCentral.Api/Controllers/DevAuthController.cs](../backend/HomeworkCentral.Api/Controllers/DevAuthController.cs)
- [backend/HomeworkCentral.Api/Captcha/CaptchaService.cs](../backend/HomeworkCentral.Api/Captcha/CaptchaService.cs)
- [backend/HomeworkCentral.Api/Data/AppDbContext.Authorization.cs](../backend/HomeworkCentral.Api/Data/AppDbContext.Authorization.cs)
- [backend/HomeworkCentral.Api/Tenancy/TenancyConstants.cs](../backend/HomeworkCentral.Api/Tenancy/TenancyConstants.cs)
- [backend/HomeworkCentral.Api/Authorization/AccountClass.cs](../backend/HomeworkCentral.Api/Authorization/AccountClass.cs)
- [backend/HomeworkCentral.Api/Program.cs](../backend/HomeworkCentral.Api/Program.cs)

## Related documentation

- [docs/tenancy-isolation.md](tenancy-isolation.md)
- [docs/chat-room-access.md](chat-room-access.md)
- [docs/tickets-assessment.md](tickets-assessment.md)
- [docs/COMMENT_STANDARD.md](COMMENT_STANDARD.md)

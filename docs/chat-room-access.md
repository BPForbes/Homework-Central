# Chat Room Access Model

Chat navigation uses **categories** (dropdown headers) and **rooms** (joinable channels). Subject **types** are categories only; **expertise bits** are the actual subject rooms.

## Navigation shape

```
Chat
├── General ▾
│   ├── General (public)
│   └── Get Roles (public — not a chat room, buttons to self-claim general subjects)
├── Mathematics ▾
│   ├── Calculus
│   ├── Algebra
│   └── …
├── Science ▾
│   ├── Biology
│   └── …
├── Computer Science ▾
│   ├── Python
│   └── …
└── Staff ▾
    ├── Tutors
    ├── Moderators
    ├── Admins
    └── …
```

## Access rules

| Rule | Implementation |
|------|----------------|
| **General (public)** | `general:lobby` — any authenticated user; `IsPrivate = false`, no key icon. |
| **Get Roles (public)** | `general:get-roles` — any authenticated user; frontend routes it to `/get-roles` (a button grid, not chat) instead of the messaging UI. Backed by `GET/POST /api/subjects/*`, not `/api/chat/*`. |
| Subject room | User has matching **expertise bit**; `IsPrivate = true`, key overlay on icon. |
| Staff room | User has matching **role bit** (e.g. Moderators needs `PlatformRoles.Moderator`); private with key + role icon (shield for mods). |
| Super viewers | `Owner` or `Administrator` → all subject and staff rooms. |
| Category visibility | Dropdown shown only when ≥1 child room is accessible. |
| Category kind | `General`, `Subject`, or `Staff` — drives nav grouping and `IsPrivateCategory`. |
| General subject only | `generalSubjectMask` bit **alone** does not open a subject category or room. |
| Feature gate | User must have `PlatformFeatures.GroupMessages` to send messages. |
| Tenant isolation | Messages implement `IScopedResource`; `"ResourceVisibility"` policy applies after room access. |

## Room blueprint

`ChatRoomBlueprint` constructs all rooms with explicit privacy:

- `GeneralLobby()` — public `ChatCategoryKind.General`
- `SubjectExpertise(...)` — private subject rooms (Mathematics, Science, …)
- `StaffRole(...)` — private staff rooms (Moderators, Tutors, …)

`ChatNavRoomDto` exposes `IsPrivate`, `CategoryKey`, and `CategoryKind` for the frontend icon layer.

## Examples

- Science + Biology bits → **Science** dropdown with **Biology** room only.
- Calculus bit only → **Mathematics** → **Calculus**.
- Tutor role → **Staff** → **Tutors**.
- Owner → all categories and rooms from `ChatRoomCatalog`.

## Backend API

- `GET /api/chat/nav` — returns `ChatNavDto` for the authenticated user.
- `ChatRoomCatalog` — canonical room list from `*Expertise` index classes and staff `PlatformRoles`.
- `IChatRoomAccessService` — `CanAccessRoom`, `GetAccessibleNav`, `CanAccessAllRooms`.

## Frontend

Use `hasSubjectExpertise`, `hasRole`, and `hasFeature` from `AuthContext` for menu gating. Fetch `/api/chat/nav` to build dropdowns.

For rich-text message rendering (future), sanitize with DOMPurify before any `dangerouslySetInnerHTML`.

## Future `ChatChannel` entity

Each channel should store:

- `ExpertiseCategory` + `ExpertiseBit` **or** `RequiredRoleBit`
- `OwnerAccountClass` + `TenantDatabaseName` (`IScopedResource`)
- Sanitized message content via `ISanitizableContent` / `IContentSanitizer`

Do not duplicate visibility logic in controllers — delegate to `IChatRoomAccessService` and `ResourceVisibilityHandler`.

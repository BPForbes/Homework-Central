# Chat Room Access Model

Chat navigation uses **categories** (dropdown headers) and **rooms** (joinable channels). Subject **types** are categories only; **expertise bits** are the actual subject rooms.

## Navigation shape

```
Chat
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
| Subject room | User has matching **expertise bit** in `subjectExpertiseMasks[category]` (e.g. `hasSubjectExpertise("Science", Biology)`). |
| Staff room | User has matching **role bit** in `roleMask` (e.g. `hasRole(PlatformRoles.Moderator)`). |
| Super viewers | `Owner` or `Administrator` → all subject and staff rooms. |
| Category visibility | Dropdown shown only when ≥1 child room is accessible. |
| General subject only | `generalSubjectMask` bit **alone** does not open a category or room. |
| Feature gate | User must have `PlatformFeatures.GroupMessages` (or relevant feature) to use chat. |
| Tenant isolation | Channels/messages implement `IScopedResource`; `"ResourceVisibility"` policy applies after room access. |

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

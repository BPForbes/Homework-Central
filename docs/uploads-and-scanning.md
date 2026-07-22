# Uploads and malware scanning

## Purpose

Homework Central chat attachments are inspected, feature-gated, scanned when
ClamAV is available, stored on disk, and referenced from chat messages through
database metadata. Download authorization is enforced either by normal Bearer
authentication and room access checks or by short-lived signed access-token URLs.

## Scope

This document covers current backend behavior for:

- chat attachment upload, delete, message-linking, and download;
- attachment type inspection and hazard classification;
- ClamAV and null malware scanner implementations;
- `ImageUploads` and `FileUploads` feature bits;
- developer bypass behavior for uploads;
- the caution gate for infected attachments;
- orphan attachment cleanup.

It does not cover avatars, profile media, external object storage, thumbnail
generation, or retention policies beyond the current orphan cleanup worker.

## Terminology

| Term | Meaning |
|---|---|
| Attachment | A `ChatAttachment` metadata row plus a file stored under `Uploads:RootPath`. |
| Message link | A `ChatMessageAttachment` row connecting an uploaded attachment to a chat message. |
| Orphan attachment | A `ChatAttachment` row with no message links. |
| Hazard | An attachment whose detected type is executable, archive-like, or script-like; hazards do not receive inline previews. |
| Scan status | `MalwareScanResult` stored as a string on `ChatAttachment.ScanStatus`. |
| Caution gate | Download response behavior that requires `riskAcknowledged=true` only when scan status is `Infected`. |
| Access-token URL | A signed URL with `accessToken` query string that permits anonymous download until its cache-backed token expires. |

## System context

`ChatController` owns the HTTP surface:

| Endpoint | Authorization | Current behavior |
|---|---|---|
| `POST /api/chat/attachments` | Bearer auth required | Uploads one `IFormFile`, validates feature bits and scope, scans, stores, and returns attachment metadata plus a signed download URL. |
| `GET /api/chat/attachments/{attachmentId}` | Bearer auth or valid `accessToken` query string | Streams the file, returns `404` when inaccessible, or returns `409` when an infected file needs acknowledgement. |
| `DELETE /api/chat/attachments/{attachmentId}` | Bearer auth required | Deletes an unattached file only when the caller uploaded it. |

`ChatMessageService.SendMessageAsync` links uploaded attachments to messages.
It only links attachments uploaded by the sender and rejects attachments whose
stored account class or tenant database does not match the message scope.

## Architecture

### Upload pipeline

`ChatAttachmentService.UploadAsync` performs the current upload flow:

1. Reject empty files and files larger than `Uploads:MaxBytes`.
2. Load the caller's effective feature mask.
3. Resolve the caller's account-class and tenant scope.
4. Inspect the file head with `AttachmentTypeInspector`.
5. Enforce upload feature bits unless the caller is a development persona or
   DevAdmin and dev bypass is enabled.
6. Scan the file stream with `IMalwareScanner`.
7. Create `Uploads:RootPath` when needed.
8. Save the file as `{attachmentId}_{safeOriginalFileName}`.
9. Persist `ChatAttachment` metadata, including owner scope, hazard status,
   inline preview kind, and scan status.
10. Return a `ChatAttachmentDto` with a signed download URL.

The controller also has `[RequestSizeLimit(12_000_000)]`. The configured
`Uploads:MaxBytes` default is `10 * 1024 * 1024`, so the service limit is the
effective default application limit.

### Type inspection and hazard classification

`AttachmentTypeInspector` reads up to 8192 bytes from the beginning of the
seekable upload stream and asks MimeDetective for type matches. The resolved
content type comes from the strongest detected MIME type when available. If
inspection cannot identify the type, the browser-provided content type is used
unless it is empty or `application/octet-stream`; otherwise the fallback is
`application/octet-stream`.

`HazardDefinitionRegistry` marks MimeDetective executable and archive MIME
types as hazards. `ShebangClassifier` also treats script-like shebang content as
hazardous. Hazard attachments are still stored and downloadable, but they do not
receive inline preview kinds.

Non-hazard inline preview kinds are:

- `image` for `image/*`;
- `video` for `video/*`;
- `audio` for `audio/*`;
- `pdf` for `application/pdf`;
- `text` for `text/*`.

### Feature-bit gates

Upload permission is based on the effective feature mask:

| Detected upload kind | Required feature bit for normal accounts |
|---|---|
| Image (`ContentType` starts with `image/`) | `PlatformFeatures.ImageUploads` or `PlatformFeatures.FileUploads`. |
| Non-image | `PlatformFeatures.FileUploads`. |

`VerifiedUser`, `Student`, `TrialTutor`, tutor roles, `BetaTester`, and
administrator-class roles receive image and file upload bits through current
role-mask construction. `Guest` does not.

Developer personas and DevAdmin bypass these feature-bit checks only when:

- the current access scope is `DeveloperAccount` or `DevAdmin`; and
- `DevBypass.IsEnabled` is true for the host process.

The bypass does not remove the requirement for an authenticated caller or a
valid access scope.

### Malware scanning

`Program.cs` currently registers `ClamAvMalwareScanner` for `IMalwareScanner`.
`NullMalwareScanner` is also present and always returns `NotScanned`, but it is
not the default application registration in the inspected startup code.

`ClamAvMalwareScanner` behavior:

| Condition | Stored scan status |
|---|---|
| `ClamAv:Enabled` is `false` | `NotScanned` |
| ClamAV returns virus detected | `Infected` |
| ClamAV returns protocol scan error | `NotScanned` |
| ClamAV returns clean or any other non-error result | `Clean` |
| ClamAV is unreachable, loading, times out, or throws | `NotScanned` unless the request cancellation token was canceled |

The scanner streams to `clamd` through `nClam`, uses `ClamAv:Host` and
`ClamAv:Port`, applies `ClamAv:TimeoutSeconds`, and sets an nClam
`MaxStreamSize` of `12_000_000`.

### Download authorization

Download has two authorization paths:

1. Bearer-authenticated callers can download their own uploads. Other
   authenticated callers can download an attachment when at least one linked
   message belongs to a room the caller can access.
2. Anonymous callers can download with a valid `accessToken` query string minted
   by `AttachmentAccessTokenService`.

Access-token URLs are bearer secrets. The token payload contains attachment ID,
minting user ID, and expiry. The HMAC signature uses `Jwt:Secret`, and token
presence is cached under `att:tok:{attachmentId}:{signature}` in the configured
distributed cache. A valid access-token URL bypasses the room-access check until
the token expires or the cache entry disappears.

`ChatMessageService` mints viewer-specific download URLs when returning message
DTOs that include attachments. Upload responses also include a signed download
URL for the uploader.

### Caution gate

`ChatAttachmentService.OpenReadAsync` only requires safety acknowledgement for
attachments whose stored scan status is `Infected`. When an infected attachment
is requested without `riskAcknowledged=true`, `ChatController` returns
`409 Conflict` with the scan status and no file stream.

`NotScanned` does not block download. Scanner downtime, disabled scanning, or
local development without ClamAV therefore fails open as `NotScanned` rather
than quarantining ordinary uploads.

### Deletion and orphan cleanup

The uploader may delete an attachment only while it has no message links. Linked
attachments are retained with their chat message metadata.

`OrphanAttachmentCleanupWorker` periodically calls
`OrphanAttachmentCleanupService.PurgeOrphansAsync`. The service deletes orphan
rows older than `Uploads:OrphanTtlHours` and removes the corresponding files
from `Uploads:RootPath`. Cleanup failures are logged as warnings and retried on
the next interval.

## Data and control flow

### Upload and send

1. The client uploads a file to `POST /api/chat/attachments`.
2. The server stores file metadata and returns an attachment ID.
3. The client includes that attachment ID in `SendChatMessageRequest.AttachmentIds`.
4. `ChatMessageService` links each distinct attachment ID to the new message
   only when ownership and account scope match.
5. Message DTOs include attachment metadata, scan status, hazard status, inline
   preview kind, and a download URL.

### Download

1. The controller resolves the caller from the Bearer token when present.
2. Anonymous callers must provide a valid signed access token.
3. The service loads attachment metadata and linked room IDs.
4. The service enforces ownership or room access unless the access-token URL was
   already validated.
5. Infected files require `riskAcknowledged=true`.
6. The file is streamed from `Uploads:RootPath`.

## State ownership

| State | Owner |
|---|---|
| File bytes | Local filesystem under `Uploads:RootPath`. |
| Attachment metadata | `ChatAttachment` rows in the application database. |
| Message links | `ChatMessageAttachment` rows. |
| Access-token URL validity | Distributed cache; Redis when configured, in-memory cache otherwise. |
| Malware signatures and scan engine | External ClamAV service when enabled and reachable. |
| Feature-bit authorization | Effective masks from role assignments. |

Database metadata is the application source of truth for attachment ownership,
scope, scan status, and message linkage. The filesystem is the byte store.

## Trust boundaries

- Uploaded bytes, filenames, and browser-provided content types are untrusted.
- `Path.GetFileName` strips path components before the original filename is
  stored or combined into the storage filename.
- MIME inspection reduces reliance on browser content type, but it is not a
  substitute for malware scanning or safe rendering.
- Access-token URLs are bearer credentials and can be used without a logged-in
  session until expiry.
- `Jwt:Secret` protects attachment URL signatures as well as JWT signatures.
- ClamAV failure is not treated as confirmed malware. Only an `Infected` result
  triggers the caution gate.
- Developer upload feature bypass is limited to development bypass sessions and
  must not be treated as production upload permission.
- Attachment scope is stored as `OwnerAccountClass` and `TenantDatabaseName`.
  Cross-scope linking is rejected before a message can reference an attachment.
  See [docs/tenancy-isolation.md](tenancy-isolation.md) for the account-class
  isolation model.

## Configuration

| Key | Purpose | Current default or behavior |
|---|---|---|
| `Uploads:RootPath` | Filesystem directory for attachment bytes. | `App_Data/uploads`; Docker sets `/app/App_Data/uploads`. |
| `Uploads:MaxBytes` | Service-level maximum upload size. | `10 * 1024 * 1024`. |
| `Uploads:OrphanTtlHours` | Age before unattached metadata rows and files are eligible for cleanup. | `24`. |
| `Uploads:CleanupIntervalMinutes` | Background cleanup interval. | `60`. |
| `ClamAv:Enabled` | Enables ClamAV scan attempts. | `true`. |
| `ClamAv:Host` | ClamAV host. | `localhost`. |
| `ClamAv:Port` | ClamAV port. | `3310`. |
| `ClamAv:TimeoutSeconds` | ClamAV per-scan timeout. | `120`. |
| `AttachmentAccess:TokenTtlMinutes` | Signed download URL TTL and cache expiry. | `60`. |
| `ConnectionStrings:Redis` | Distributed cache backing signed download URL validity. | Redis when non-empty; in-memory distributed cache otherwise. |
| `Jwt:Secret` | HMAC key for signed download URLs and JWTs. | Required. |
| `HC_DEV_BYPASS` | Enables development upload feature-bit bypass for dev scopes. | Disabled unless set to `1` or `true` in Development. |

## Failure handling

| Condition | Response or state |
|---|---|
| Missing file in upload request | `400 BadRequest`. |
| Empty or oversized file | `400 BadRequest` with the service validation message. |
| Missing upload feature bit for normal account | `400 BadRequest`. |
| Scanner disabled or unavailable | Upload succeeds with `ScanStatus=NotScanned`. |
| Confirmed infected scan | Upload succeeds with `ScanStatus=Infected`; download requires acknowledgement. |
| Anonymous download without access token | `401 Unauthorized`. |
| Invalid or expired access token | `401 Unauthorized`. |
| Authenticated caller lacks ownership or room access | `404 NotFound` from the download path. |
| Infected download without acknowledgement | `409 Conflict`. |
| File missing from disk | `404 NotFound`. |
| Delete by non-uploader | `400 BadRequest`. |
| Delete after message link exists | `400 BadRequest`. |
| Cleanup worker failure | Warning log; next interval retries cleanup. |

If a file is written successfully but metadata persistence fails afterward, the
current upload path does not remove the file immediately. The orphan cleanup
worker only sees metadata rows, so files without rows require operational
cleanup from `Uploads:RootPath`.

## Planned or adjacent work

No inspected code implements external object storage, quarantine storage,
thumbnail generation, or blocking behavior for `NotScanned`.
[docs/system-efficiency.md](system-efficiency.md) documents why the current C#
client streams to `clamd` directly and why replacing it with an npm wrapper
would not remove the ClamAV engine requirement.
[docs/windows-docker-resources.md](windows-docker-resources.md) documents the
opt-in Docker `antivirus` profile and its memory cost on an 8 GB Windows
development host.

## Implementation references

- [backend/HomeworkCentral.Api/Uploads/ChatAttachmentService.cs](../backend/HomeworkCentral.Api/Uploads/ChatAttachmentService.cs)
- [backend/HomeworkCentral.Api/Uploads/AttachmentTypeInspector.cs](../backend/HomeworkCentral.Api/Uploads/AttachmentTypeInspector.cs)
- [backend/HomeworkCentral.Api/Uploads/HazardDefinitionRegistry.cs](../backend/HomeworkCentral.Api/Uploads/HazardDefinitionRegistry.cs)
- [backend/HomeworkCentral.Api/Uploads/ShebangClassifier.cs](../backend/HomeworkCentral.Api/Uploads/ShebangClassifier.cs)
- [backend/HomeworkCentral.Api/Uploads/ClamAvMalwareScanner.cs](../backend/HomeworkCentral.Api/Uploads/ClamAvMalwareScanner.cs)
- [backend/HomeworkCentral.Api/Uploads/NullMalwareScanner.cs](../backend/HomeworkCentral.Api/Uploads/NullMalwareScanner.cs)
- [backend/HomeworkCentral.Api/Uploads/AttachmentAccessTokenService.cs](../backend/HomeworkCentral.Api/Uploads/AttachmentAccessTokenService.cs)
- [backend/HomeworkCentral.Api/Uploads/OrphanAttachmentCleanupService.cs](../backend/HomeworkCentral.Api/Uploads/OrphanAttachmentCleanupService.cs)
- [backend/HomeworkCentral.Api/Uploads/OrphanAttachmentCleanupWorker.cs](../backend/HomeworkCentral.Api/Uploads/OrphanAttachmentCleanupWorker.cs)
- [backend/HomeworkCentral.Api/Controllers/ChatController.cs](../backend/HomeworkCentral.Api/Controllers/ChatController.cs)
- [backend/HomeworkCentral.Api/Chat/ChatMessageService.cs](../backend/HomeworkCentral.Api/Chat/ChatMessageService.cs)
- [backend/HomeworkCentral.Api/Models/ChatAttachment.cs](../backend/HomeworkCentral.Api/Models/ChatAttachment.cs)
- [backend/HomeworkCentral.Api/Authorization/BitIndices.cs](../backend/HomeworkCentral.Api/Authorization/BitIndices.cs)
- [backend/HomeworkCentral.Api/Authorization/RoleMaskBuilder.cs](../backend/HomeworkCentral.Api/Authorization/RoleMaskBuilder.cs)
- [backend/HomeworkCentral.Api/Program.cs](../backend/HomeworkCentral.Api/Program.cs)

## Related documentation

- [docs/tenancy-isolation.md](tenancy-isolation.md)
- [docs/chat-room-access.md](chat-room-access.md)
- [docs/system-efficiency.md](system-efficiency.md)
- [docs/windows-docker-resources.md](windows-docker-resources.md)
- [docs/COMMENT_STANDARD.md](COMMENT_STANDARD.md)

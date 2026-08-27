# OTMR server synchronization contract

Status: **authoritative API v1 contract** shared by the Windows uploader and the ASP.NET Core DigitalOcean server.

The Windows and server applications use SQLite with the same OTMR domain semantics:

- Windows database: `C:\OTMR_RCM\OTMR_RCM.db`
- Server database: `/var/lib/otmr/OTMR_RCM.db`

SQLite files, WAL files, and SHM files are never copied between machines. Complete recording sessions are synchronized as deterministic, authenticated JSON packages over HTTPS. This document supersedes the PostgreSQL and staged-upload proposals in `OTMR_DATABASE_AND_SERVER_SYNC.md`.

## 1. Versions and endpoint

- HTTP contract: `apiVersion = 1`
- Windows SQLite schema: `PRAGMA user_version = 2`
- Server SQLite schema: `PRAGMA user_version = 1`
- Atomic upload endpoint: `POST /api/v1/otmr/recording-sessions`
- Content type: `application/json; charset=utf-8`
- Idempotency header: `Idempotency-Key: <sessionId in UUID D format>`
- Version header: `X-OTMR-Api-Version: 1`

API v1 sends one frozen complete session in one request. No background/automatic uploader is enabled yet. If production session sizes later exceed safe proxy/API limits, v2 may add content-addressed batches while retaining all v1 identities and the same canonical content model.

## 2. SQLite schema version 2

Schema version 2 preserves the version 1 recording tables and adds upload leasing and RCM capture-frame idempotency.

### `recording_sessions`

| Column | SQLite type/constraint | Authority |
|---|---|---|
| `session_id` | `TEXT PRIMARY KEY` | Stable Windows-generated UUID and package identity. |
| `started_utc` | `TEXT NOT NULL` | Captured UTC start time. |
| `finished_utc` | `TEXT NULL` | Captured UTC stop time; an upload requires a value. |
| `software_version` | `TEXT NOT NULL` | Capturing application version. |
| `com_port` | `TEXT NOT NULL` | Local COM port provenance. |
| `serial_settings` | `TEXT NOT NULL` | Serial configuration provenance. |
| `vehicle_identifier`, `vehicle_type` | `TEXT NULL` | Recorder/vehicle metadata. |
| `ccf_filename`, `ccf_sha256` | `TEXT NULL` | Selected CCF provenance. |
| `rcm_profile_filename`, `rcm_profile_sha256` | `TEXT NULL` | RCM profile provenance. |
| `rcm_profile_json` | `TEXT NULL` | Immutable profile snapshot taken for the recording. |
| `notes` | `TEXT NULL` | Operator/recovery notes. |
| `sync_state` | `TEXT NOT NULL` | Local delivery lifecycle. |
| `remote_session_id` | `TEXT NULL` | Server acknowledgement alias; local delivery metadata. |
| `created_utc` | `TEXT NOT NULL` | Local row creation time. |

### `raw_serial_entries`

`id INTEGER PRIMARY KEY AUTOINCREMENT`, `session_id TEXT NOT NULL`, `sequence INTEGER NOT NULL`, `timestamp_utc TEXT NOT NULL`, `direction TEXT NOT NULL`, `data BLOB NOT NULL`, `interpretation TEXT NULL`.

Identity: `UNIQUE(session_id, sequence)`. The local `id` is a non-portable surrogate and is intentionally not on the wire.

### `live_frames`

`id INTEGER PRIMARY KEY AUTOINCREMENT`, `session_id TEXT NOT NULL`, `sequence INTEGER NOT NULL`, `timestamp_utc TEXT NOT NULL`, `data BLOB NOT NULL`, `decoder_version TEXT NULL`, `decode_status TEXT NOT NULL`.

Identity: `UNIQUE(session_id, sequence)`. `data` contains a genuine complete `FB FB ... FF` frame. The local `id` is not on the wire.

### RCM tables

- `rcm_input_tests`: primary key `(session_id, input_guid)`; preserves all physical, logical/CCF, testability, safety, notes, and result fields.
- `rcm_capture_windows`: primary key `capture_id`; preserves session/input identity, electrical state, UTC bounds, frame count, no-data flag, signature, and creation time.
- `rcm_capture_frames`: local surrogate `id`, stable identity `(capture_id, sequence)`, UTC timestamp, and exact frame BLOB.
- `rcm_comparisons`: primary key `(session_id, input_guid)`; preserves typed string-array comparison evidence, verification flag, result, and comparison time.

Schema v2 creates `UNIQUE INDEX ux_rcm_capture_frames_capture_sequence ON rcm_capture_frames(capture_id, sequence)`. Migration never deletes or rewrites evidence. If legacy duplicate identities exist, migration fails visibly rather than choosing a row.

### `sync_outbox`

Existing fields are `outbox_id`, `session_id`, `entity_type`, `state`, `created_utc`, `attempt_count`, `last_attempt_utc`, `last_error`, and `uploaded_utc`. Schema v2 adds:

- `lease_id TEXT NULL`
- `lease_acquired_utc TEXT NULL`
- `lease_expires_utc TEXT NULL`

The outbox is local delivery bookkeeping and is not uploaded as OTMR evidence.

The server mirrors the seven semantic evidence tables and their stable keys without the client-only outbox. Its server-only `server_upload_receipts` table has `session_id` as its primary key and stores `api_version`, `manifest_sha256`, `received_utc`, and all six manifest counts. The receipt has a restrictive foreign key to `recording_sessions` and commits in the same transaction as the uploaded evidence.

## 3. Persisted synchronization lifecycle

```text
RECORDING
   -> PENDING_UPLOAD
   -> UPLOADING (time-limited lease)
   -> UPLOADED
          or
      UPLOAD_FAILED -> retry -> UPLOADING
```

- `RECORDING`: capture is open and cannot be packaged.
- `PENDING_UPLOAD`: capture stopped, writer queue flushed, session/outbox committed, and eligible for upload.
- `UPLOADING`: one coordinator owns `lease_id` until `lease_expires_utc`.
- `UPLOAD_FAILED`: retryable; local evidence and outbox remain. `attempt_count`, `last_attempt_utc`, and bounded `last_error` are preserved.
- `UPLOADED`: set only after a completely validated positive server acknowledgement. Local data is not deleted.

On startup/coordinator execution, expired `UPLOADING` leases become `UPLOAD_FAILED`, their lease fields are cleared, and they become retryable. This covers a process/power failure after a server accepted data but before Windows stored the acknowledgement: the retry uses the same session identity and the server returns `alreadyPresent = true` if its manifest matches.

Schema v1 migration maps outbox `PENDING` to `PENDING_UPLOAD`, session `SYNCED` to `UPLOADED`, and recovered `INTERRUPTED_PENDING_UPLOAD` to `PENDING_UPLOAD`. Existing pending sessions are retained.

## 4. API v1 upload request

The network request is `OtmrApiV1UploadRequest`. All JSON property names and ordering are explicit. It contains no SQLite row dictionaries and no JSON document encoded inside another JSON string.

```text
apiVersion: integer (must be 1)
session: OtmrApiV1Session
rawEntries: OtmrApiV1RawEntry[]
liveFrames: OtmrApiV1LiveFrame[]
rcm:
  inputs: OtmrApiV1RcmInput[]
  captures: OtmrApiV1RcmCapture[]
  captureFrames: OtmrApiV1RcmCaptureFrame[]
  comparisons: OtmrApiV1RcmComparison[]
manifest: OtmrApiV1Manifest
```

### Session fields

`sessionId`, `startedUtc`, `finishedUtc`, `createdUtc`, `softwareVersion`, `comPort`, `serialSettings`, `vehicleIdentifier`, `vehicleType`, `ccfFilename`, `ccfSha256`, `rcmProfileFilename`, `rcmProfileSha256`, `rcmProfileJsonSnapshot`, and `notes`.

Local `sync_state` and `remote_session_id` are intentionally excluded: they are mutable delivery bookkeeping and would make an identical retry hash differently. This is an explicit exclusion, not silent loss of captured metadata.

### Raw/live fields

- Raw: `sessionId`, `sequence`, `timestampUtc`, `direction`, `data`, `interpretation`.
- Live: `sessionId`, `sequence`, `timestampUtc`, `data`, `decodeStatus`, `decoderVersion`.

`byte[]` is standard unpadded/padded-as-required JSON base64 produced by `System.Text.Json`; decoding must reproduce exactly the original byte array. Arrays are ordered by sequence.

### Typed RCM fields

RCM input/capture/capture-frame fields correspond one-for-one to their domain database columns, excluding local surrogate IDs. The former comparison JSON strings are parsed into JSON string arrays:

- `commonFeatures`
- `uniqueVoltageRemoved`
- `uniqueVoltageApplied`
- `repeatableDifferences`
- `candidateTransitions`

Thus the network `rcm` value is an object and none of these collections is double-encoded JSON.

### Example upload request

```json
{
  "apiVersion": 1,
  "session": {
    "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
    "startedUtc": "2026-08-27T09:00:00.0000000+00:00",
    "finishedUtc": "2026-08-27T09:05:00.0000000+00:00",
    "createdUtc": "2026-08-27T09:00:00.0000000+00:00",
    "softwareVersion": "1.0.0",
    "comPort": "COM2",
    "serialSettings": "38400/8/N/1",
    "vehicleIdentifier": "171804",
    "vehicleType": "Class 171",
    "ccfFilename": "CLASS171.ccf",
    "ccfSha256": "ccf-sha256",
    "rcmProfileFilename": "171.json",
    "rcmProfileSha256": "rcm-sha256",
    "rcmProfileJsonSnapshot": "{\"schemaVersion\":\"1.3\"}",
    "notes": "Depot validation"
  },
  "rawEntries": [
    {
      "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
      "sequence": 1,
      "timestampUtc": "2026-08-27T09:00:01.0000000+00:00",
      "direction": "RX",
      "data": "AQID/w==",
      "interpretation": null
    }
  ],
  "liveFrames": [
    {
      "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
      "sequence": 1,
      "timestampUtc": "2026-08-27T09:01:00.0000000+00:00",
      "data": "+/s4Sv8=",
      "decodeStatus": "RAW_NOT_DECODED",
      "decoderVersion": null
    }
  ],
  "rcm": {
    "inputs": [],
    "captures": [],
    "captureFrames": [],
    "comparisons": []
  },
  "manifest": {
    "rawEntryCount": 1,
    "liveFrameCount": 1,
    "rcmInputCount": 0,
    "rcmCaptureCount": 0,
    "rcmCaptureFrameCount": 0,
    "rcmComparisonCount": 0,
    "contentSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
  }
}
```

The hash above is illustrative; a real request contains the calculated value.

## 5. Deterministic JSON and manifest hash

Serialization uses `OtmrApiV1Json.Options`:

- UTF-8 JSON without BOM or indentation
- explicit camel-case `JsonPropertyName` and property order
- no dictionary payloads
- null properties are written
- UTC `DateTimeOffset` values formatted with invariant round-trip `O` format
- UUID values in standard JSON `D` form
- exact `byte[]` values encoded as base64
- source collections sorted by their stable identities before hashing

The manifest counts are:

`rawEntryCount`, `liveFrameCount`, `rcmInputCount`, `rcmCaptureCount`, `rcmCaptureFrameCount`, and `rcmComparisonCount`.

`contentSha256` is lowercase hexadecimal SHA-256 over the exact UTF-8 deterministic serialization of this object, with these properties in this order:

```json
{
  "apiVersion": 1,
  "session": { },
  "rawEntries": [ ],
  "liveFrames": [ ],
  "rcm": { }
}
```

The entire `manifest` property is excluded to avoid self-reference. The server must reproduce this exact serialization/hash algorithm after validating and ordering identities. Identical frozen content produces the same hash across retries even while local delivery state changes.

## 6. Identities and server uniqueness

- Session: `session_id`
- Raw entry: `(session_id, sequence)`
- Live frame: `(session_id, sequence)`
- RCM input: `(session_id, input_guid)`
- RCM capture: `capture_id`
- RCM capture frame: `(capture_id, sequence)`
- RCM comparison: `(session_id, input_guid)`

The server SQLite schema must enforce these keys and all parent foreign keys. Repeating identical content is successful and does not create duplicate rows. Reusing an identity with different bytes, timestamp, direction, parent, or other immutable content is HTTP `409 Conflict`; the server must never overwrite the first raw evidence.

## 7. Server acknowledgement

The server commits the complete package transaction before returning an acknowledgement. The Windows client validates API version, session ID, positive acceptance, nonblank remote ID, manifest hash, and all six counts before marking `UPLOADED`.

### Successful first upload

```json
{
  "apiVersion": 1,
  "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
  "remoteSessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
  "accepted": true,
  "alreadyPresent": false,
  "manifestSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "rawEntryCount": 1,
  "liveFrameCount": 1,
  "rcmInputCount": 0,
  "rcmCaptureCount": 0,
  "rcmCaptureFrameCount": 0,
  "rcmComparisonCount": 0
}
```

### Already-existing idempotent upload

```json
{
  "apiVersion": 1,
  "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
  "remoteSessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
  "accepted": true,
  "alreadyPresent": true,
  "manifestSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "rawEntryCount": 1,
  "liveFrameCount": 1,
  "rcmInputCount": 0,
  "rcmCaptureCount": 0,
  "rcmCaptureFrameCount": 0,
  "rcmComparisonCount": 0
}
```

This is success only when hash and all counts match the submitted manifest.

### Conflict response

HTTP status: `409 Conflict`.

```json
{
  "apiVersion": 1,
  "sessionId": "6be3fe50-1058-45f8-9db8-cf4213c2544f",
  "errorCode": "SESSION_MANIFEST_CONFLICT",
  "message": "The session identity already exists with different content.",
  "existingManifestSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "submittedManifestSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
}
```

Windows records the failure, clears its lease, leaves all local rows intact, and does not mark the session uploaded.

## 8. Windows uploader behavior

`OtmrSyncCoordinator.SynchronizePendingAsync` is an explicit/manual operation only:

1. Recover expired leases.
2. Query `PENDING_UPLOAD` and `UPLOAD_FAILED` outbox rows.
3. Atomically acquire a lease and set session/outbox to `UPLOADING`.
4. Build a transactionally consistent, finished-session API v1 package.
5. Send it through `IOtmrSyncClient`.
6. Validate the complete acknowledgement.
7. Set session/outbox to `UPLOADED` and save `remote_session_id` only after validation.
8. On network, timeout, cancellation, HTTP, conflict, or acknowledgement failure, retain evidence and set `UPLOAD_FAILED` for retry.

`BuildUploadPackageAsync` reads all session tables under one SQLite read transaction and rejects an active recording. `HttpOtmrSyncClient` makes one atomic POST. It does not delete local recordings.

## 9. Configuration and security

`OtmrSyncOptions` supplies:

- `Enabled`
- absolute `BaseUrl`, for example `https://otmr.example.com`
- externally supplied `ApiToken`
- `RequestTimeout`
- test-only insecure-loopback permission

Production URLs must use HTTPS. Plain HTTP is accepted only for an explicitly enabled loopback test URL. Tokens are sent as `Authorization: Bearer`; none are hard-coded or stored in repository configuration. Later WinForms settings must obtain secrets from an OS-protected/external source.

The server must:

- expose only HTTPS API access, never its SQLite file or a database port;
- authenticate uploader and mobile reader separately with least-privilege scopes;
- store `/var/lib/otmr/OTMR_RCM.db` with restricted service ownership;
- enable and verify foreign keys on every connection;
- validate request size, API version, identities, base64, counts, hash, and parent links before commit;
- use SQLite-safe backup/checkpoint procedures;
- never log credentials and avoid duplicating sensitive raw payloads in application logs.

## 10. Raw-data invariant

**Raw evidence is authoritative.** Server/mobile decoded representations must never replace, rewrite, normalize, or delete:

- `raw_serial_entries.data`
- genuine `live_frames.data`
- `rcm_capture_frames.data`

Decoder corrections create versioned derived views. They never mutate the captured bytes. Candidate/unverified/conflicted RCM mappings must not be presented as verified decoded states.

## 11. Implemented server read endpoints

The ASP.NET Core server provides authenticated, bounded endpoints for:

- `GET /api/v1/otmr/vehicles?limit=...`
- `GET /api/v1/otmr/vehicles/{vehicleIdentifier}/sessions?offset=...&limit=...`
- `GET /api/v1/otmr/vehicles/{vehicleIdentifier}/records?fromUtc=...&toUtc=...&limit=...`
- `GET /api/v1/otmr/vehicles/{vehicleIdentifier}/configuration`
- `GET /api/v1/otmr/vehicles/{vehicleIdentifier}/live`

The records endpoint returns permanently stored complete live-frame evidence in a required, bounded UTC range. All SQL values are parameterized and server-configured result limits are enforced. The live endpoint deliberately returns `liveAvailable: false` and no signals: completed-session synchronization is historical evidence, not realtime telemetry. Only explicitly verified, non-conflicted mappings may appear in any future decoded live API. The external MAUI project's final read DTOs still require joint review.

## 12. Implemented server acceptance rules and remaining deployment work

The .NET 8 server reuses this shared DTO assembly and canonical hash implementation. It validates API version, session identity, collection identities, all six counts, complete live/capture frame boundaries, and the manifest hash before persistence. A new upload inserts all seven semantic evidence collections and `server_upload_receipts` in one SQLite transaction. Exact retries return the existing receipt; conflicting evidence returns HTTP 409 and is never overwritten.

Remaining work before production deployment:

1. Select the production DNS name/Droplet and provision a high-entropy token using an external environment file; establish a rotation procedure for Windows and mobile clients.
2. Decide the fleet policy for packages whose `vehicleIdentifier` is null. They are preserved but omitted from vehicle-oriented queries.
3. Measure real long-session payload sizes before designing any future chunked v2 protocol. API v1 remains one atomic request.
4. Freeze mobile-specific read response DTOs with the separate MAUI repository; current read shapes are a safe historical foundation, not a claim of final mobile UI compatibility.
5. Design a separate authenticated realtime ingestion path. Never reinterpret completed-session upload as a live feed.
6. Define retention, monitoring, disk-capacity alerts, bearer-token rotation, and tested off-host backup/restore operations.
7. A UI/manual command must still be designed before operators invoke synchronization; no automatic network timer was added.

These blockers do not change the frozen API v1 upload request, acknowledgement, stable identities, or raw-data preservation rules above.

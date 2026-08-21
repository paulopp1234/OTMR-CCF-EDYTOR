# OTMR local database and future online sync

## Roles of each file/system

- **CCF** = logical OTMR configuration.
- **RCM JSON** = editable RCM/physical test definition.
- **SQLite (`OTMR_RCM.db`)** = captured data, RCM test data, history, and the upload queue.
- **Future PostgreSQL server** = permanent shared online history used by the Windows application and later Android application.

SQLite does not replace the RCM JSON. A recording session stores the JSON filename, SHA-256 and an immutable JSON snapshot so historical data can always be tied back to the exact test definition that produced it.

## Local recording

The Windows OTMR Live tab has explicit **Start Recording** and **Stop Recording** controls.

Recording is never started just because a COM port is opened.

While recording is active the database stores:

1. Every raw front-RS232 RX callback exactly as received.
2. Every raw front-RS232 TX callback exactly as transmitted, including the controlled start sequence.
3. Every complete assembled `FB FB ... FF` live frame.
4. Structured RCM capture windows for Voltage Removed and +24 V Applied.
5. The stable RCM input GUID and a snapshot of the input definition used for that test.
6. RCM raw comparison evidence. Semantic decoding remains unverified until a decoder is actually proven.

Raw bytes are stored as SQLite **BLOBs**, not only as display hex strings.

The runtime database is stored under the current Windows user's Local Application Data folder and is ignored by Git.

## Offline-first behaviour

The Windows application records locally first. Internet access is not required for an OTMR test.

When a recording session is stopped it is flushed to SQLite and a row is added to `sync_outbox` with state `PENDING`.

This means a future server outage or depot Wi-Fi outage does not lose the test. The local data remains available until the server confirms the upload.

## Planned online server

The intended first server is a small DigitalOcean Ubuntu server containing:

- ASP.NET Core Web API
- PostgreSQL
- HTTPS

The Windows app and future Android app will use the API. Neither application should expose or connect directly to PostgreSQL over the public internet.

### Planned upload sequence

For each pending SQLite session:

1. `POST /api/v1/otmr/recording-sessions`
   - uploads session metadata and provenance
   - uses the local session GUID as an idempotency key
2. `POST /api/v1/otmr/recording-sessions/{sessionId}/raw-entries`
   - uploads raw RS232 entries in batches (recommended 500 rows)
3. `POST /api/v1/otmr/recording-sessions/{sessionId}/live-frames`
   - uploads complete raw live frames in batches
4. `POST /api/v1/otmr/recording-sessions/{sessionId}/rcm-results`
   - uploads structured RCM captures/comparisons
5. `POST /api/v1/otmr/recording-sessions/{sessionId}/complete`
   - server confirms the complete session

Only after the server confirms completion should the local outbox row be changed from `PENDING` to `UPLOADED` and the session be marked `SYNCED`.

If upload fails, the local row remains pending and stores the attempt count/error for retry later.

## Android application later

The Android application should read the same PostgreSQL-backed data through the ASP.NET Core API, for example:

- fleet/vehicle list
- latest RCM result
- input-level results
- historical tests
- decoded OTMR signals when the RS232 decoder is eventually verified

The Android application should never read the local Windows SQLite file and should never connect directly to PostgreSQL.

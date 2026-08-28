# OTMR CCF Editor / Creator — .NET 8 WinForms

Current released application revision: **v0.3.0**.

> Controlled Class 171 bench validation update (2026-08-25): the application
> now has an explicit selected-CCF preflight and a reply-gated, byte-proven live
> START sequence. **Stop Live** is close-only; **Stop + Restore** is a separate
> captured cleanup/restoration exchange based on frozen pre-START recorder data.
> This does not claim successful validation on physical OTMR hardware. See
> `docs/CLASS171_OTMR_CONTROLLED_BENCH_INTEGRATION_20260825.md`.

## `test` branch — OTMR Live Milestone 1

The `test` branch adds the first OTMR serial module without changing the existing CCF editor design or file-handling rules.

Milestone 1 contains only:

- a new Designer-managed **OTMR Live - M1** tab
- COM-port enumeration
- proven Class 171 bench serial settings: **38400 baud, 8 data bits, no parity, 1 stop bit**
- Connect / Disconnect
- exact timestamped RX capture
- exact timestamped TX capture at the transport boundary for future safe commands
- Clear Capture
- **Copy Hex** for the selected captured frame/chunk
- display-only **Show: All / RX / TX** filtering
- visible **Shown / Total** capture counts
- Save Capture as **JSON Lines (`.jsonl`)** with raw hex retained
- Save Capture as a simple human-readable **text (`.txt`)** log
- no protocol decoder or framing assumptions

The RX/TX display filter never removes data from the underlying capture. **Save Capture always exports the complete raw session**, including both TX and RX entries, regardless of the current on-screen filter.

Milestone 1 deliberately does **not** expose or automatically transmit:

- startup/session commands
- identity requests
- start-live or stop-live commands
- manual raw TX
- capture replay
- Program OTMR
- write configuration
- erase / firmware / unidentified commands

Connecting a COM port does not send an OTMR protocol command. It only opens the proven 38400/8N1 serial connection and records bytes received from the recorder.

New isolated project/module:

```text
src/CcfEditor.Otmr/
  Transport/
    IOtmrTransport.cs
    OtmrSerialSettings.cs
    SerialOtmrTransport.cs
  Capture/
    OtmrDirection.cs
    OtmrCaptureEntry.cs
    OtmrCaptureWriter.cs
  Live/
    OtmrLiveService.cs
  Bench/
    OtmrBenchPinDefinition.cs
    OtmrBenchProfileReader.cs
    OtmrBenchLiveActivity.cs
```

WinForms integration:

```text
src/CcfEditor.WinForms/
  OtmrLiveControl.cs
  OtmrLiveControl.Designer.cs
  OtmrLiveControl.resx
  OtmrBenchControl.cs
  OtmrBenchControl.Designer.cs
  OtmrBenchControl.resx
  MainForm.BenchIntegration.cs
  Profiles/Class171/Class171_Bench_PinMap.tsv
```

The OTMR controls are normal Visual Studio Designer `UserControl`s; they are not built dynamically at runtime.

## `test` branch — OTMR I/O Bench

The top-level **OTMR I/O Bench** tab is an observation/comparison tool for physical Class 171 OTMR connector testing.

Its purpose is to keep four different kinds of information visibly separate:

1. **Physical pin reference** — external J1/J2 bench/wiring information.
2. **Reference CCF expectation** — expected record/card/channel where source evidence exists.
3. **Current opened CCF** — record name/type/card/channel/pair read from the actual CCF opened in the editor.
4. **Live OTMR observation** — actual decoded record/state once a verified read/live decoder is available.

The table contains:

- connector pin
- role
- MIO/channel reference
- physical/expected function
- safety/bench instruction
- reference record pair
- reference card/channel
- current opened-CCF interpretation
- CCF structure check
- live observed record(s)
- live value/state
- bench result

The user can select one row and press **Arm Selected Pin** before physically stimulating that input. Once a verified OTMR live decoder is connected, every decoded record change occurring during that armed test can be recorded against the selected physical pin. Multiple observed records are deliberately retained rather than silently choosing one.

Bench result states include:

- `WAITING`
- `LIVE MATCH`
- `MISMATCH / EXTRA`
- `DISCOVERED`
- `DISCOVERED MULTIPLE`
- `NOT TESTABLE`

### Bench safety classification

The tab does not apply voltage and does not transmit OTMR commands. It is an observation tool only.

Rows classified as returns, supplies, RS485, termination links or unresolved/special interfaces are visibly marked as not voltage-testable and cannot be armed by the UI. A connector being populated does **not** mean that applying 24 V to that contact is safe.

### J1 coverage

The bundled Class 171 external profile currently contains the source-backed J1 bench rows, including the proven MIO1 digital bank, AWS/TPWS/MIO5 reference rows, returns, supply/reference contacts, RS485 contacts and unresolved/special contacts. The source/reference profile remains separate from CCF parsing.

### J2 coverage

J2 is intentionally **not treated as solved**. The bundled profile currently contains only the present bench-discovery candidates:

- `J2-A` — Headlight Left
- `J2-Q` — Fire Alarm Isolation
- `J2-f` — Wheelslide

Current logical channel-0 candidates remain record/card possibilities rather than proven physical-card assignments. Missing J2 contacts are not invented. The tab supports **Load Pin Map...** so a complete source-backed J2 TSV can replace/extend the bundled reference when available.

### Current live limitation

Milestone 1 captures raw serial bytes but does not yet contain a verified Arrowvale/Grinsty live-record decoder. Therefore the I/O Bench UI and its `OtmrBenchLiveActivity` input contract are ready, but automatic population of **Live observed record(s)** will remain inactive until genuine Analyser captures are used to implement and verify the safe startup/live protocol.

## Revision history

- **v0.1** — initial usable CCF viewer/editor foundation with CCF-only data provenance, Save As protection and per-app startup authorisation.
- **v0.2** — adds the permanent right-hand Records field-description/schema-help pane, visible application revision and executable version metadata.
- **v0.3.0** — current desktop application revision with OTMR live, RCM bench/live and manual server-sync foundations.

This is a Visual Studio WinForms Designer project.

## Data provenance

The CCF editor itself remains CCF-only:

- Records: parsed from the opened CCF bytes.
- Header: raw and decoded values calculated from the opened CCF bytes.
- Hex: exact working CCF bytes.
- Validation: calculated from the opened/working CCF.

The **OTMR I/O Bench** tab is different by design: it can load an explicitly separate external physical pin-reference profile. The bundled `Class171_Bench_PinMap.tsv` is labelled and treated as external reference/schema information. It is never merged into the CCF parser and never replaces values read from the opened CCF.

The header `Meaning` labels and the Records field-description pane are schema/help metadata. Actual Records/Header/Hex values are read from the CCF that the user opened.

## Editing

Editing writes directly to the working byte array. The application does not rebuild or reserialize the full CCF, so unknown bytes remain untouched unless the user edits a field that owns those exact bytes.

Editable record grid fields:

- Type
- Name (maximum 15 ASCII characters, NUL terminated and zero padded inside the existing 16-byte field)
- Colour as four raw bytes / eight hex digits
- Card
- Channel
- Logger mode
- Hardware function
- Pair record for digital Type 2 only
- OFF text for digital Type 2 only
- ON text for digital Type 2 only

Record/event index, classification flag and unknown/type-specific bytes remain read-only in this stage.

Supported Header fields can be edited by changing the `Decoded / edit value*` cell. The `Editable` column identifies which rows are enabled. Fields whose encoding is not sufficiently proven remain read-only, including the profile/family word, JP6, JP12, mileage raw field and the unknown 0x0231 byte.

The Hex view is rebuilt from the working bytes after every edit. Changed hex rows are highlighted and the status bar shows the exact count of bytes different from the opened file.

Saving is **Save As only**. The source `.ccf` is never silently overwritten. The saved file is read back and verified against the complete working byte array and working SHA-256.

## Records field description pane

The Records tab contains a permanent right-hand `Field description / schema help` pane. Clicking any record cell shows:

- field name
- current value from the opened CCF
- relative and absolute CCF byte location
- whether the field is editable
- a description of the field and any type-specific restrictions

For digital-only fields such as Pair, OFF text and ON text, the pane explicitly warns when the selected record is not Type 2. It also avoids inventing meanings for incompletely decoded values such as Logger and Hardware Function codes.

## Visual Studio Designer

`MainForm.cs` contains application logic.
`MainForm.Designer.cs` contains the form controls/layout.
`MainForm.FieldHelp.cs` contains Records field-help behaviour.
`MainForm.BenchIntegration.cs` exposes the current opened CCF to the bench UserControl without changing the editor logic.
`MainForm.resx` is linked to the form.

`OtmrLiveControl` and `OtmrBenchControl` each have conventional `.cs`, `.Designer.cs` and `.resx` files. The bench control suppresses runtime profile/host access while Visual Studio is using it in design mode.

Right-click `MainForm.cs` -> **View Designer**.

## Remote startup gate

Before the WinForms application creates `MainForm`, it reads:

`https://raw.githubusercontent.com/paulopp1234/CL380_App_Control/main/status.txt`

This application ignores the existing CL380 control line and checks only its own entry:

`OTMR CCF EDYTOR - ALLOW_START`

To block only this application, change that line to:

`OTMR CCF EDYTOR - DO_NOT_START`

The gate is fail-closed. A missing/duplicate OTMR line, unknown or empty status, HTTP error, network failure, timeout, or GitHub being unavailable prevents the application from opening.

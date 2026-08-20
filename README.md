# OTMR CCF Editor / Creator — .NET 8 WinForms

Current released application revision: **v0.2**.

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
- Save Capture as JSON Lines with raw hex retained
- no protocol decoder or framing assumptions

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
```

WinForms integration:

```text
src/CcfEditor.WinForms/
  OtmrLiveControl.cs
  OtmrLiveControl.Designer.cs
  OtmrLiveControl.resx
```

The OTMR UI is a normal Visual Studio Designer `UserControl`; it is not built dynamically at runtime.

## Revision history

- **v0.1** — initial usable CCF viewer/editor foundation with CCF-only data provenance, Save As protection and per-app startup authorisation.
- **v0.2** — adds the permanent right-hand Records field-description/schema-help pane, visible application revision and executable version metadata.

This is a Visual Studio WinForms Designer project.

## Data provenance

The visible CCF data comes only from the `.ccf` file explicitly opened by the user.

- Records: parsed from the opened CCF bytes.
- Header: raw and decoded values calculated from the opened CCF bytes.
- Hex: exact working CCF bytes.
- Validation: calculated from the opened/working CCF.

No J1 spreadsheet, TSV, Class 171 mapping table, demo data, sample records, or fallback configuration is bundled or auto-loaded by the application.

The header `Meaning` labels and the Records field-description pane are schema/help metadata. Actual displayed current values are read from the CCF that the user opened.

External mapping/import functionality can be added later only as an explicit user action and must remain clearly separate from CCF-derived data.

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
`MainForm.FieldHelp.cs` contains the field-help behaviour only.
`MainForm.resx` is linked to the form.

Right-click `MainForm.cs` -> **View Designer**.

The editing UI and right-side help pane are Designer-managed controls; the program does not construct the GUI dynamically at runtime.

## Remote startup gate

Before the WinForms application creates `MainForm`, it reads:

`https://raw.githubusercontent.com/paulopp1234/CL380_App_Control/main/status.txt`

This application ignores the existing CL380 control line and checks only its own entry:

`OTMR CCF EDYTOR - ALLOW_START`

To block only this application, change that line to:

`OTMR CCF EDYTOR - DO_NOT_START`

The gate is fail-closed. A missing/duplicate OTMR line, unknown or empty status, HTTP error, network failure, timeout, or GitHub being unavailable prevents the application from opening.

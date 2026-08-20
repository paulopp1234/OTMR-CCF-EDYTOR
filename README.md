# CCF Editor / Creator — .NET 8 WinForms

This build is a Visual Studio WinForms Designer project.

## Data provenance

The visible CCF data comes only from the `.ccf` file explicitly opened by the user.

- Records: parsed from the opened CCF bytes.
- Header: raw and decoded values calculated from the opened CCF bytes.
- Hex: exact opened-file bytes.
- Validation: calculated from the opened CCF.

No J1 spreadsheet, TSV, Class 171 mapping table, demo data, sample records, or fallback configuration is bundled or auto-loaded by the application.

The header `Meaning` labels are schema metadata for known offsets; the displayed raw/decoded values are from the opened file.

External mapping/import functionality can be added later only as an explicit user action and must remain clearly separate from CCF-derived data.

## Visual Studio Designer

`MainForm.cs` contains application logic.
`MainForm.Designer.cs` contains the form controls/layout.
`MainForm.resx` is linked to the form.

Right-click `MainForm.cs` -> **View Designer**.

## Remote startup gate

Before the WinForms application creates `MainForm`, it reads:

`https://raw.githubusercontent.com/paulopp1234/CL380_App_Control/main/status.txt`

This application ignores the existing CL380 control line and checks only its own entry:

`OTMR CCF EDYTOR - ALLOW_START`

To block only this application, change that line to:

`OTMR CCF EDYTOR - DO_NOT_START`

The gate is fail-closed. A missing/duplicate OTMR line, unknown or empty status, HTTP error, network failure, timeout, or GitHub being unavailable prevents the application from opening.

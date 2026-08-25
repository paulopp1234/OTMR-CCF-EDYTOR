# Class 171 OTMR live-start evidence boundary (2026-08-24)

Sources:

- `TestData/HHD_Serial_Trace_20260824_143201.txt` — authoritative Analyser.exe writes, reads, port lifecycle, line control, and timestamps.
- `TestData/OTMR_RAW_CAPTURE_20260824.txt` — software-side RX chunking and byte confirmation.

## Captured read/interrogation phase

The initial port is opened at 38400/8/N/1 with RTS LOW and DTR LOW. Normal Connect sends no bytes. The explicit Start action performs this reply-gated sequence:

| Required complete RX | Analyser.exe TX enabled only after that RX |
|---|---|
| Start action | `01 01 00 01 01 01 01 01 02 01 03 01 04` |
| `01 01` reply | no TX; continue waiting for the `01 02` data block |
| `01 02` data, 0x10B bytes | `01 02 00 01 01 00 02 01 02 02 03 02 04`, then `01 03 00 01 01 01 00 00 02 03 00 04` |
| `01 03` reply | no TX; continue waiting for `01 04` data |
| `01 04` data, 0x10B bytes | `01 04 00 01 01 00 02 01 02 04 03 04 04`, then `01 05 00 01 01 01 00 00 02 03 00 04` |
| `01 05` reply | no TX; continue waiting for `01 06` data |
| `01 06` data, 0x10B bytes | `01 06 00 01 01 00 02 01 02 06 03 06 04`, then `01 07 00 01 01 01 00 00 02 03 00 04` |
| `01 07` reply | no TX; continue waiting for `01 08` data |
| `01 08` data, 0x10B bytes | `01 08 00 01 01 00 02 01 02 08 03 08 04`, then `01 09 00 01 01 01 00 00 02 03 00 04` |
| `01 09` reply | no TX; continue waiting for `01 0A` data |
| `01 0A` data, 0x10B bytes | `01 0A 00 01 01 00 02 01 02 0A 03 0A 04`, then `01 0B 00 01 01 01 00 00 02 03 00 04` |
| `01 0B` reply | no TX; continue waiting for `01 0C` data |
| `01 0C` data, 0xD6 bytes | stop at `RecorderConfigurationWriteBlocked` |

RX frames may be arbitrarily fragmented. Raw transport chunks are recorded unchanged; protocol assembly is separate from raw capture/database recording.

## Deliberately blocked recorder writes

Immediately after the complete `01 0C` data block, the HHD trace contains seven Analyser.exe writes: a 13-byte `01 0C` transition/acknowledgement followed by configuration blocks of 0x10B, 0x10B, 0x10B, 0x10B, 0x10B, and 0xD2 bytes. These lead through recorder replies `01 0D` to `01 13`.

| HHD event | Length | SHA-256 |
|---|---:|---|
| 000301 (`01 0C` transition) | 0x0D | `CB33A615758D78F150495610DD2849ACF5D1100901CB1A622F3B0D0EF3002291` |
| 000312 (configuration page 1) | 0x10B | `8A40FD74B018F0807A9689C76FB1DC2795BCE1F39D3E45486AB2DA5525543BA5` |
| 000335 (configuration page 2) | 0x10B | `CC2E34C7D6EDC2A8DBA40DF7EE4383E2371127490210ECBB129BE9CEF0ED9992` |
| 000357 (configuration page 3) | 0x10B | `3709E87FA10DFA7E06C7797CE7BEE5550EA9A20A70F37F5FCC8FCF06BFD0CF9B` |
| 000378 (configuration page 4) | 0x10B | `152BBAF8861969838EDBE02A364579D46E2920EB07829D96BD8F0EBA020F4C2C` |
| 000401 (configuration page 5) | 0x10B | `ADB1936D3AC4E2411AE094B0C732E0674DF152DAAE22102E4B5A2157D867FDD7` |
| 000423 (configuration page 6) | 0xD2 | `4596E51EF7142736AE6F222190241AE0A412419F0CF0A748C574247968F8B852` |

The large writes are not safe echoes of the current OTMR read pages. Five differ materially in payload layout/content; the final write also changes the payload length. Their contents cannot be safely generated from the current-recorder data using the supplied evidence. The WinForms application therefore has no path that supplies or transmits these recorder-specific blocks, and does not transmit the preceding `01 0C` transition either.

The byte-level derivation, complete per-offset CSV, check-byte rule, and safety
assessment are in
[`CLASS171_OTMR_BLOCKED_WRITE_ANALYSIS_20260824.md`](CLASS171_OTMR_BLOCKED_WRITE_ANALYSIS_20260824.md).

An internal test-only evidence object accepts the seven blocks only when their SHA-256 values exactly match the authoritative HHD trace. It exists solely to validate the captured post-boundary ordering and final port transition; it is not instantiated by application code.

## Captured final live transition

Only after the exact complete reply

`01 13 00 01 01 01 01 01 02 06 03 06 04`

does the evidence path send

`01 07 00 01 01 01 02 01 02 13 03 13 04`.

HHD records that write at 14:32:50.8343001 and COM close at 14:32:50.8519293, an interval of about 17.6 ms. The implementation uses 17.6 ms, removes the former invented 120 ms delay, and reopens immediately after close with no former 150 ms delay. Reopen is 38400/8/N/1, RTS LOW, DTR HIGH.

`LiveActive` is entered only after the live-frame assembler receives a complete `FB FB ... FF` frame. The existing RCM gate continues to require `OtmrLiveState.LiveActive`.

## Second full session and STOP update (2026-08-25)

The second full TX/RX HHD capture resolves 12 of the previous 14 target bytes as
repeated transformation values, but proves that `000312` offsets `0x08B` and
`0x0BD` differ between START and STOP cleanup. Those two phase-dependent bytes
keep `RecorderConfigurationWriteBlocked` active.

Arrowvale STOP first closes the live DTR-high port without a TX command, then
reopens RTS LOW / DTR LOW, repeats the complete protocol exchange, sends final
`01 07`, and closes without another reopen. Exact bytes, timing, and safety
analysis are in
[`CLASS171_OTMR_SECOND_HHD_AND_STOP_20260825.md`](CLASS171_OTMR_SECOND_HHD_AND_STOP_20260825.md).

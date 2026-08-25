# Second full HHD session and STOP analysis (2026-08-25)

## Sources and exchange boundaries

Authoritative HHD sources:

- `TestData/HHD_Serial_Trace_20260824_143201.txt`
- `TestData/HHD_Serial_Trace_20260825_START_LIVE_STOP.txt`

The new trace contains three serial-port phases:

1. Initial RTS LOW / DTR LOW port: complete START interrogation and indexed
   configuration exchange, ending with final `01 07` and close.
2. Reopened RTS LOW / DTR HIGH port: realtime traffic.
3. STOP cleanup: close the live port, reopen RTS LOW / DTR LOW, perform another
   complete 19-frame exchange, send final `01 07`, and close without reopening.

The 2026-08-25 START and STOP RX exchanges are byte-for-byte identical. Compared
with 2026-08-24, only two payload bytes in RX `01 02` differ:

| Continuous RX location | 2026-08-24 | 2026-08-25 START | 2026-08-25 STOP |
|---|---:|---:|---:|
| page 1 payload `0x86` | `04` | `01` | `01` |
| page 1 payload `0xB8` | `00` | `03` | `03` |

The complete 2026-08-24 START TX exchange and 2026-08-25 START TX exchange are
byte-for-byte identical. The STOP TX exchange differs only at the two target
positions corresponding to the changing source fields.

Per-frame SHA-256 comparison is in
[`CLASS171_OTMR_HHD_EXCHANGE_COMPARISON_20260824_20260825.csv`](CLASS171_OTMR_HHD_EXCHANGE_COMPARISON_20260824_20260825.csv).

## Re-evaluation of the previous 14 target bytes

The complete byte table, including all three TX values and related continuous RX
values, is
[`CLASS171_OTMR_HHD_UNKNOWN_BYTES_20260824_20260825.csv`](CLASS171_OTMR_HHD_UNKNOWN_BYTES_20260824_20260825.csv).

### Resolved by repeated transformation rule: 12 bytes

These values are identical in the 2026-08-24 START, 2026-08-25 START, and
2026-08-25 STOP exchanges:

| TX | Full-frame offsets | Value | Classification |
|---|---|---|---|
| `000312` | `0x009` | `02` | continuous-stream prefix replacement constant |
| `000357` | `0x086..0x087` | `00 00` | structural zero insertion |
| `000357` | `0x0D6..0x0D7` | `00 00` | structural zero insertion |
| `000378` | `0x00F..0x010` | `00 00` | cross-page structural zero insertion |
| `000378` | `0x067..0x068` | `00 00` | structural zero insertion |
| `000378` | `0x097..0x098` | `00 00` | structural zero insertion |
| `000423` | `0x0CD` | `40` | continuous-stream terminal replacement constant |

This resolves their observed values as repeated START/STOP transformation rules.
It does not make the indexed configuration operation harmless or prove that the
recorder never persists it.

### Phase-dependent bytes resolved by the selected CCF: 2 bytes

| TX offset | Related RX | 2026-08-24 START | 2026-08-25 START | 2026-08-25 STOP |
|---|---|---:|---:|---:|
| `000312 0x08B` | page 1 payload `0x86`: `04 / 01 / 01` | `FA` | `FA` | `FD` |
| `000312 0x0BD` | page 1 payload `0xB8`: `00 / 03 / 03` | `03` | `03` | `00` |

The two repository Class 171 CCFs are distinct files but both carry the same
header/configuration values at the aligned positions: `04` at CCF `0x0231` and
`00` at CCF `0x0263`. The START page-1 payload otherwise aligns with the same
loaded CCF header region, including its version, serial, vehicle, unit, and
vehicle-type fields. Those CCF values resolve the phase-dependent source:

- START selects desired `CCF[0x0231] = 04`, then
  `TX[0x08B] = (FE - 04) mod 256 = FA`.
- START selects desired `CCF[0x0263] = 00`, then
  `TX[0x0BD] = (03 - 00) mod 256 = 03`.
- STOP selects the values read from the recorder before cleanup, `01 / 03`, and
  the same transforms produce `FD / 00`.

This is byte-level evidence that `FA / 03` are neither universal constants nor
copied from the current recorder: START encodes selected desired CCF values,
whereas STOP encodes the cached original recorder values. It supports a
restoration purpose for STOP, although the trace has no post-write read-back or
power cycle with which to prove persistence semantics.

`OtmrProtocolDerivation.CreateConfigurationPage01WriteFromCcf` now generates
the complete captured page-1 write from the current recorder pages plus the
currently selected CCF. Capture-backed tests reproduce both START page-1 writes
and the STOP restoration page. The helper is not connected to the transport.
`RecorderConfigurationWriteBlocked` remains active: the UI/live service does
not yet bind an explicitly selected and validated CCF to a complete generated
six-page transaction, and this analysis does not authorize transmitting the
potentially persistent indexed write.

> Subsequent implementation now generates and validates the complete seven-write
> START and STOP/restoration exchanges in memory. The transport boundary remains
> unchanged. See
> [`CLASS171_OTMR_GENERATED_CONFIGURATION_EXCHANGE_20260825.md`](CLASS171_OTMR_GENERATED_CONFIGURATION_EXCHANGE_20260825.md).

No `Analyser.exe` or other Arrowvale binary is present anywhere in the workspace,
so no executable static analysis was possible or used for this conclusion.

## Realtime tail and STOP lifecycle

HHD read event `000294` is the last recorded realtime RX buffer. It is exactly
`0xD3` (211) bytes; the full byte sequence is preserved in
[`CLASS171_OTMR_STOP_TIMELINE_20260825.csv`](CLASS171_OTMR_STOP_TIMELINE_20260825.csv).
Its final complete realtime frame is exactly:

`FB FB 38 8F 38 90 38 8F 38 90 FF`

No TX Write Request, purge, RTS change, or DTR change occurs between the live
reopen and the first STOP close.

| Event | Timestamp | Operation/timing |
|---|---|---|
| `000991` | `08:34:36.4766763` | close live RTS LOW / DTR HIGH port |
| `000993` | `08:34:36.6770042` | cleanup open requested, `200.3279 ms` after close |
| `000994` | `08:34:36.7020128` | cleanup open completed, `225.3365 ms` after close |
| `001011` | `08:34:36.7085671` | set 38400 baud |
| `001013` | `08:34:36.7100470` | RTS LOW |
| `001015` | `08:34:36.7111323` | DTR LOW |
| `001017` | `08:34:36.7119980` | 8 data bits, no parity, 1 stop bit |
| `001025` | `08:34:36.7149450` | purge RX/TX requests and buffers |
| `001029` | `08:34:36.7187099` | purge RX/TX requests and buffers |
| `001031` | `08:34:36.7190059` | purge RX/TX requests and buffers |
| `001033` | `08:34:36.7192584` | send `01 01`, `242.5821 ms` after live close |
| `001245` | `08:34:47.8269134` | send final `01 07` after complete `01 13` |
| `001247` | `08:34:47.8314752` | close, `4.5618 ms` after final `01 07` |

There is no subsequent COM reopen in the capture.

### What actually stops realtime

The realtime transport stops when Arrowvale closes the DTR-high port; there is
no distinct stop command sent while that live port is open. However, the complete
Arrowvale STOP operation is not close-only: after the close it reopens DTR LOW,
performs the full interrogation/indexed-write exchange, sends the same final
`01 07`, and closes again. The evidence supports calling this a STOP cleanup or
restoration exchange, not a single stop-live protocol command.

The trace proves Arrowvale's behaviour, but not that the recorder electrically
requires the cleanup exchange merely to cease realtime transmission.

## Reproduction

```powershell
& .\tools\Compare-OtmrHhdSessions.ps1 -WriteCsv
```

# Working Arrowvale capture comparison: 2026-08-17 versus 2026-08-24

> Update: the full 2026-08-25 TX/RX HHD capture resolves 12 of the 14 target
> values below by repetition and shows that the remaining two are START/STOP
> phase-dependent. See
> [`CLASS171_OTMR_SECOND_HHD_AND_STOP_20260825.md`](CLASS171_OTMR_SECOND_HHD_AND_STOP_20260825.md).

## Scope and capture integrity

The requested capture was found at:

`C:\OTMR\Class153OtmrMonitor\tools\RS485Analyser\bin\Release\net8.0-windows\win-x64\logs\ScpSniffer_COM2_20260817_112520.bin`

It is 2,241 bytes. The adjacent `ScpSniffer_COM2_20260817_112520.hex.log`
is required to recover callback timestamps and direction. Inspection of the
adjacent `ScpSniffer.RawLogWriter` source proves that `.bin` receives only bytes
passed to `WriteReceived`; sent bytes are written only to the `.hex.log`.

The binary is byte-for-byte identical to the concatenation of all 26 sidecar RX
records. The sidecar contains zero TX records. This is an important evidence
limit: the previous Arrowvale Write Requests are not present in either file, so
old-session TX values cannot be compared directly or reconstructed by assuming
that they equal the 2026-08-24 writes.

The RX evidence is complete enough to prove the working progression:

- 19 valid protocol frames, transactions `01` through `13` in order;
- all 19 payload checks satisfy the modulo-256 payload-sum rule;
- RX `01 13` is followed by realtime traffic;
- five RX callbacks begin with `FB FB`, and their fragmented data is terminated
  by later `FF` callbacks.

This is consistent with the stated working Class 150 recorder/Class 171 coding
plug session. The binary does not contain the Start or Stop TX operations.

## Source recorder data differences

The six recorder-data payloads were compared at identical payload offsets:

| 2026-08-24 blocked TX | Source RX | Payload bytes | Bytes differing from working capture |
|---|---|---:|---:|
| `000312` | `01 02` | 255 | 18 |
| `000335` | `01 04` | 255 | 9 |
| `000357` | `01 06` | 255 | 12 |
| `000378` | `01 08` | 255 | 8 |
| `000401` | `01 0A` | 255 | 0 |
| `000423` | `01 0C` | 202 | 0 |

The complete 47-byte RX difference table is
[`CLASS171_OTMR_WORKING_CAPTURE_RX_DIFFS_20260817_20260824.csv`](CLASS171_OTMR_WORKING_CAPTURE_RX_DIFFS_20260817_20260824.csv).

RX `01 02` includes recorder/configuration identity differences, including
firmware `V02.07_` versus `V02.04_` and recorder identifier `04B771` versus
`05C077`. The differing values throughout RX `01 02`, `01 04`, `01 06`, and
`01 08` therefore cannot be treated as protocol constants. RX `01 0A` (all
zeros) and RX `01 0C` are identical between these two sessions.

## Continuous six-page stream correction

The earlier analysis compared each RX/TX page locally. Concatenating all six RX
payloads and all six Analyser TX payloads exposes the actual cross-page flow:

- current-recorder RX stream: 1,477 bytes;
- Analyser TX stream: 1,473 bytes;
- longest common subsequence: 1,459 copied bytes;
- 14 TX bytes remain outside that copied sequence;
- the net four-byte shortening occurs at the beginning, where five RX bytes
  `06 00 00 05 C0` become one TX byte `02`.

Consequently, page boundaries are sliding windows over a continuous structure.
These formerly unexplained tail bytes are copied from the next recorder RX page:

| TX event and full-frame offsets | Proven current-recorder source | Working/current source comparison |
|---|---|---|
| `000312 0x104..0x107` = `00 00 00 00` | RX `01 04` payload `0x00..0x03` | constant |
| `000335 0x104..0x107` = `00 00 00 03` | RX `01 06` payload `0x00..0x03` | constant |
| `000357 0x102..0x107` = six zeros | RX `01 08` payload `0x00..0x05` | constant |
| `000378 0x104..0x107` = four zeros | RX `01 0A` payload `0x00..0x03` | constant |

This accounts for 18 TX bytes previously classified as unknown. In particular,
all 255 payload bytes of `000335` are derivable from the current recorder:

- TX payload `0x00..0xFA` copies RX `01 04` payload `0x04..0xFE`;
- TX payload `0xFB..0xFE` copies RX `01 06` payload `0x00..0x03`.

The full comparison of all previously unknown/modified target bytes is
[`CLASS171_OTMR_WORKING_CAPTURE_FIELD_COMPARISON_20260817_20260824.csv`](CLASS171_OTMR_WORKING_CAPTURE_FIELD_COMPARISON_20260817_20260824.csv).

## Fields that remain unexplained

Continuous alignment leaves the following 14 target bytes without a copied RX
source or proven generation rule:

| TX | Target full-frame offsets/value | Current source involved | Working source at same structural field | Finding |
|---|---|---|---|---|
| `000312` | `0x009 = 02` | RX `01 02` payload `0x00..0x04` = `06 00 00 05 C0` | same | 5-to-1 rule unknown |
| `000312` | `0x08B = FA` | RX `01 02` payload `0x86 = 04` | `C6` | source is recorder/config dependent; TX rule unknown |
| `000312` | `0x0BD = 03` | RX `01 02` payload `0xB8 = 00` | `08` | source is recorder/config dependent; TX rule unknown |
| `000357` | `0x086..0x087 = 00 00` | inserted after removal of RX `01 06` payload `0x6B..0x6C = 01 07` | `00 00` | placement/generation rule unknown |
| `000357` | `0x0D6..0x0D7 = 00 00` | inserted after removal of RX `01 06` payload `0xCB..0xCC = 03 02` | `03 02` | not derived by copying either session |
| `000378` | `0x00F..0x010 = 00 00` | follows removal of RX `01 06` payload `0xEB..0xEC = 01 13` across the page boundary | `00 00` | newly exposed by continuous alignment; rule unknown |
| `000378` | `0x067..0x068 = 00 00` | inserted after removal of RX `01 08` payload `0x5C..0x5D = 03 02` | `00 02` | rule unknown |
| `000378` | `0x097..0x098 = 00 00` | inserted after removal of RX `01 08` payload `0x74..0x75 = 01 12` | `00 0E` | rule unknown |
| `000423` | `0x0CD = 40` | RX `01 0C` payload `0xC8 = 00` after the global four-byte shift | `00` | one-byte modification; rule unknown |

The target values `02`, `FA`, `03`, the inserted zero pairs, and `40` cannot be
labelled constant or variable *between TX sessions*, because the working capture
contains no TX records. The table only establishes whether their corresponding
RX source fields remain constant or change between recorders.

## Protocol/configuration assessment

The working capture strengthens two conclusions:

1. Much of the write stream is constructed from the complete current-recorder
   RX data stream, not independently hard-coded per page.
2. Several source fields change with recorder/session configuration, while the
   Analyser applies unexplained replacements and relocations before realtime.

This still is not evidence that the operation is harmless or session-only. The
indexed `01 0D` through `01 12` acknowledgements, recorder identity/configuration
content, and transformed write-back remain consistent with a configuration
write operation. Neither capture contains a power-cycle/read-back experiment,
so persistence cannot be resolved.

No blocked write generator or hardware path was enabled from this comparison.
`RecorderConfigurationWriteBlocked` remains the runtime boundary. The earliest
unresolved target is still `000312` frame offset `0x009`; `000423` offset
`0x0CD = 40` independently prevents safe completion.

## Reproduction

Run the read-only comparison with:

```powershell
& .\tools\Compare-OtmrWorkingCapture.ps1 -WriteCsv
```

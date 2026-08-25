# Class 171 post-validation state and live-transition investigation

## Read-only recorder state

The 2026-08-25 state check opened COM2 at 38400/8N1, `Handshake.None`,
RTS LOW and DTR LOW. Its runtime allowlist accepted only the 11 existing
`OtmrLiveStartProtocol.SafeInterrogationWrites`. It sent no generated
configuration write, final live `01 07`, restoration write, or invented
command.

All six complete, checksum-valid pages were received. Pages `01 04`, `01 06`,
`01 08`, `01 0A`, and `01 0C` match the frozen pre-START pages exactly. Page
`01 02` has two differences:

| Recorder RX frame offset | Frozen | Current | Corresponding generated-write offset |
|---:|---:|---:|---:|
| `0x08F` | `04` | `01` | `0x08B` |
| `0x0C1` | `00` | `03` | `0x0BD` |

The four-byte offset difference is the already-proven page-1 construction
alignment. No automatic restoration was attempted. The recorder therefore is
not byte-for-byte equal to, and is not proven to be in, its frozen pre-START
state.

Raw evidence is in
`TestData/CLASS171_READ_ONLY_STATE_CHECK_20260825.txt`.

## What the original timestamps measured

The first validation recorded final `01 07` TX at `10:19:15.7992688`, the
service-level close-completion event at `10:19:15.9550441`, and reopen
completion at `10:19:15.9913919`. The close event was emitted only after
`SerialPort.Close()` returned, so it did not identify when Close was invoked.

An independent read-only close measured 110.8904 ms from immediately before
`DisconnectAsync` to its return. Subtracting the configured 17.6 ms delay from
the first validation's final-TX-to-close-completion interval leaves about
138.2 ms in transport shutdown. This establishes that `Task.Delay` is not the
source of that interval.

The complete working Arrowvale HHD session is important context: its final TX
was at `08:32:58.4824724`, Close Request at `08:32:58.5013189` (18.8465 ms),
and the next Create Request at `08:32:58.6275321` (126.2132 ms after Close).
Thus a roughly 110-140 ms close-to-reopen interval is also present in the
authoritative working session. The earlier 2026-08-24 trace showed a much
shorter interval, so close-to-create timing is not invariant across captures.

## Source of the synchronous close duration

There is no application read loop to cancel or join. Receive handling is a
`SerialPort.DataReceived` callback which checks `BytesToRead` and performs one
synchronous `SerialPort.Read` for only the available bytes. The transport
releases its `_sync` lock before closing. `SendAsync` completes its
`BaseStream.WriteAsync` and `FlushAsync` before the 17.6 ms delay starts.

Local disassembly of the exact Windows `System.IO.Ports` 8.0.0 assembly shows:

1. `SerialPort.Close()` calls `Component.Dispose()` synchronously.
2. `SerialPort.Dispose(true)` removes its data handler, flushes the internal
   `SerialStream`, and closes that stream.
3. Windows `SerialStream.Dispose(true)` sets the event-loop shutdown flag,
   calls `SetCommMask(0)`, clears DTR through `EscapeCommFunction`, flushes,
   signals the wait handle, discards the input and output buffers, synchronously
   awaits the `WaitCommEvent` task, closes the native handle, and disposes the
   thread-pool binding.

The observed extra interval is therefore inside synchronous
`SerialPort.Close()`/`SerialStream.Dispose`, not an application lock, sleep,
cancellation wait, or user read-loop join. Replacing that disposal machinery
with a raced background close or private-handle manipulation would be less safe,
and the working Arrowvale capture demonstrates a comparable 126.2 ms interval,
so no unsafe fast-close bypass was added.

## Changes for the next approved validation

High-resolution `Stopwatch` diagnostics now mark:

1. final `01 07` write begin;
2. final `01 07` write return;
3. 17.6 ms delay begin;
4. delay end;
5. immediately before `SerialPort.Close()`;
6. immediately after Close returns, including elapsed milliseconds;
7. reopen begin;
8. `SerialPort.Open()` return;
9. RTS application;
10. DTR application;
11. first received byte and first read size.

Separate markers cover the subsequent `SerialPort.Dispose()` call. Every entry
contains wall-clock time, `Stopwatch.GetTimestamp()`, and stopwatch frequency.

The serial transport now leaves RTS/DTR at their LOW defaults during Open and
applies requested RTS followed by DTR immediately after Open returns. This
matches the ordering visible in the HHD working trace, where Create/Open is
followed by CLR RTS and SET DTR. Protocol bytes, generators, CCF derivation,
SQLite/raw recording, and the RCM `LiveActive` gate are unchanged.

Expected timing for a future approved START is approximately 17.6-20 ms from
final-write begin/return to the Close invocation, followed by the driver's
observed roughly 110-140 ms synchronous close and immediate reopen begin. The
prior real run's Open then returned in 36.3 ms; Arrowvale's working Open returned
in 11.5 ms. The new diagnostics will distinguish all of these intervals rather
than treating Close completion as Close invocation.

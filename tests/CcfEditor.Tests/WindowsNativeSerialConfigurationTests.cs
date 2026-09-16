using System.Runtime.InteropServices;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.Tests;

public sealed class WindowsNativeSerialConfigurationTests
{
    [Fact]
    public void LiveDcbMatchesCaptured38400EightNOneRtsLowDtrHighState()
    {
        OtmrSerialSettings settings = OtmrSerialSettings.Class171Bench("COM2", dtrHigh: true);

        NativeDcb dcb = WindowsNativeSerialConfiguration.CreateDcb(settings, dtrEnabled: true);
        NativeSerialLineControl line = WindowsNativeSerialConfiguration.CreateLineControl(settings);

        Assert.Equal(28, Marshal.SizeOf<NativeDcb>());
        Assert.Equal(38400u, dcb.BaudRate);
        Assert.Equal(8, dcb.ByteSize);
        Assert.Equal(0, dcb.Parity);
        Assert.Equal(0, dcb.StopBits);
        Assert.Equal(
            WindowsNativeSerialConfiguration.BinaryFlag |
            WindowsNativeSerialConfiguration.DtrControlEnableFlag,
            dcb.Flags);
        Assert.Equal(0, dcb.XonLimit);
        Assert.Equal(0, dcb.XoffLimit);
        Assert.Equal(3, Marshal.SizeOf<NativeSerialLineControl>());
        Assert.Equal(8, line.WordLength);
        Assert.Equal(0, line.Parity);
        Assert.Equal(0, line.StopBits);
        WindowsNativeSerialConfiguration.VerifyDcb(dcb, settings);
    }

    [Fact]
    public void NativeSpecialCharactersAndHandflowMatchCapturedValues()
    {
        NativeSerialChars chars = WindowsNativeSerialConfiguration.CreateChars();
        NativeSerialHandflow handflow = WindowsNativeSerialConfiguration.CreateHandflow();

        Assert.Equal(6, Marshal.SizeOf<NativeSerialChars>());
        Assert.Equal(16, Marshal.SizeOf<NativeSerialHandflow>());
        WindowsNativeSerialConfiguration.VerifyChars(chars);
        Assert.Equal(1u, handflow.ControlHandshake);
        Assert.Equal(0u, handflow.FlowReplace);
        Assert.Equal(0, handflow.XonLimit);
        Assert.Equal(0, handflow.XoffLimit);
        WindowsNativeSerialConfiguration.VerifyHandflow(handflow);
    }

    [Fact]
    public void NativeTimeoutsMatchCapturedFiveHundredMillisecondState()
    {
        NativeCommTimeouts timeouts = WindowsNativeSerialConfiguration.CreateTimeouts();

        Assert.Equal(0u, timeouts.ReadIntervalTimeout);
        Assert.Equal(0u, timeouts.ReadTotalTimeoutMultiplier);
        Assert.Equal(500u, timeouts.ReadTotalTimeoutConstant);
        Assert.Equal(0u, timeouts.WriteTotalTimeoutMultiplier);
        Assert.Equal(500u, timeouts.WriteTotalTimeoutConstant);
        WindowsNativeSerialConfiguration.VerifyTimeouts(timeouts);
    }

    [Fact]
    public void VerificationRejectsAnyUncapturedFlowOrSpecialCharacterValue()
    {
        NativeSerialHandflow handflow = WindowsNativeSerialConfiguration.CreateHandflow();
        handflow.XoffLimit = 1;
        Assert.Throws<InvalidOperationException>(() =>
            WindowsNativeSerialConfiguration.VerifyHandflow(handflow));

        NativeSerialChars chars = WindowsNativeSerialConfiguration.CreateChars();
        chars.BreakChar = 1;
        Assert.Throws<InvalidOperationException>(() =>
            WindowsNativeSerialConfiguration.VerifyChars(chars));
    }

    [Fact]
    public async Task NativeReadPump_ForwardsExactChunksPreservesInitialBytesAndRearmsAfterZeroCompletion()
    {
        using var operation = new FakeOverlappedReadOperation(
            FakeReadCompletion.Immediate(0xFF),
            FakeReadCompletion.Pending(0xFB, 0xFB),
            FakeReadCompletion.Immediate(),
            FakeReadCompletion.Pending(0x31, 0x32, 0x33));
        var diagnostics = new List<(string Stage, string Detail)>();
        var received = new List<byte[]>();
        var errors = new List<Exception>();
        var pump = new NativeOverlappedReadPump(
            operation,
            (stage, detail) => { lock (diagnostics) diagnostics.Add((stage, detail)); },
            bytes => { lock (received) received.Add(bytes); },
            error => { lock (errors) errors.Add(error); });
        using var cancellation = new CancellationTokenSource();

        Task loop = pump.RunAsync(cancellation.Token);
        await pump.FirstReadSubmitted;
        await WaitForAsync(() => operation.SubmitCount >= 5);
        cancellation.Cancel();
        operation.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            new[] { new byte[] { 0xFF }, new byte[] { 0xFB, 0xFB }, new byte[] { 0x31, 0x32, 0x33 } },
            received,
            ByteArrayComparer.Instance);
        Assert.Empty(errors);
        Assert.Equal(5, operation.SubmitCount);
        Assert.Equal(4, diagnostics.Count(item => item.Stage == "NATIVE_READ_COMPLETE"));
        Assert.Equal(4, diagnostics.Count(item => item.Stage == "NATIVE_READ_REARM"));
        Assert.Contains(diagnostics, item =>
            item.Stage == "NATIVE_READ_COMPLETE" && item.Detail.Contains("bytesTransferred=0", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item =>
            item.Stage == "NATIVE_READFILE_RETURNED" && item.Detail.Contains("bytesTransferred=0", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item =>
            item.Stage == "NATIVE_READ_COMPLETE" &&
            item.Detail.Contains("first=0xFF", StringComparison.Ordinal) &&
            item.Detail.Contains("last=0xFF", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Stage == "NATIVE_READ_PENDING");
        Assert.Contains(diagnostics, item => item.Stage == "NATIVE_READFILE_PENDING");
        Assert.Contains(diagnostics, item =>
            item.Stage == "NATIVE_RX_CHUNK" && item.Detail.Contains("hex=FF", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Stage == "NATIVE_READ_CANCEL");
        NativeLiveReceiveStatistics statistics = pump.GetStatistics();
        Assert.Equal(5, statistics.ReadSubmissions);
        Assert.Equal(1, statistics.ZeroByteCompletions);
        Assert.Equal(6, statistics.BytesReceived);
        Assert.NotEqual(0, statistics.FirstNonZeroReadTimestamp);
    }

    [Fact]
    public async Task NativeReadPump_CancellationAbortsOutstandingReadWithoutReportingTransportError()
    {
        using var operation = new FakeOverlappedReadOperation();
        var diagnostics = new List<string>();
        var errors = new List<Exception>();
        var pump = new NativeOverlappedReadPump(
            operation,
            (stage, _) => { lock (diagnostics) diagnostics.Add(stage); },
            _ => throw new InvalidOperationException("A cancelled pending read must not deliver bytes."),
            error => { lock (errors) errors.Add(error); });
        using var cancellation = new CancellationTokenSource();

        Task loop = pump.RunAsync(cancellation.Token);
        await pump.FirstReadSubmitted;
        cancellation.Cancel();
        operation.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, operation.CancelCount);
        Assert.Empty(errors);
        Assert.Contains("NATIVE_READ_CANCEL", diagnostics);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }

    private readonly record struct FakeReadCompletion(byte[] Bytes, bool CompletesPending)
    {
        internal static FakeReadCompletion Immediate(params byte[] bytes) => new(bytes, false);
        internal static FakeReadCompletion Pending(params byte[] bytes) => new(bytes, true);
    }

    private sealed class FakeOverlappedReadOperation : INativeOverlappedReadOperation
    {
        private readonly object _sync = new();
        private readonly Queue<FakeReadCompletion> _completions;
        private readonly ManualResetEventSlim _cancelled = new(false);
        private FakeReadCompletion _current;
        private bool _waitForCancellation;
        private bool _disposed;
        private int _submitCount;
        private int _cancelCount;

        internal FakeOverlappedReadOperation(params FakeReadCompletion[] completions) =>
            _completions = new Queue<FakeReadCompletion>(completions);

        internal int SubmitCount => Volatile.Read(ref _submitCount);
        internal int CancelCount => Volatile.Read(ref _cancelCount);

        public NativeReadSubmission Submit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Interlocked.Increment(ref _submitCount);
            lock (_sync)
            {
                if (_completions.Count == 0)
                {
                    _current = FakeReadCompletion.Pending();
                    _waitForCancellation = true;
                    return NativeReadSubmission.Pending;
                }

                _current = _completions.Dequeue();
                _waitForCancellation = false;
                return _current.CompletesPending
                    ? NativeReadSubmission.Pending
                    : NativeReadSubmission.Completed(checked((uint)_current.Bytes.Length));
            }
        }

        public uint WaitForPendingCompletion()
        {
            bool waitForCancellation;
            lock (_sync)
                waitForCancellation = _waitForCancellation;
            if (waitForCancellation)
            {
                _cancelled.Wait();
                throw new OperationCanceledException("Fake pending native read cancelled.");
            }

            lock (_sync)
                return checked((uint)_current.Bytes.Length);
        }

        public byte[] CopyBytes(uint bytesTransferred)
        {
            lock (_sync)
                return _current.Bytes.AsSpan(0, checked((int)bytesTransferred)).ToArray();
        }

        public void Cancel()
        {
            Interlocked.Increment(ref _cancelCount);
            _cancelled.Set();
        }

        public void Dispose()
        {
            _disposed = true;
            _cancelled.Dispose();
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        internal static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? x, byte[]? y) =>
            x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Aggregate(17, (hash, value) => hash * 31 + value);
    }
}

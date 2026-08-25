using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CcfEditor.Otmr.Transport;

internal sealed class WindowsNativeLiveSerialPort : IAsyncDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PurgeAll = 0x0001 | 0x0002 | 0x0004 | 0x0008;
    private const uint ClearRts = 4;
    private const uint SetDtr = 5;
    private const uint IoctlSerialSetBaudRate = 0x001B0004;
    private const uint IoctlSerialSetLineControl = 0x001B000C;
    private const uint IoctlSerialGetChars = 0x001B0058;
    private const uint IoctlSerialSetChars = 0x001B005C;
    private const uint IoctlSerialGetHandflow = 0x001B0060;
    private const uint IoctlSerialSetHandflow = 0x001B0064;

    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _firstReadSubmitted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string, string> _diagnostic;
    private readonly Action<byte[]> _received;
    private readonly Action<Exception> _error;
    private readonly Task _readLoop;
    private int _firstRxReported;
    private int _disposed;

    private WindowsNativeLiveSerialPort(
        SafeFileHandle handle,
        FileStream stream,
        Action<string, string> diagnostic,
        Action<byte[]> received,
        Action<Exception> error)
    {
        _handle = handle;
        _stream = stream;
        _diagnostic = diagnostic;
        _received = received;
        _error = error;
        _readLoop = ReadLoopAsync(_readCancellation.Token);
    }

    public bool IsOpen => Volatile.Read(ref _disposed) == 0 && !_handle.IsInvalid && !_handle.IsClosed;

    public static async Task<WindowsNativeLiveSerialPort> OpenAsync(
        OtmrSerialSettings settings,
        Action<string, string> diagnostic,
        Action<byte[]> received,
        Action<Exception> error,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The captured Class 171 live reopen requires Windows serial APIs.");
        if (!settings.DtrEnable || settings.RtsEnable)
            throw new ArgumentException("Native live reopen requires RTS LOW and DTR HIGH.", nameof(settings));

        cancellationToken.ThrowIfCancellationRequested();
        diagnostic("NATIVE_CREATEFILE_BEGIN", settings.PortName);
        SafeFileHandle handle = NativeMethods.CreateFile(
            @"\\.\" + settings.PortName,
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int errorCode = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(errorCode, $"CreateFile failed for {settings.PortName}.");
        }
        diagnostic("NATIVE_CREATEFILE_RETURNED", settings.PortName);

        try
        {
            ConfigureAndVerify(handle, settings, diagnostic);
            var stream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true);
            var port = new WindowsNativeLiveSerialPort(handle, stream, diagnostic, received, error);
            await port._firstReadSubmitted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return port;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _diagnostic("NATIVE_READ_CANCEL_BEGIN", "CancelIoEx for the outstanding overlapped ReadFile");
        _readCancellation.Cancel();
        if (!NativeMethods.CancelIoEx(_handle, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1168) // ERROR_NOT_FOUND: no pending I/O remains.
                _diagnostic("NATIVE_READ_CANCEL_RESULT", $"CancelIoEx error {error}");
        }
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _diagnostic("NATIVE_READ_LOOP_STOPPED", "No read operation remains pending");
        await _stream.DisposeAsync().ConfigureAwait(false);
        _readCancellation.Dispose();
        _writeLock.Dispose();
        _diagnostic("NATIVE_HANDLE_CLOSED", "Orderly FileStream/SafeFileHandle close completed");
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ValueTask<int> readOperation = _stream.ReadAsync(buffer, cancellationToken);
                _diagnostic("NATIVE_READFILE_PENDING", "Overlapped ReadFile submitted for 4096 bytes; timeout constant 500 ms");
                _firstReadSubmitted.TrySetResult();
                int read = await readOperation.ConfigureAwait(false);
                if (read == 0)
                    continue;

                byte[] exact = buffer.AsSpan(0, read).ToArray();
                if (Interlocked.Exchange(ref _firstRxReported, 1) == 0)
                    _diagnostic("FIRST_RX_BYTE", $"0x{exact[0]:X2}; native ReadFile returned {read} byte(s)");
                _received(exact);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (Volatile.Read(ref _disposed) == 0)
        {
            _firstReadSubmitted.TrySetException(ex);
            _error(ex);
        }
    }

    private static void ConfigureAndVerify(
        SafeFileHandle handle,
        OtmrSerialSettings settings,
        Action<string, string> diagnostic)
    {
        InvokeIoctlSet(handle, IoctlSerialSetBaudRate,
            new NativeSerialBaudRate { BaudRate = checked((uint)settings.BaudRate) },
            "IOCTL_SERIAL_SET_BAUD_RATE");
        diagnostic("NATIVE_BAUD_APPLIED", settings.BaudRate.ToString());

        Check(NativeMethods.EscapeCommFunction(handle, ClearRts), "EscapeCommFunction(CLRRTS)");
        diagnostic("NATIVE_RTS_APPLIED", "LOW via EscapeCommFunction(CLRRTS)");
        Check(NativeMethods.EscapeCommFunction(handle, SetDtr), "EscapeCommFunction(SETDTR)");
        diagnostic("NATIVE_DTR_APPLIED", "HIGH via EscapeCommFunction(SETDTR)");

        NativeSerialLineControl line = WindowsNativeSerialConfiguration.CreateLineControl(settings);
        InvokeIoctlSet(handle, IoctlSerialSetLineControl, line, "IOCTL_SERIAL_SET_LINE_CONTROL");
        diagnostic("NATIVE_LINE_CONTROL_APPLIED", WindowsNativeSerialConfiguration.FormatLineControl(line));

        NativeSerialChars chars = WindowsNativeSerialConfiguration.CreateChars();
        InvokeIoctlSet(handle, IoctlSerialSetChars, chars, "IOCTL_SERIAL_SET_CHARS");
        diagnostic("NATIVE_SPECIAL_CHARS_APPLIED", WindowsNativeSerialConfiguration.FormatChars(chars));

        NativeSerialHandflow handflow = WindowsNativeSerialConfiguration.CreateHandflow();
        InvokeIoctlSet(handle, IoctlSerialSetHandflow, handflow, "IOCTL_SERIAL_SET_HANDFLOW");
        diagnostic("NATIVE_HANDFLOW_APPLIED", WindowsNativeSerialConfiguration.FormatHandflow(handflow));

        Check(NativeMethods.SetupComm(handle, 4096, 4096), "SetupComm(4096,4096)");
        diagnostic("NATIVE_QUEUE_CONFIGURED", "input=4096 output=4096; SetupComm accepted (driver allocation is advisory)");

        Check(NativeMethods.PurgeComm(handle, PurgeAll), "PurgeComm(all)");
        diagnostic("NATIVE_PURGE_COMPLETED", "TXABORT|RXABORT|TXCLEAR|RXCLEAR");

        NativeCommTimeouts timeouts = WindowsNativeSerialConfiguration.CreateTimeouts();
        Check(NativeMethods.SetCommTimeouts(handle, ref timeouts), "SetCommTimeouts");
        diagnostic("NATIVE_TIMEOUTS_APPLIED", WindowsNativeSerialConfiguration.FormatTimeouts(timeouts));

        var actualDcb = new NativeDcb { DcbLength = checked((uint)Marshal.SizeOf<NativeDcb>()) };
        Check(NativeMethods.GetCommState(handle, ref actualDcb), "GetCommState(verify)");
        WindowsNativeSerialConfiguration.VerifyDcb(actualDcb, settings);
        diagnostic("NATIVE_DCB_VERIFIED", WindowsNativeSerialConfiguration.FormatDcb(actualDcb));

        NativeSerialChars actualChars = InvokeIoctlGet<NativeSerialChars>(
            handle, IoctlSerialGetChars, "IOCTL_SERIAL_GET_CHARS");
        WindowsNativeSerialConfiguration.VerifyChars(actualChars);
        diagnostic("NATIVE_SPECIAL_CHARS_VERIFIED", WindowsNativeSerialConfiguration.FormatChars(actualChars));

        NativeSerialHandflow actualHandflow = InvokeIoctlGet<NativeSerialHandflow>(
            handle, IoctlSerialGetHandflow, "IOCTL_SERIAL_GET_HANDFLOW");
        WindowsNativeSerialConfiguration.VerifyHandflow(actualHandflow);
        diagnostic("NATIVE_HANDFLOW_VERIFIED", WindowsNativeSerialConfiguration.FormatHandflow(actualHandflow));

        Check(NativeMethods.GetCommTimeouts(handle, out NativeCommTimeouts actualTimeouts), "GetCommTimeouts(verify)");
        WindowsNativeSerialConfiguration.VerifyTimeouts(actualTimeouts);
        diagnostic("NATIVE_TIMEOUTS_VERIFIED", WindowsNativeSerialConfiguration.FormatTimeouts(actualTimeouts));
        diagnostic("NATIVE_REOPEN_CONFIGURATION_COMPLETE", "38400/8N1; RTS LOW; DTR HIGH; exact DCB; 4096/4096 requested; purge complete; exact 500 ms timeouts");
    }

    private static void InvokeIoctlSet<T>(SafeFileHandle handle, uint controlCode, T value, string operation)
        where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr input = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, input, fDeleteOld: false);
            InvokeIoctl(handle, controlCode, input, checked((uint)size), IntPtr.Zero, 0, operation);
        }
        finally
        {
            Marshal.FreeHGlobal(input);
        }
    }

    private static T InvokeIoctlGet<T>(SafeFileHandle handle, uint controlCode, string operation)
        where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr output = Marshal.AllocHGlobal(size);
        try
        {
            InvokeIoctl(handle, controlCode, IntPtr.Zero, 0, output, checked((uint)size), operation);
            return Marshal.PtrToStructure<T>(output);
        }
        finally
        {
            Marshal.FreeHGlobal(output);
        }
    }

    private static void InvokeIoctl(
        SafeFileHandle handle,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        IntPtr output,
        uint outputSize,
        string operation)
    {
        using SafeWaitHandle completionEvent = NativeMethods.CreateEvent(
            IntPtr.Zero, manualReset: true, initialState: false, name: null);
        if (completionEvent.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " CreateEvent failed.");

        int overlappedSize = Marshal.SizeOf<NativeOverlappedData>();
        IntPtr overlapped = Marshal.AllocHGlobal(overlappedSize);
        try
        {
            Marshal.StructureToPtr(new NativeOverlappedData { EventHandle = completionEvent.DangerousGetHandle() },
                overlapped, fDeleteOld: false);
            bool completed = NativeMethods.DeviceIoControl(
                handle, controlCode, input, inputSize, output, outputSize, out _, overlapped);
            if (completed)
                return;

            int error = Marshal.GetLastWin32Error();
            if (error != 997) // ERROR_IO_PENDING
                throw new Win32Exception(error, operation + " failed.");

            uint wait = NativeMethods.WaitForSingleObject(completionEvent, 5000);
            if (wait != 0)
            {
                NativeMethods.CancelIoEx(handle, overlapped);
                throw new TimeoutException(operation + " did not complete within 5000 ms.");
            }
            Check(NativeMethods.GetOverlappedResult(handle, overlapped, out _, wait: false), operation);
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
        }
    }

    private static void Check(bool success, string operation)
    {
        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed.");
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCommState(SafeFileHandle file, ref NativeDcb dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupComm(SafeFileHandle file, uint inputQueue, uint outputQueue);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PurgeComm(SafeFileHandle file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EscapeCommFunction(SafeFileHandle file, uint function);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetCommTimeouts(SafeFileHandle file, ref NativeCommTimeouts timeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCommTimeouts(SafeFileHandle file, out NativeCommTimeouts timeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle file, uint controlCode, IntPtr input, uint inputSize,
            IntPtr output, uint outputSize, out uint bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeWaitHandle CreateEvent(
            IntPtr eventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool manualReset,
            [MarshalAs(UnmanagedType.Bool)] bool initialState,
            string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetOverlappedResult(
            SafeFileHandle file, IntPtr overlapped, out uint bytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool wait);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDcb
{
    internal uint DcbLength;
    internal uint BaudRate;
    internal uint Flags;
    internal ushort Reserved;
    internal ushort XonLimit;
    internal ushort XoffLimit;
    internal byte ByteSize;
    internal byte Parity;
    internal byte StopBits;
    internal byte XonChar;
    internal byte XoffChar;
    internal byte ErrorChar;
    internal byte EofChar;
    internal byte EventChar;
    internal ushort Reserved1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCommTimeouts
{
    internal uint ReadIntervalTimeout;
    internal uint ReadTotalTimeoutMultiplier;
    internal uint ReadTotalTimeoutConstant;
    internal uint WriteTotalTimeoutMultiplier;
    internal uint WriteTotalTimeoutConstant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeOverlappedData
{
    internal IntPtr Internal;
    internal IntPtr InternalHigh;
    internal uint Offset;
    internal uint OffsetHigh;
    internal IntPtr EventHandle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSerialBaudRate
{
    internal uint BaudRate;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct NativeSerialLineControl
{
    internal byte StopBits;
    internal byte Parity;
    internal byte WordLength;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct NativeSerialChars
{
    internal byte EofChar;
    internal byte ErrorChar;
    internal byte BreakChar;
    internal byte EventChar;
    internal byte XonChar;
    internal byte XoffChar;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSerialHandflow
{
    internal uint ControlHandshake;
    internal uint FlowReplace;
    internal int XonLimit;
    internal int XoffLimit;
}

internal static class WindowsNativeSerialConfiguration
{
    internal const uint BinaryFlag = 0x0001;
    internal const uint DtrControlEnableFlag = 0x0010;

    internal static NativeDcb CreateDcb(OtmrSerialSettings settings, bool dtrEnabled) => new()
    {
        DcbLength = checked((uint)Marshal.SizeOf<NativeDcb>()),
        BaudRate = checked((uint)settings.BaudRate),
        Flags = BinaryFlag | (dtrEnabled ? DtrControlEnableFlag : 0u),
        Reserved = 0,
        XonLimit = 0,
        XoffLimit = 0,
        ByteSize = checked((byte)settings.DataBits),
        Parity = settings.Parity switch
        {
            Parity.None => 0,
            Parity.Odd => 1,
            Parity.Even => 2,
            Parity.Mark => 3,
            Parity.Space => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        },
        StopBits = settings.StopBits switch
        {
            StopBits.One => 0,
            StopBits.OnePointFive => 1,
            StopBits.Two => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        },
        XonChar = 0,
        XoffChar = 0,
        ErrorChar = 0,
        EofChar = 0,
        EventChar = 0,
        Reserved1 = 0
    };

    internal static NativeCommTimeouts CreateTimeouts() => new()
    {
        ReadIntervalTimeout = 0,
        ReadTotalTimeoutMultiplier = 0,
        ReadTotalTimeoutConstant = 500,
        WriteTotalTimeoutMultiplier = 0,
        WriteTotalTimeoutConstant = 500
    };

    internal static NativeSerialLineControl CreateLineControl(OtmrSerialSettings settings) => new()
    {
        StopBits = settings.StopBits switch
        {
            StopBits.One => 0,
            StopBits.OnePointFive => 1,
            StopBits.Two => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        },
        Parity = settings.Parity switch
        {
            Parity.None => 0,
            Parity.Odd => 1,
            Parity.Even => 2,
            Parity.Mark => 3,
            Parity.Space => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        },
        WordLength = checked((byte)settings.DataBits)
    };

    internal static NativeSerialChars CreateChars() => new();

    internal static NativeSerialHandflow CreateHandflow() => new()
    {
        ControlHandshake = 1,
        FlowReplace = 0,
        XonLimit = 0,
        XoffLimit = 0
    };

    internal static void VerifyDcb(NativeDcb actual, OtmrSerialSettings settings)
    {
        NativeDcb expected = CreateDcb(settings, dtrEnabled: true);
        if (actual.BaudRate != expected.BaudRate || actual.Flags != expected.Flags ||
            actual.XonLimit != 0 || actual.XoffLimit != 0 || actual.ByteSize != expected.ByteSize ||
            actual.Parity != expected.Parity || actual.StopBits != expected.StopBits ||
            actual.XonChar != 0 || actual.XoffChar != 0 || actual.ErrorChar != 0 ||
            actual.EofChar != 0 || actual.EventChar != 0)
        {
            throw new InvalidOperationException("The serial driver did not retain the exact captured live DCB state. Actual: " + FormatDcb(actual));
        }
    }

    internal static void VerifyTimeouts(NativeCommTimeouts actual)
    {
        NativeCommTimeouts expected = CreateTimeouts();
        if (actual.ReadIntervalTimeout != expected.ReadIntervalTimeout ||
            actual.ReadTotalTimeoutMultiplier != expected.ReadTotalTimeoutMultiplier ||
            actual.ReadTotalTimeoutConstant != expected.ReadTotalTimeoutConstant ||
            actual.WriteTotalTimeoutMultiplier != expected.WriteTotalTimeoutMultiplier ||
            actual.WriteTotalTimeoutConstant != expected.WriteTotalTimeoutConstant)
            throw new InvalidOperationException("The serial driver did not retain the exact captured live timeout state.");
    }

    internal static void VerifyChars(NativeSerialChars actual)
    {
        if (actual.EofChar != 0 || actual.ErrorChar != 0 || actual.BreakChar != 0 ||
            actual.EventChar != 0 || actual.XonChar != 0 || actual.XoffChar != 0)
            throw new InvalidOperationException("The serial driver did not retain the captured all-zero special-character state.");
    }

    internal static void VerifyHandflow(NativeSerialHandflow actual)
    {
        if (actual.ControlHandshake != 1 || actual.FlowReplace != 0 ||
            actual.XonLimit != 0 || actual.XoffLimit != 0)
            throw new InvalidOperationException("The serial driver did not retain the exact captured handflow state.");
    }

    internal static string FormatDcb(NativeDcb dcb) =>
        $"baud={dcb.BaudRate}; byteSize={dcb.ByteSize}; parity={dcb.Parity}; stopBits={dcb.StopBits}; " +
        $"ControlHandShake={((dcb.Flags & DtrControlEnableFlag) != 0 ? 1 : 0)}; " +
        $"FlowReplace=0x{((dcb.Flags >> 8) & 0x3F):X}; RTS={((dcb.Flags & 0x3000) == 0 ? "LOW" : "CONTROLLED")}; " +
        $"DTR={((dcb.Flags & DtrControlEnableFlag) != 0 ? "HIGH" : "LOW")}; XonLimit={dcb.XonLimit}; XoffLimit={dcb.XoffLimit}; " +
        $"chars={dcb.XonChar:X2}/{dcb.XoffChar:X2}/{dcb.ErrorChar:X2}/{dcb.EofChar:X2}/{dcb.EventChar:X2}; flags=0x{dcb.Flags:X8}";

    internal static string FormatTimeouts(NativeCommTimeouts value) =>
        $"read interval={value.ReadIntervalTimeout} multiplier={value.ReadTotalTimeoutMultiplier} constant={value.ReadTotalTimeoutConstant} ms; " +
        $"write multiplier={value.WriteTotalTimeoutMultiplier} constant={value.WriteTotalTimeoutConstant} ms";

    internal static string FormatLineControl(NativeSerialLineControl value) =>
        $"wordLength={value.WordLength}; parity={value.Parity}; stopBits={value.StopBits}";

    internal static string FormatChars(NativeSerialChars value) =>
        $"EOF={value.EofChar:X2}; Error={value.ErrorChar:X2}; Break={value.BreakChar:X2}; " +
        $"Event={value.EventChar:X2}; XON={value.XonChar:X2}; XOFF={value.XoffChar:X2}";

    internal static string FormatHandflow(NativeSerialHandflow value) =>
        $"ControlHandShake={value.ControlHandshake}; FlowReplace={value.FlowReplace}; " +
        $"XonLimit={value.XonLimit}; XoffLimit={value.XoffLimit}";
}

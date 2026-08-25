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
}

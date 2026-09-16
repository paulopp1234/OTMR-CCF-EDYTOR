using CcfEditor.Core;

namespace CcfEditor.Otmr.Live;

/// <summary>
/// Protocol rules proven across the 2026-08-24 HHD TX/RX capture. These
/// helpers construct bytes only; the live service does not transmit the
/// blocked 01 0C acknowledgement or any following data write.
/// </summary>
internal static class OtmrProtocolDerivation
{
    public static byte ComputePayloadCheckByte(ReadOnlySpan<byte> payload)
    {
        int sum = 0;
        foreach (byte value in payload)
            sum = (sum + value) & 0xFF;
        return (byte)sum;
    }

    public static bool HasValidPayloadCheckByte(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 12 || frame[0] != 0x01 || frame[8] != 0x02 ||
            frame[^3] != 0x03 || frame[^1] != 0x04)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = frame.Slice(9, frame.Length - 12);
        return frame[^2] == ComputePayloadCheckByte(payload);
    }

    public static byte[] CreateReadDataAcknowledgement(ReadOnlySpan<byte> receivedDataFrame)
    {
        if (receivedDataFrame.Length < 13)
            throw new ArgumentException("A complete OTMR data frame is required.", nameof(receivedDataFrame));

        byte transaction = receivedDataFrame[1];
        if (transaction is not (0x02 or 0x04 or 0x06 or 0x08 or 0x0A or 0x0C) ||
            !OtmrLiveStartProtocol.IsExpectedReply(transaction, receivedDataFrame) ||
            !HasValidPayloadCheckByte(receivedDataFrame))
        {
            throw new ArgumentException("The input is not a complete captured-form OTMR read-data frame.", nameof(receivedDataFrame));
        }

        byte[] acknowledgement =
        {
            0x01,
            transaction,
            0x00,
            0x01,
            0x01,
            receivedDataFrame[4],
            0x02,
            0x01,
            0x02,
            transaction,
            0x03,
            0x00,
            0x04
        };
        acknowledgement[^2] = ComputePayloadCheckByte(acknowledgement.AsSpan(9, 1));
        return acknowledgement;
    }

    /// <summary>
    /// Generates only the captured page-5 transformation, whose complete
    /// payload is proven to be an unchanged copy of the current recorder's
    /// 01 0A data page. This helper is not used by the live service.
    /// </summary>
    public static byte[] CreateUnmodifiedConfigurationPage05Write(ReadOnlySpan<byte> receivedDataFrame)
    {
        if (!OtmrLiveStartProtocol.IsExpectedReply(0x0A, receivedDataFrame) ||
            !HasValidPayloadCheckByte(receivedDataFrame))
        {
            throw new ArgumentException(
                "The input is not a complete captured-form current-recorder 01 0A page.",
                nameof(receivedDataFrame));
        }

        byte[] write = receivedDataFrame.ToArray();
        write[1] = receivedDataFrame[3];
        write[^2] = ComputePayloadCheckByte(write.AsSpan(9, write.Length - 12));
        return write;
    }

    /// <summary>
    /// Reconstructs the captured START page-1 write from the current recorder's
    /// first two read pages and the currently selected CCF. This helper only
    /// constructs bytes; the live service deliberately does not transmit the
    /// indexed configuration-write sequence.
    /// </summary>
    public static byte[] CreateConfigurationPage01WriteFromCcf(
        ReadOnlySpan<byte> receivedPage01,
        ReadOnlySpan<byte> receivedPage02,
        ReadOnlySpan<byte> selectedCcf)
    {
        if (!OtmrLiveStartProtocol.IsExpectedReply(0x02, receivedPage01) ||
            !HasValidPayloadCheckByte(receivedPage01))
        {
            throw new ArgumentException(
                "The first input is not a complete captured-form current-recorder 01 02 page.",
                nameof(receivedPage01));
        }
        if (!OtmrLiveStartProtocol.IsExpectedReply(0x04, receivedPage02) ||
            !HasValidPayloadCheckByte(receivedPage02))
        {
            throw new ArgumentException(
                "The second input is not a complete captured-form current-recorder 01 04 page.",
                nameof(receivedPage02));
        }
        if (selectedCcf.Length != CcfConstants.FileSize)
        {
            throw new ArgumentException(
                $"The selected CCF must contain exactly {CcfConstants.FileSize} bytes.",
                nameof(selectedCcf));
        }

        return CreateConfigurationPage01Write(
            receivedPage01,
            receivedPage02,
            selectedCcf[CcfFieldDefinitions.Header.Unknown0231],
            selectedCcf[CcfFieldDefinitions.Header.Unknown0263]);
    }

    /// <summary>
    /// Encodes the two phase-selected page-1 logical values. Across the two
    /// authoritative START sessions and the captured STOP restoration, the
    /// wire values are respectively FE-value and 03-value modulo 256.
    /// </summary>
    internal static byte[] CreateConfigurationPage01Write(
        ReadOnlySpan<byte> receivedPage01,
        ReadOnlySpan<byte> receivedPage02,
        byte logicalValue0231,
        byte logicalValue0263)
    {
        ReadOnlySpan<byte> source01 = receivedPage01.Slice(9, receivedPage01.Length - 12);
        ReadOnlySpan<byte> source02 = receivedPage02.Slice(9, receivedPage02.Length - 12);
        if (source01.Length != 0xFF || source02.Length < 4)
            throw new ArgumentException("Captured page-1 construction requires a 255-byte page and four following bytes.");

        byte[] write = receivedPage01.ToArray();
        write[1] = receivedPage01[3];
        Span<byte> payload = write.AsSpan(9, 0xFF);
        payload[0] = 0x02;
        source01[5..0x86].CopyTo(payload[1..0x82]);
        payload[0x82] = unchecked((byte)(0xFE - logicalValue0231));
        source01[0x87..0xB8].CopyTo(payload[0x83..0xB4]);
        payload[0xB4] = unchecked((byte)(0x03 - logicalValue0263));
        source01[0xB9..].CopyTo(payload[0xB5..0xFB]);
        source02[..4].CopyTo(payload[0xFB..]);
        write[^2] = ComputePayloadCheckByte(payload);
        return write;
    }
}

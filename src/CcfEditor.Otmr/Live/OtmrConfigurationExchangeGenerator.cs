using CcfEditor.Core;

namespace CcfEditor.Otmr.Live;

internal enum OtmrConfigurationByteSource
{
    ProtocolConstant,
    RecorderRx,
    SelectedCcf,
    CalculatedFieldOrChecksum
}

internal sealed record OtmrConfigurationByteProvenance(
    int FrameOffset,
    byte Value,
    OtmrConfigurationByteSource Source,
    string SourceReference);

internal sealed record OtmrGeneratedConfigurationWrite(
    string CaptureEvent,
    byte[] Bytes,
    IReadOnlyList<OtmrConfigurationByteProvenance> Provenance);

internal sealed record OtmrGeneratedConfigurationExchange(
    IReadOnlyList<OtmrGeneratedConfigurationWrite> Writes);

/// <summary>
/// In-memory reconstruction of the seven captured configuration-stage writes.
/// Nothing in this type has access to an OTMR transport.
/// </summary>
internal static class OtmrConfigurationExchangeGenerator
{
    private static readonly byte[] DataTransactions = { 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C };
    private static readonly string[] CaptureEvents =
        { "000312", "000335", "000357", "000378", "000401", "000423" };
    private static readonly int[] TransmitPayloadLengths =
        { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xC6 };

    public static OtmrGeneratedConfigurationExchange CreateStart(
        IReadOnlyDictionary<byte, byte[]> recorderReplies,
        ReadOnlySpan<byte> selectedCcf)
    {
        ArgumentNullException.ThrowIfNull(recorderReplies);
        if (selectedCcf.Length != CcfConstants.FileSize)
        {
            throw new ArgumentException(
                $"The selected CCF must contain exactly {CcfConstants.FileSize} bytes.",
                nameof(selectedCcf));
        }

        return Create(
            recorderReplies,
            selectedCcf[CcfFieldDefinitions.Header.Unknown0231],
            selectedCcf[CcfFieldDefinitions.Header.Unknown0263],
            useSelectedCcf: true);
    }

    public static OtmrGeneratedConfigurationExchange CreateStopRestoration(
        IReadOnlyDictionary<byte, byte[]> recorderReplies)
    {
        ArgumentNullException.ThrowIfNull(recorderReplies);
        byte[] page01 = GetValidatedReply(recorderReplies, 0x02);
        ReadOnlySpan<byte> payload = GetPayload(page01);
        return Create(
            recorderReplies,
            payload[0x86],
            payload[0xB8],
            useSelectedCcf: false);
    }

    private static OtmrGeneratedConfigurationExchange Create(
        IReadOnlyDictionary<byte, byte[]> recorderReplies,
        byte logicalValue0231,
        byte logicalValue0263,
        bool useSelectedCcf)
    {
        byte[][] replies = DataTransactions
            .Select(transaction => GetValidatedReply(recorderReplies, transaction))
            .ToArray();
        SourceByte[] recorderStream = CreateRecorderStream(replies);
        List<SourceByte> transmitStream = TransformPayloadStream(
            recorderStream,
            logicalValue0231,
            logicalValue0263,
            useSelectedCcf);

        if (transmitStream.Count != TransmitPayloadLengths.Sum())
            throw new InvalidDataException("The proven six-page transformation produced an unexpected length.");

        var writes = new List<OtmrGeneratedConfigurationWrite>(7)
        {
            CreateAcknowledgement(replies[^1])
        };

        int streamOffset = 0;
        for (int page = 0; page < replies.Length; page++)
        {
            int payloadLength = TransmitPayloadLengths[page];
            writes.Add(CreatePageWrite(
                CaptureEvents[page],
                replies[page],
                transmitStream.GetRange(streamOffset, payloadLength)));
            streamOffset += payloadLength;
        }

        return new OtmrGeneratedConfigurationExchange(writes);
    }

    private static byte[] GetValidatedReply(
        IReadOnlyDictionary<byte, byte[]> recorderReplies,
        byte transaction)
    {
        if (!recorderReplies.TryGetValue(transaction, out byte[]? reply) ||
            !OtmrLiveStartProtocol.IsExpectedReply(transaction, reply) ||
            !OtmrProtocolDerivation.HasValidPayloadCheckByte(reply))
        {
            throw new ArgumentException(
                $"A complete valid current-recorder 01 {transaction:X2} data page is required.",
                nameof(recorderReplies));
        }
        return reply;
    }

    private static SourceByte[] CreateRecorderStream(IReadOnlyList<byte[]> replies)
    {
        var stream = new List<SourceByte>(0x5C5);
        for (int page = 0; page < replies.Count; page++)
        {
            ReadOnlySpan<byte> payload = GetPayload(replies[page]);
            byte transaction = DataTransactions[page];
            for (int offset = 0; offset < payload.Length; offset++)
            {
                stream.Add(new SourceByte(
                    payload[offset],
                    OtmrConfigurationByteSource.RecorderRx,
                    $"RX 01 {transaction:X2} payload 0x{offset:X2}"));
            }
        }
        return stream.ToArray();
    }

    private static List<SourceByte> TransformPayloadStream(
        SourceByte[] source,
        byte logicalValue0231,
        byte logicalValue0263,
        bool useSelectedCcf)
    {
        const int pageLength = 0xFF;
        int page3 = 2 * pageLength;
        int page4 = 3 * pageLength;
        int page6 = 5 * pageLength;
        var target = new List<SourceByte>(0x5C1);

        void Copy(int start, int end) => target.AddRange(source[start..end]);
        void Constant(byte value, string reference) => target.Add(new SourceByte(
            value, OtmrConfigurationByteSource.ProtocolConstant, reference));
        void PhaseValue(byte value, string startReference, string stopReference) => target.Add(new SourceByte(
            value,
            useSelectedCcf ? OtmrConfigurationByteSource.SelectedCcf : OtmrConfigurationByteSource.RecorderRx,
            useSelectedCcf ? startReference : stopReference));

        Constant(0x02, "proven continuous-stream prefix replacement");
        Copy(0x005, 0x086);
        PhaseValue(
            unchecked((byte)(0xFE - logicalValue0231)),
            "CCF 0x0231 transformed as (FE-value) mod 256",
            "cached RX 01 02 payload 0x86 transformed as (FE-value) mod 256");
        Copy(0x087, 0x0B8);
        PhaseValue(
            unchecked((byte)(0x03 - logicalValue0263)),
            "CCF 0x0263 transformed as (03-value) mod 256",
            "cached RX 01 02 payload 0xB8 transformed as (03-value) mod 256");
        Copy(0x0B9, page3 + 0x6B);

        // The remaining edits are identical in the 2026-08-24 START,
        // 2026-08-25 START, and 2026-08-25 STOP exchanges.
        Copy(page3 + 0x6D, page3 + 0x83);
        Constant(0x00, "proven structural zero insertion after RX page 3 payload 0x6B..0x6C removal");
        Constant(0x00, "proven structural zero insertion after RX page 3 payload 0x6B..0x6C removal");
        Copy(page3 + 0x83, page3 + 0xCB);
        Copy(page3 + 0xCD, page3 + 0xD3);
        Constant(0x00, "proven structural zero insertion after RX page 3 payload 0xCB..0xCC removal");
        Constant(0x00, "proven structural zero insertion after RX page 3 payload 0xCB..0xCC removal");
        Copy(page3 + 0xD3, page3 + 0xEB);
        Copy(page3 + 0xED, page4 + 0x0C);
        Constant(0x00, "proven cross-page zero insertion after RX page 3 payload 0xEB..0xEC removal");
        Constant(0x00, "proven cross-page zero insertion after RX page 3 payload 0xEB..0xEC removal");
        Copy(page4 + 0x0C, page4 + 0x5C);
        Copy(page4 + 0x5E, page4 + 0x64);
        Constant(0x00, "proven structural zero insertion after RX page 4 payload 0x5C..0x5D removal");
        Constant(0x00, "proven structural zero insertion after RX page 4 payload 0x5C..0x5D removal");
        Copy(page4 + 0x64, page4 + 0x74);
        Copy(page4 + 0x76, page4 + 0x94);
        Constant(0x00, "proven structural zero insertion after RX page 4 payload 0x74..0x75 removal");
        Constant(0x00, "proven structural zero insertion after RX page 4 payload 0x74..0x75 removal");
        Copy(page4 + 0x94, page6 + 0xC8);
        Constant(0x40, "proven continuous-stream terminal replacement");
        Copy(page6 + 0xC9, source.Length);

        return target;
    }

    private static OtmrGeneratedConfigurationWrite CreateAcknowledgement(byte[] receivedPage06)
    {
        byte[] bytes = OtmrProtocolDerivation.CreateReadDataAcknowledgement(receivedPage06);
        var provenance = new OtmrConfigurationByteProvenance[bytes.Length];
        for (int offset = 0; offset < bytes.Length; offset++)
        {
            (OtmrConfigurationByteSource source, string reference) = offset switch
            {
                1 or 9 => (OtmrConfigurationByteSource.RecorderRx, "RX 01 0C transaction"),
                5 => (OtmrConfigurationByteSource.RecorderRx, "RX 01 0C frame offset 0x004"),
                11 => (OtmrConfigurationByteSource.CalculatedFieldOrChecksum, "payload sum modulo 256"),
                _ => (OtmrConfigurationByteSource.ProtocolConstant, "proven acknowledgement framing/header constant")
            };
            provenance[offset] = new(offset, bytes[offset], source, reference);
        }
        return new OtmrGeneratedConfigurationWrite("000301", bytes, provenance);
    }

    private static OtmrGeneratedConfigurationWrite CreatePageWrite(
        string captureEvent,
        byte[] receivedPage,
        IReadOnlyList<SourceByte> payload)
    {
        byte[] bytes = new byte[payload.Count + 12];
        bytes[0] = 0x01;
        bytes[1] = receivedPage[3];
        bytes[2] = 0x00;
        bytes[3] = receivedPage[3];
        bytes[4] = receivedPage[4];
        bytes[5] = receivedPage[5];
        bytes[6] = receivedPage[6];
        bytes[7] = checked((byte)payload.Count);
        bytes[8] = 0x02;
        for (int offset = 0; offset < payload.Count; offset++)
            bytes[9 + offset] = payload[offset].Value;
        bytes[^3] = 0x03;
        bytes[^2] = OtmrProtocolDerivation.ComputePayloadCheckByte(bytes.AsSpan(9, payload.Count));
        bytes[^1] = 0x04;

        var provenance = new OtmrConfigurationByteProvenance[bytes.Length];
        for (int offset = 0; offset < bytes.Length; offset++)
        {
            (OtmrConfigurationByteSource source, string reference) = offset switch
            {
                1 or 3 => (OtmrConfigurationByteSource.RecorderRx, $"RX 01 {receivedPage[1]:X2} frame offset 0x003"),
                4 or 5 or 6 => (OtmrConfigurationByteSource.RecorderRx, $"RX 01 {receivedPage[1]:X2} frame offset 0x{offset:X3}"),
                7 => (OtmrConfigurationByteSource.CalculatedFieldOrChecksum, "generated payload length"),
                8 => (OtmrConfigurationByteSource.ProtocolConstant, "STX framing constant"),
                _ when offset >= 9 && offset < bytes.Length - 3 =>
                    (payload[offset - 9].Source, payload[offset - 9].SourceReference),
                _ when offset == bytes.Length - 2 =>
                    (OtmrConfigurationByteSource.CalculatedFieldOrChecksum, "payload sum modulo 256"),
                _ => (OtmrConfigurationByteSource.ProtocolConstant, "proven frame/header constant")
            };
            provenance[offset] = new(offset, bytes[offset], source, reference);
        }

        return new OtmrGeneratedConfigurationWrite(captureEvent, bytes, provenance);
    }

    private static ReadOnlySpan<byte> GetPayload(byte[] frame) =>
        frame.AsSpan(9, frame.Length - 12);

    private readonly record struct SourceByte(
        byte Value,
        OtmrConfigurationByteSource Source,
        string SourceReference);
}

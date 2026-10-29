using System.Text.RegularExpressions;
using CcfEditor.Otmr.Live;

namespace CcfEditor.Tests;

public sealed class OtmrConfigurationChecksumTests
{
    [Theory]
    [InlineData("HHD_Serial_Trace_20260824_143201.txt", 0, int.MaxValue, 0, int.MaxValue, false)]
    [InlineData("HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", 41, 253, 44, 238, false)]
    [InlineData("HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", 1033, 1245, 1036, 1230, true)]
    public void ChecksumBalancingPreservesEveryByteOfTheThreeGoldenExchanges(
        string filename, int firstWrite, int lastWrite, int firstRead, int lastRead, bool stop)
    {
        Dictionary<byte, byte[]> replies = LoadDataReplies(filename, firstRead, lastRead);
        byte[] ccf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));
        byte[][] expected = LoadHhdFrames(filename, "Write", firstWrite, lastWrite)
            .Skip(11).Take(7).ToArray();

        OtmrGeneratedConfigurationExchange actual = stop
            ? OtmrConfigurationExchangeGenerator.CreateStopRestoration(replies)
            : OtmrConfigurationExchangeGenerator.CreateStart(replies, ccf);

        Assert.Equal(7, expected.Length);
        Assert.Equal(7, actual.Writes.Count);
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual.Writes[index].Bytes);
    }

    [Fact]
    public void EditedRecorderHeaderUsesItsCompensatingChecksumInsteadOfTheSelectedCcfByte()
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();
        byte[] ccf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));

        OtmrGeneratedConfigurationWrite page = OtmrConfigurationExchangeGenerator.CreateStart(replies, ccf).Writes[1];
        byte[] singlePage = OtmrProtocolDerivation.CreateConfigurationPage01WriteFromCcf(replies[2], replies[4], ccf);

        // Eight generated edits add 5B. The recorder's 01 -> A6 balancing
        // byte cancels that delta. These are software expectations, not an ACK.
        Assert.Equal(0x9F, page.Bytes[9 + 0x82]);
        Assert.Equal(0x03, page.Bytes[9 + 0xB4]);
        Assert.Equal(0x8E, page.Bytes[^2]);
        Assert.Equal(0x87, Sum(page.Bytes.AsSpan(10, 0xB4)));
        Assert.Equal(page.Bytes, singlePage);
        OtmrConfigurationByteProvenance checksum = page.Provenance[9 + 0x82];
        Assert.Equal(OtmrConfigurationByteSource.CalculatedFieldOrChecksum, checksum.Source);
        Assert.Contains("0x86", checksum.SourceReference);
        Assert.Contains("0xB8", checksum.SourceReference);
        Assert.Contains("0xB4", checksum.SourceReference);
    }

    [Fact]
    public void EditedRecorderStopRestorationBalancesItsZeroWireMode()
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();

        OtmrGeneratedConfigurationWrite page = OtmrConfigurationExchangeGenerator.CreateStopRestoration(replies).Writes[1];

        Assert.Equal(0xA2, page.Bytes[9 + 0x82]);
        Assert.Equal(0x00, page.Bytes[9 + 0xB4]);
        Assert.Equal(0x8E, page.Bytes[^2]);
        Assert.Equal(0x87, Sum(page.Bytes.AsSpan(10, 0xB4)));
    }

    [Theory]
    [InlineData(0x00, 0x03, 0x9F)]
    [InlineData(0x03, 0x00, 0xA2)]
    public void ChangingTheTargetModePreservesTheGeneratedHeaderBalance(int selectedMode, int wireMode, int wireChecksum)
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();
        byte[] ccf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));
        ccf[0x263] = (byte)selectedMode;

        byte[] page = OtmrConfigurationExchangeGenerator.CreateStart(replies, ccf).Writes[1].Bytes;

        Assert.Equal(wireMode, page[9 + 0xB4]);
        Assert.Equal(wireChecksum, page[9 + 0x82]);
        Assert.Equal(0x87, Sum(page.AsSpan(10, 0xB4)));
        Assert.Equal(0x8E, page[^2]);
    }

    [Fact]
    public void ChangingTheObsoleteSelectedChecksumDoesNotChangeGeneratedWrites()
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();
        byte[] original = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));
        byte[] changed = original.ToArray();
        changed[0x231] = 0xA9;

        OtmrGeneratedConfigurationExchange first = OtmrConfigurationExchangeGenerator.CreateStart(replies, original);
        OtmrGeneratedConfigurationExchange second = OtmrConfigurationExchangeGenerator.CreateStart(replies, changed);

        for (int index = 0; index < first.Writes.Count; index++)
            Assert.Equal(first.Writes[index].Bytes, second.Writes[index].Bytes);
    }

    [Fact]
    public void GeneratingStartAndStopDoesNotMutateCurrentOrFrozenInputs()
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();
        Dictionary<byte, byte[]> frozen = replies.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        byte[] ccf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));
        byte[] originalCcf = ccf.ToArray();

        OtmrConfigurationExchangeGenerator.CreateStart(replies, ccf);
        OtmrConfigurationExchangeGenerator.CreateStopRestoration(replies);

        Assert.Equal(originalCcf, ccf);
        foreach (byte transaction in replies.Keys)
            Assert.Equal(frozen[transaction], replies[transaction]);
    }

    [Fact]
    public void AnInvalidRecorderPayloadCheckIsRejectedBeforeGeneratingWrites()
    {
        Dictionary<byte, byte[]> replies = CreateEditedRecorderReplies();
        replies[2][9 + 0x15] ^= 1;
        byte[] ccf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));

        Assert.Throws<ArgumentException>(() => OtmrConfigurationExchangeGenerator.CreateStart(replies, ccf));
        Assert.Throws<ArgumentException>(() => OtmrConfigurationExchangeGenerator.CreateStopRestoration(replies));
        Assert.Throws<ArgumentException>(() => OtmrProtocolDerivation.CreateConfigurationPage01WriteFromCcf(replies[2], replies[4], ccf));
    }

    private static Dictionary<byte, byte[]> CreateEditedRecorderReplies()
    {
        Dictionary<byte, byte[]> replies = LoadDataReplies("HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", 44, 238);
        byte[] page = replies[2];
        (int Offset, int Delta)[] edits =
        {
            (0x15, 1), (0x16, 2), (0x17, 3), (0x18, 4),
            (0x23, 5), (0x24, 6), (0x82, 0x20), (0x84, 0x26)
        };
        foreach ((int offset, int delta) in edits)
            page[9 + offset] = unchecked((byte)(page[9 + offset] + delta));
        page[9 + 0x86] = 0xA6;
        page[^2] = Sum(page.AsSpan(9, page.Length - 12));
        Assert.Equal(0x5E, page[^2]);
        return replies;
    }

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return unchecked((byte)sum);
    }

    private static Dictionary<byte, byte[]> LoadDataReplies(string filename, int first, int last)
    {
        IReadOnlyList<byte[]> frames = LoadHhdFrames(filename, "Read", first, last);
        return new byte[] { 2, 4, 6, 8, 10, 12 }.ToDictionary(
            transaction => transaction,
            transaction => Assert.Single(frames, frame => frame[1] == transaction && frame.Length == (transaction == 12 ? 0xD6 : 0x10B)));
    }

    private static IReadOnlyList<byte[]> LoadHhdFrames(string filename, string direction, int first, int last)
    {
        var stream = new List<byte>();
        bool selected = false;
        foreach (string line in File.ReadLines(FindTestDataFile(filename)))
        {
            Match header = Regex.Match(line.TrimStart('\uFEFF'), @"^(?<event>\d+): (?<direction>Write|Read) Request \((?<side>DOWN|UP)\),");
            if (header.Success)
            {
                int number = int.Parse(header.Groups["event"].Value, System.Globalization.CultureInfo.InvariantCulture);
                selected = number >= first && number <= last && header.Groups["direction"].Value == direction &&
                    header.Groups["side"].Value == (direction == "Write" ? "DOWN" : "UP");
                continue;
            }
            Match bytes = Regex.Match(line, @"^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}");
            if (selected && bytes.Success)
                stream.AddRange(bytes.Groups["hex"].Value.Split(' ').Select(value => Convert.ToByte(value, 16)));
            else if (line.Length == 0)
                selected = false;
        }

        var frames = new List<byte[]>();
        for (int offset = 0; offset + 12 <= stream.Count;)
        {
            int length = 12 + stream[offset + 7];
            if (stream[offset] == 1 && offset + length <= stream.Count && stream[offset + 8] == 2 &&
                stream[offset + length - 3] == 3 && stream[offset + length - 1] == 4)
            {
                frames.Add(stream.GetRange(offset, length).ToArray());
                offset += length;
            }
            else offset++;
        }
        return frames;
    }

    private static string FindTestDataFile(string filename)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "TestData", filename);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Required authoritative OTMR capture was not found: {filename}");
    }
}

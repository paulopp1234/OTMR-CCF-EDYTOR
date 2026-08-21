using System.Security.Cryptography;
using System.Text;

namespace CcfEditor.Otmr.Rcm;

public static class RcmRawEvidenceAnalyzer
{
    public static void Analyze(RcmStateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (RcmRawFrameEvidence frame in evidence.CompleteRawFrames)
        {
            byte[] bytes = frame.RawFrameBytes.Select(value => checked((byte)value)).ToArray();
            Increment(frequencies, $"FRAME[{frame.RawFrameHex}]");

            for (int index = 0; index < bytes.Length; index++)
                Increment(frequencies, $"POSITION[{index:D3}]={bytes[index]:X2}");

            for (int index = 0; index + 1 < bytes.Length; index++)
                Increment(frequencies, $"SEQUENCE2[{bytes[index]:X2} {bytes[index + 1]:X2}]");
        }

        evidence.FeatureFrequencies = frequencies;
        evidence.CandidateStableFeatures = evidence.FrameCount == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : frequencies
                .Where(pair => pair.Key.StartsWith("POSITION[", StringComparison.Ordinal) &&
                               pair.Value == evidence.FrameCount)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        string canonical = string.Join(
            "\n",
            evidence.CandidateStableFeatures
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        evidence.CandidateRawSignature = $"CANDIDATE-RAW-SHA256:{Convert.ToHexString(digest)}";
    }

    private static void Increment(Dictionary<string, int> frequencies, string feature)
    {
        frequencies.TryGetValue(feature, out int count);
        frequencies[feature] = count + 1;
    }
}

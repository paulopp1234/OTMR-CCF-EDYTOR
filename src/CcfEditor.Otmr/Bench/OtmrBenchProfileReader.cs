namespace CcfEditor.Otmr.Bench;

public static class OtmrBenchProfileReader
{
    private static readonly string[] RequiredColumns =
    {
        "Connector", "Pin", "Role", "MIO", "Channel", "ExpectedFunction",
        "ReturnOrPair", "SafetyInstruction", "IsVoltageTestPoint",
        "ExpectedRecordA", "ExpectedRecordB", "ExpectedCard", "ExpectedChannel",
        "EvidenceStatus", "Source"
    };

    public static IReadOnlyList<OtmrBenchPinDefinition> LoadTsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
            throw new InvalidDataException("Bench pin-map file is empty.");

        string[] header = SplitLine(lines[0]);
        var column = header
            .Select((name, index) => new { Name = name.Trim(), Index = index })
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        foreach (string required in RequiredColumns)
        {
            if (!column.ContainsKey(required))
                throw new InvalidDataException($"Bench pin-map is missing required column '{required}'.");
        }

        var result = new List<OtmrBenchPinDefinition>();
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                continue;

            string[] values = SplitLine(lines[lineIndex]);
            string Get(string name)
            {
                int index = column[name];
                return index < values.Length ? values[index].Trim() : string.Empty;
            }

            string connector = Get("Connector");
            string pin = Get("Pin");
            if (connector.Length == 0 || pin.Length == 0)
                throw new InvalidDataException($"Bench pin-map line {lineIndex + 1} must contain Connector and Pin.");

            result.Add(new OtmrBenchPinDefinition(
                connector,
                pin,
                Get("Role"),
                Get("MIO"),
                Get("Channel"),
                Get("ExpectedFunction"),
                Get("ReturnOrPair"),
                Get("SafetyInstruction"),
                ParseBool(Get("IsVoltageTestPoint"), lineIndex + 1),
                ParseNullableInt(Get("ExpectedRecordA"), "ExpectedRecordA", lineIndex + 1),
                ParseNullableInt(Get("ExpectedRecordB"), "ExpectedRecordB", lineIndex + 1),
                ParseNullableInt(Get("ExpectedCard"), "ExpectedCard", lineIndex + 1),
                ParseNullableInt(Get("ExpectedChannel"), "ExpectedChannel", lineIndex + 1),
                Get("EvidenceStatus"),
                Get("Source")));
        }

        return result;
    }

    private static string[] SplitLine(string line) => line.Split('\t');

    private static bool ParseBool(string value, int line)
    {
        if (bool.TryParse(value, out bool parsed))
            return parsed;
        throw new InvalidDataException($"Invalid IsVoltageTestPoint value '{value}' on line {line}.");
    }

    private static int? ParseNullableInt(string value, string field, int line)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (int.TryParse(value, out int parsed))
            return parsed;
        throw new InvalidDataException($"Invalid {field} value '{value}' on line {line}.");
    }
}

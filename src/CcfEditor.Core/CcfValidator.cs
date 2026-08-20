namespace CcfEditor.Core;

public static class CcfValidator
{
    public static IReadOnlyList<CcfValidationIssue> ValidateMilestone1(CcfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<CcfValidationIssue>();

        if (document.Length != CcfConstants.FileSize)
            issues.Add(new(CcfValidationSeverity.Error, $"File length is {document.Length}; expected {CcfConstants.FileSize}."));

        if (document.Records.Count != CcfConstants.RecordCount)
            issues.Add(new(CcfValidationSeverity.Error, $"Parsed {document.Records.Count} records; expected {CcfConstants.RecordCount}."));

        foreach (var record in document.Records)
        {
            var expectedOffset = CcfConstants.GetRecordOffset(record.PhysicalIndex);
            if (record.Offset != expectedOffset)
                issues.Add(new(CcfValidationSeverity.Error, $"Record {record.PhysicalIndex} offset is 0x{record.Offset:X}; expected 0x{expectedOffset:X}."));

            if (record.EventIndex != record.PhysicalIndex)
            {
                issues.Add(new(
                    CcfValidationSeverity.Warning,
                    $"Physical record {record.PhysicalIndex} stores event index {record.EventIndex}. This is reported only; Milestone 1 does not repair it."));
            }

            if (record.Type == 2 && record.PairRecord is > 255)
                issues.Add(new(CcfValidationSeverity.Error, $"Digital record {record.PhysicalIndex} pair {record.PairRecord} is outside 0..255."));
        }

        return issues;
    }
}

public enum CcfValidationSeverity
{
    Information,
    Warning,
    Error
}

public sealed record CcfValidationIssue(CcfValidationSeverity Severity, string Message);

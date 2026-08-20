using System.Collections.ObjectModel;

namespace CcfEditor.Core;

public sealed class CcfDocument
{
    private readonly byte[] _originalBytes;
    private readonly byte[] _workingBytes;

    internal CcfDocument(byte[] sourceBytes, string? sourcePath)
    {
        _originalBytes = sourceBytes.ToArray();
        _workingBytes = sourceBytes.ToArray();
        SourcePath = sourcePath;

        Header = new CcfHeader(_workingBytes);
        Records = new ReadOnlyCollection<CcfRecord>(
            Enumerable.Range(0, CcfConstants.RecordCount)
                .Select(index => new CcfRecord(_workingBytes, index))
                .ToArray());
    }

    public string? SourcePath { get; }
    public int Length => _workingBytes.Length;
    public CcfHeader Header { get; }
    public IReadOnlyList<CcfRecord> Records { get; }

    public string OriginalSha256 => CcfSha256.ComputeHex(_originalBytes);
    public string WorkingSha256 => CcfSha256.ComputeHex(_workingBytes);
    public bool IsByteIdenticalToOriginal => _originalBytes.AsSpan().SequenceEqual(_workingBytes);

    public byte[] GetOriginalBytesSnapshot() => _originalBytes.ToArray();
    public byte[] GetWorkingBytesSnapshot() => _workingBytes.ToArray();

    internal ReadOnlySpan<byte> WorkingBytesSpan => _workingBytes;
}

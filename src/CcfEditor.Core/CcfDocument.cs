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
    public bool IsModified => !IsByteIdenticalToOriginal;

    public byte[] GetOriginalBytesSnapshot() => _originalBytes.ToArray();
    public byte[] GetWorkingBytesSnapshot() => _workingBytes.ToArray();

    public IReadOnlyList<CcfByteChange> GetByteChanges()
    {
        var changes = new List<CcfByteChange>();
        for (int offset = 0; offset < _workingBytes.Length; offset++)
        {
            if (_originalBytes[offset] != _workingBytes[offset])
                changes.Add(new CcfByteChange(offset, _originalBytes[offset], _workingBytes[offset]));
        }

        return changes;
    }

    internal byte[] WorkingBytesBuffer => _workingBytes;
    internal ReadOnlySpan<byte> OriginalBytesSpan => _originalBytes;
    internal ReadOnlySpan<byte> WorkingBytesSpan => _workingBytes;
}

public readonly record struct CcfByteChange(int Offset, byte OriginalValue, byte WorkingValue);

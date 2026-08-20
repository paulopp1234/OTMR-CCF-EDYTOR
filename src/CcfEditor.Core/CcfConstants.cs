namespace CcfEditor.Core;

public static class CcfConstants
{
    public const int FileSize = 26_600;
    public const int HeaderSize = 1_000;
    public const int RecordAreaOffset = 0x03E8;
    public const int RecordCount = 256;
    public const int RecordSize = 100;

    public static int GetRecordOffset(int recordIndex)
    {
        if ((uint)recordIndex >= RecordCount)
            throw new ArgumentOutOfRangeException(nameof(recordIndex), recordIndex, "Record index must be 0..255.");

        return RecordAreaOffset + (recordIndex * RecordSize);
    }
}

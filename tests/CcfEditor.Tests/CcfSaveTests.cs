using CcfEditor.Core;

namespace CcfEditor.Tests;

public sealed class CcfSaveTests
{
    [Fact]
    public void NoEditSaveAs_IsByteForByteIdentical_AndSha256Matches()
    {
        var sourceBytes = TestCcfFactory.CreateDeterministicFile();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "CcfEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var sourcePath = Path.Combine(tempDirectory, "input.ccf");
        var outputPath = Path.Combine(tempDirectory, "output.ccf");
        File.WriteAllBytes(sourcePath, sourceBytes);
        try
        {
            var document = CcfParser.Load(sourcePath);
            var save = CcfFileService.SaveAs(document, outputPath);
            var outputBytes = File.ReadAllBytes(outputPath);
            Assert.Equal(CcfConstants.FileSize, outputBytes.Length);
            Assert.Equal(sourceBytes, outputBytes);
            Assert.True(save.OutputMatchesWorkingBytes);
            Assert.True(save.NoEditShaMatchesOriginal);
            Assert.Equal(CcfSha256.ComputeHex(sourceBytes), save.OriginalSha256);
            Assert.Equal(save.OriginalSha256, save.WorkingSha256);
            Assert.Equal(save.WorkingSha256, save.OutputSha256);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SaveAs_RefusesToOverwriteSourceFile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "CcfEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var sourcePath = Path.Combine(tempDirectory, "source.ccf");
        File.WriteAllBytes(sourcePath, TestCcfFactory.CreateDeterministicFile());
        try
        {
            var document = CcfParser.Load(sourcePath);
            var ex = Assert.Throws<InvalidOperationException>(() => CcfFileService.SaveAs(document, sourcePath));
            Assert.Contains("Save As", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}

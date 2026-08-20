using CcfEditor.Core;

namespace CcfEditor.Tests;

public sealed class CcfValidatorTests
{
    [Fact]
    public void ValidSyntheticLayout_HasNoErrors()
    {
        var document = CcfParser.Parse(TestCcfFactory.CreateDeterministicFile());
        var issues = CcfValidator.ValidateMilestone1(document);
        Assert.DoesNotContain(issues, issue => issue.Severity == CcfValidationSeverity.Error);
    }
}

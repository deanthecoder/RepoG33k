// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class UnnecessaryEnumMemberValueCodeReviewCheckTests
{
    [Test]
    public void CanFixWhenFindingMatchesRuleAndLineIsPositiveReturnsTrue()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnnecessaryEnumMemberValue,
            "Sample.cs",
            10,
            "Unnecessary enum member value.");

        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();

        var canFix = check.CanFix(finding);

        Assert.That(canFix, Is.True);
    }

    [Test]
    public void TryFixWhenFindingIsNullReturnsFalse()
    {
        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();

        var success = check.TryFix(null, new FileInfo("Sample.cs"), out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Finding is required."));
    }

    [Test]
    public void TryFixWhenFilePathIsBlankReturnsFalse()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnnecessaryEnumMemberValue,
            "Sample.cs",
            10,
            "Unnecessary enum member value.");
        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();

        var success = check.TryFix(finding, null, out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("File path could not be resolved."));
    }
}

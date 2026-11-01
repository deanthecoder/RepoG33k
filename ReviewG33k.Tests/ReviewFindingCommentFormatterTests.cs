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

public sealed class ReviewFindingCommentFormatterTests
{
    [Test]
    public void FormatWhenRuleHasFriendlyTemplateReturnsFriendlyComment()
    {
        var formatter = new ReviewFindingCommentFormatter();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.MethodCanBeStatic,
            "Sample.cs",
            12,
            "Method `SumAbs(int left, int right)` can likely be made static.");

        var comment = formatter.Format(finding);

        Assert.That(comment, Is.EqualTo("This method looks like it could be made static, since it does not appear to use instance state."));
    }

    [Test]
    public void FormatWhenRuleHasArgumentClarityTemplateReturnsHumanFriendlyText()
    {
        var formatter = new ReviewFindingCommentFormatter();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.ConsecutiveNullArguments,
            "Sample.cs",
            18,
            "Consecutive positional null arguments are passed. Consider naming these arguments for clarity.");

        var comment = formatter.Format(finding);

        Assert.That(comment, Is.EqualTo("These adjacent null arguments are hard to read positionally. Naming them would make the intent clearer."));
    }

    [Test]
    public void FormatWhenRuleHasReadonlyTemplateReturnsFriendlyComment()
    {
        var formatter = new ReviewFindingCommentFormatter();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.PrivateFieldCanBeReadonly,
            "Sample.cs",
            14,
            "Private field `count` can be made `readonly`.");

        var comment = formatter.Format(finding);

        Assert.That(comment, Is.EqualTo("This field looks like it could be marked readonly, since it is only assigned during initialization."));
    }

    [Test]
    public void FormatWhenRuleHasUnusedUsingTemplateReturnsFriendlyComment()
    {
        var formatter = new ReviewFindingCommentFormatter();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnusedUsingsRoslyn,
            "Sample.cs",
            2,
            "Unused using directive `using System;`.");

        var comment = formatter.Format(finding);

        Assert.That(comment, Is.EqualTo("This using directive does not appear to be needed anymore."));
    }

    [Test]
    public void FormatWhenRuleIsUnknownFallsBackToOriginalMessage()
    {
        var formatter = new ReviewFindingCommentFormatter();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "custom-rule",
            "Sample.cs",
            7,
            "Original finding message.");

        var comment = formatter.Format(finding);

        Assert.That(comment, Is.EqualTo("Original finding message."));
    }
}

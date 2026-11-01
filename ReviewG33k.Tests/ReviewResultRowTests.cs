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
using ReviewG33k.Services.Checks.Support;
using ReviewG33k.Views;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewResultRowTests
{
    [Test]
    public void CanCodexPromptActiveWhenRowIsIncludedReturnsTrue()
    {
        var row = CreateCodexRow();

        Assert.That(row.CanCodexPromptActive, Is.True);
    }

    [Test]
    public void CanCodexPromptActiveWhenRowIsDeselectedReturnsFalse()
    {
        var row = CreateCodexRow();
        row.IsIncluded = false;

        Assert.That(row.CanCodexPromptActive, Is.False);
    }

    [Test]
    public void TickSameTypeMenuHeaderWhenRuleIdExistsIncludesRuleId()
    {
        var row = CreateCodexRow();
        Assert.That(row.TickSameTypeMenuHeader, Is.EqualTo("Select all `sample-rule` issues"));
    }

    [Test]
    public void TickSameTypeMenuHeaderWhenRuleIdMissingUsesGenericText()
    {
        var row = new ReviewResultRow(
            new CodeSmellFinding(
                CodeReviewFindingSeverity.Hint,
                null,
                "Sample.cs",
                12,
                "Sample finding"),
            canOpen: false,
            commentAvailability: ReviewResultRow.ActionAvailability.Hidden,
            fixAvailability: ReviewResultRow.ActionAvailability.Hidden,
            codexPromptAvailability: ReviewResultRow.ActionAvailability.Enabled);
        Assert.That(row.TickSameTypeMenuHeader, Is.EqualTo("Select issues of this type"));
    }

    [Test]
    public void CategoryTextWhenRuleIsMissingTestsUsesTestingCategory()
    {
        var row = new ReviewResultRow(
            new CodeSmellFinding(
                CodeReviewFindingSeverity.Hint,
                "missing-tests",
                "Sample.cs",
                12,
                "Sample finding"),
            canOpen: false,
            commentAvailability: ReviewResultRow.ActionAvailability.Hidden,
            fixAvailability: ReviewResultRow.ActionAvailability.Hidden,
            codexPromptAvailability: ReviewResultRow.ActionAvailability.Enabled);

        Assert.That(row.CategoryText, Is.EqualTo(CodeReviewFindingCategoryResolver.Testing));
    }

    [Test]
    public void CategoryTextWhenRuleIsUnknownFallsBackToMaintainability()
    {
        var row = CreateCodexRow();
        Assert.That(row.CategoryText, Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
    }

    [Test]
    public void IssueSummaryWhenFindingMessageContainsNewlinesCollapsesToSingleLine()
    {
        var row = new ReviewResultRow(
            new CodeSmellFinding(
                CodeReviewFindingSeverity.Hint,
                "sample-rule",
                "Sample.cs",
                12,
                "Method `Method(int foo,\nstring bar)` can likely be made static."),
            canOpen: false,
            commentAvailability: ReviewResultRow.ActionAvailability.Hidden,
            fixAvailability: ReviewResultRow.ActionAvailability.Hidden,
            codexPromptAvailability: ReviewResultRow.ActionAvailability.Enabled);

        Assert.That(row.IssueSummary, Is.EqualTo("Method `Method(int foo, string bar)` can likely be made static."));
        Assert.That(row.IssueFull, Is.EqualTo("Method `Method(int foo,\nstring bar)` can likely be made static."));
    }

    private static ReviewResultRow CreateCodexRow()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "sample-rule",
            "Sample.cs",
            12,
            "Sample finding");

        return new ReviewResultRow(
            finding,
            canOpen: false,
            commentAvailability: ReviewResultRow.ActionAvailability.Hidden,
            fixAvailability: ReviewResultRow.ActionAvailability.Hidden,
            codexPromptAvailability: ReviewResultRow.ActionAvailability.Enabled);
    }
}

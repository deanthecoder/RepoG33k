// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Views;
using ReviewG33k.Views.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewFindingSelectionTests
{
    private static CodeSmellFinding Finding(string message = "An issue") =>
        new(CodeReviewFindingSeverity.Important, "rule", "file.cs", 1, message);

    [Test]
    public void IgnoringAndRestoringDoesNotChangePostingSelection()
    {
        var selection = new ReviewFindingSelection();
        var finding = Finding();
        Assert.That(selection.IsSelected(finding), Is.True);
        Assert.That(selection.IsIgnored(finding), Is.False);
        selection.SetIgnored(finding, true);
        Assert.That(selection.IsSelected(finding), Is.True);
        selection.SetSelected(finding, false);
        selection.SetIgnored(finding, false);
        Assert.That(selection.IsSelected(finding), Is.False);
        Assert.That(selection.IsIgnored(finding), Is.False);
    }

    [Test]
    public void ReopenedResultsCanRecognizeEquivalentFindingsWithoutHidingOtherMessages()
    {
        var selection = new ReviewFindingSelection();
        selection.SetIgnored(Finding(), true);
        selection.SetSelected(Finding(), false);
        Assert.That(selection.IsIgnored(Finding()), Is.True);
        Assert.That(selection.IsSelected(Finding()), Is.False);
        Assert.That(selection.IsIgnored(Finding("Another issue")), Is.False);
        Assert.That(new ReviewFindingSelection().IsIgnored(Finding()), Is.False);
    }

    [Test]
    public void RowIgnoreActionLeavesCheckboxUntouched()
    {
        var row = new ReviewResultRow(Finding(), true, ReviewResultRow.ActionAvailability.Enabled,
            ReviewResultRow.ActionAvailability.Hidden, ReviewResultRow.ActionAvailability.Hidden);
        row.IsIgnored = true;
        Assert.That(row.IsIncluded, Is.True);
        Assert.That(row.IgnoreActionText, Is.EqualTo("Restore"));
        row.IsIncluded = false;
        row.IsIgnored = false;
        Assert.That(row.IsIncluded, Is.False);
        Assert.That(row.IgnoreActionText, Is.EqualTo("Ignore"));
    }

    [Test]
    public void UpdatingCanvasFindingsPreservesFileSelectionAndCanRestoreIgnoredMarkers()
    {
        var finding = Finding();
        var file = new DiffLensFile(Path.Combine(Path.GetTempPath(), "file.cs"), "file.cs", "HEAD", "old", System.Text.Encoding.UTF8.GetBytes("new"));
        var cell = new DiffLensCell(new DiffLensEntry("file.cs", file, null)) { Findings = [finding] };
        var canvas = new DiffLensControl();
        canvas.SetFiles("repo", [cell]);
        Assert.That(canvas.SelectedFindings, Has.Count.EqualTo(1));
        canvas.UpdateFindings(_ => []);
        Assert.That(canvas.SelectedFindings, Is.Empty);
        Assert.That(canvas.SelectedPath, Is.EqualTo("file.cs"));
        canvas.UpdateFindings(_ => [finding]);
        Assert.That(canvas.SelectedFindings, Has.Count.EqualTo(1));
        canvas.StopAnimations();
    }
}

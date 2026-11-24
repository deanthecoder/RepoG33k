// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Text;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Views.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensFileActionTests
{
    private static DiffLensCell Cell() => new(new DiffLensEntry("sample.cs",
        new DiffLensFile(Path.Combine(Path.GetTempPath(), "sample.cs"), "sample.cs", "HEAD", "old", Encoding.UTF8.GetBytes("new")), null));

    [TestCase(true)]
    [TestCase(false)]
    public async Task StageKeepsRefreshedCardHiddenOnSuccessAndRestoresItOnFailure(bool success)
    {
        var original = Cell();
        var refreshed = Cell();
        var canvas = new DiffLensControl { CanStageFile = _ => true };
        canvas.SetFiles("repo", [original]);
        var calls = 0;
        canvas.StageFileAsync = path =>
        {
            Assert.That(path, Is.EqualTo("sample.cs"));
            calls++;
            canvas.SetFiles("repo", [refreshed]);
            return Task.FromResult(success);
        };

        await canvas.ActivateFileActionAsync(original);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(canvas.VisibleCells.Contains(refreshed), Is.EqualTo(!success));
        Assert.That(original.IsReviewed, Is.False);
        canvas.StopAnimations();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SlowStagingHidesImmediatelyAndIgnoresRepeatedClicks(bool success)
    {
        var cell = Cell();
        var completion = new TaskCompletionSource<bool>();
        var calls = 0;
        var canvas = new DiffLensControl { CanStageFile = _ => true, StageFileAsync = _ => { calls++; return completion.Task; } };
        canvas.SetFiles("repo", [cell]);
        var pending = canvas.ActivateFileActionAsync(cell);
        Assert.That(canvas.VisibleCells, Is.Empty);
        Assert.That(pending.IsCompleted, Is.False);
        await canvas.ActivateFileActionAsync(cell);
        Assert.That(calls, Is.EqualTo(1));
        completion.SetResult(success);
        await pending;
        Assert.That(canvas.VisibleCells.Contains(cell), Is.EqualTo(!success));
        canvas.StopAnimations();
    }

    [Test]
    public void StageExceptionRestoresExpandedCard()
    {
        var cell = Cell();
        var canvas = new DiffLensControl { CanStageFile = _ => true, StageFileAsync = _ => throw new IOException("Git failed") };
        canvas.SetFiles("repo", [cell]);
        Assert.ThrowsAsync<IOException>(() => canvas.ActivateFileActionAsync(cell));
        Assert.That(cell.IsReviewed, Is.False);
        Assert.That(canvas.VisibleCells, Does.Contain(cell));
        canvas.StopAnimations();
    }

    [Test]
    public async Task StagedCardReappearsWhenContentChangesOrUserSelectsIt()
    {
        var cell = Cell();
        var canvas = new DiffLensControl { CanStageFile = _ => true, StageFileAsync = _ => Task.FromResult(true) };
        canvas.SetFiles("repo", [cell]);
        await canvas.ActivateFileActionAsync(cell);
        Assert.That(canvas.VisibleCells, Is.Empty);
        canvas.SetFiles("repo", [Cell()], preserveViewport: true);
        Assert.That(canvas.VisibleCells, Is.Empty);
        Assert.That(canvas.FocusFile("sample.cs"), Is.True);
        Assert.That(canvas.VisibleCells.Count, Is.EqualTo(1));
        await canvas.ActivateFileActionAsync(canvas.VisibleCells[0]);
        var changed = new DiffLensCell(new DiffLensEntry("sample.cs",
            new DiffLensFile(Path.Combine(Path.GetTempPath(), "sample.cs"), "sample.cs", "HEAD", "old", Encoding.UTF8.GetBytes("changed again")), null));
        canvas.SetFiles("repo", [changed], preserveViewport: true);
        Assert.That(canvas.VisibleCells, Does.Contain(changed));
        canvas.StopAnimations();
    }

    [Test]
    public async Task StageAndSameScopeRefreshPreserveZoomAndPosition()
    {
        var canvas = new DiffLensControl { CanStageFile = _ => true, StageFileAsync = _ => Task.FromResult(true) };
        canvas.Measure(new Avalonia.Size(1200, 800));
        canvas.Arrange(new Avalonia.Rect(0, 0, 1200, 800));
        var cell = Cell();
        canvas.SetFiles("repo", [cell]);
        canvas.ZoomBy(2);
        var zoom = canvas.Zoom;
        var offsetField = typeof(DiffLensControl).GetField("m_offset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var offset = offsetField.GetValue(canvas);
        await canvas.ActivateFileActionAsync(cell);
        Assert.That(canvas.Zoom, Is.EqualTo(zoom));
        Assert.That(offsetField.GetValue(canvas), Is.EqualTo(offset));
        canvas.SetFiles("repo", [Cell()], preserveViewport: true);
        Assert.That(canvas.Zoom, Is.EqualTo(zoom));
        Assert.That(offsetField.GetValue(canvas), Is.EqualTo(offset));
        Assert.That(canvas.VisibleCells, Is.Empty);
        canvas.StopAnimations();
    }

    [Test]
    public async Task ReviewSnapshotsAndExpandingCardsDoNotStageFiles()
    {
        var cell = Cell();
        var canvas = new DiffLensControl { CanStageFile = _ => false };
        canvas.SetFiles("repo", [cell]);
        canvas.StageFileAsync = _ => throw new InvalidOperationException("This action must not stage.");
        await canvas.ActivateFileActionAsync(cell);
        Assert.That(cell.IsReviewed, Is.True);
        canvas.CanStageFile = _ => true;
        await canvas.ActivateFileActionAsync(cell);
        Assert.That(cell.IsReviewed, Is.False);
        canvas.StopAnimations();
    }
}

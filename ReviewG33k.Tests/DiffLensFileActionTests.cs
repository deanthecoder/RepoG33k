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
    public async Task StageCollapsesTheRefreshedCardOnlyAfterSuccess(bool success)
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
        Assert.That(refreshed.IsReviewed, Is.EqualTo(success));
        Assert.That(original.IsReviewed, Is.False);
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

// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.Document;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Views.DiffLens;

namespace ReviewG33k.Tests;

public sealed class DiffLensFindingActionsTests
{
    [TestCase("\n")]
    [TestCase("\r\n")]
    public void TodoUsesIssueInstructionsIndentationAndExistingNewlinesAndCanBeUndone(string newline)
    {
        var original = $"class Example {{{newline}\tpublic int Value() => 1;{newline}}}";
        var document = new TextDocument(original);
        var finding = Finding(2);
        var actions = new DiffLensFindingActions("Example.cs", document, [finding]);

        Assert.That(actions.TryInsertTodo(finding, out var line, out var reason), Is.True, reason);

        Assert.That(line, Is.EqualTo(2));
        Assert.That(document.Text, Does.Contain($"\t// TODO [example-rule]: Add a test for Value.{newline}\tpublic"));
        Assert.That(actions.GetCurrentLine(finding), Is.EqualTo(3));
        document.UndoStack.Undo();
        Assert.That(document.Text, Is.EqualTo(original));
        Assert.That(actions.GetCurrentLine(finding), Is.EqualTo(2));
    }

    [Test]
    public void TodoFollowsEditsAboveTheIssueAndDuplicateInsertionIsRefused()
    {
        var document = new TextDocument("class Example {\n    public int Value() => 1;\n}");
        var finding = Finding(2);
        var actions = new DiffLensFindingActions("Example.cs", document, [finding]);
        document.Insert(0, "// Heading\n");

        Assert.That(actions.TryInsertTodo(finding, out var line, out var reason), Is.True, reason);
        Assert.That(line, Is.EqualTo(3));
        var inserted = document.Text;
        Assert.That(actions.TryInsertTodo(finding, out _, out reason), Is.False);
        Assert.That(reason, Does.Contain("already present"));
        Assert.That(document.Text, Is.EqualTo(inserted));
        Assert.That(actions.GetFixInstructions(finding), Does.Contain("Example.cs:4"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangedOrDeletedIssueLineIsNotAnnotated(bool delete)
    {
        var document = new TextDocument("class Example {\n    public int Value() => 1;\n}");
        var finding = Finding(2);
        var actions = new DiffLensFindingActions("Example.cs", document, [finding]);
        var target = document.GetLineByNumber(2);
        if (delete)
            document.Remove(target.Offset, target.TotalLength);
        else
            document.Replace(target.Offset, target.Length, "    public int Value() => 2;");
        var edited = document.Text;

        Assert.That(actions.TryInsertTodo(finding, out _, out _), Is.False);
        Assert.That(document.Text, Is.EqualTo(edited));
    }

    [TestCase("Example.axaml")]
    [TestCase("Example.resx")]
    [TestCase("Example.csproj")]
    public void UnsupportedCommentSyntaxAllowsCopyingButNotInsertion(string path)
    {
        var document = new TextDocument("<Example />");
        var finding = Finding(1);
        var actions = new DiffLensFindingActions(path, document, [finding]);

        Assert.That(actions.CanInsertTodo(finding, out _), Is.False);
        Assert.That(actions.GetFixInstructions(finding), Does.Contain(finding.Message));
        Assert.That(document.Text, Is.EqualTo("<Example />"));
    }

    [TestCase("class Example {\n string value = @\"first\nsecond\";\n}")]
    [TestCase("class Example {\n /* first\nsecond */\n}")]
    public void TodoIsNotInsertedInsideMultilineStringsOrComments(string source)
    {
        var document = new TextDocument(source);
        var finding = Finding(3);
        var actions = new DiffLensFindingActions("Example.cs", document, [finding]);

        Assert.That(actions.TryInsertTodo(finding, out _, out _), Is.False);
        Assert.That(document.Text, Is.EqualTo(source));
    }

    [Test]
    public void SeveralIssuesOnOneLineCanEachReceiveOneTodo()
    {
        var document = new TextDocument("class Example {\n    public int Value() => 1;\n}");
        var first = Finding(2);
        var second = new CodeSmellFinding(CodeReviewFindingSeverity.Hint, "another-rule", "Example.cs", 2, "Different instructions.\nKeep them on one line.");
        var actions = new DiffLensFindingActions("Example.cs", document, [first, second]);

        Assert.That(actions.TryInsertTodo(first, out _, out _), Is.True);
        Assert.That(actions.TryInsertTodo(second, out _, out _), Is.True);
        Assert.That(actions.TryInsertTodo(first, out _, out _), Is.False);
        Assert.That(document.Text, Does.Contain("Different instructions. Keep them on one line."));
    }

    [Test]
    public void MarkerHitAreaIncludesSpaceOutsideTheThinMarginAndReturnsAllIssuesOnTheRow()
    {
        var findings = new[] { Finding(2), Finding(2) };
        var entry = new DiffLensEntry("Example.cs", new DiffLensFile("/tmp/Example.cs", "Example.cs", "HEAD", "class Example {}", System.Text.Encoding.UTF8.GetBytes("class Example {\n    public int Value() => 1;\n}")), null);
        var cell = new DiffLensCell(entry) { Findings = findings };
        var canvas = new DiffLensControl();
        canvas.Arrange(new Rect(0, 0, 1100, 600));
        canvas.SetFiles("example", [cell]);
        canvas.FocusFinding("Example.cs", 2);
        var left = (canvas.Bounds.Width - DiffLensCell.Width * canvas.Zoom) / 2;
        var point = new Point(left - 5, canvas.Bounds.Height / 2 + DiffLensCell.RowHeight * canvas.Zoom / 2);

        Assert.That(canvas.FindingsAt(point), Is.EqualTo(findings));
        Assert.That(canvas.FindingsAt(new Point(0, 0)), Is.Empty);
    }

    [Test]
    public void RightButtonReleaseOnMarkerRaisesTheContextMenuRequest()
    {
        var (canvas, point) = CanvasWithMarker();
        var requested = false;
        canvas.ContextRequested += (_, e) =>
        {
            requested = e.TryGetPosition(canvas, out var position) && canvas.FindingsAt(position).Count == 1;
            e.Handled = true;
        };
        var release = new PointerReleasedEventArgs(canvas, new Pointer(1, PointerType.Mouse, true), canvas,
            point, 0, new PointerPointProperties(), KeyModifiers.None, MouseButton.Right);

        canvas.RaiseEvent(release);

        Assert.That(requested, Is.True);
        Assert.That(release.Handled, Is.True);
    }

    [Test]
    public void MovingOntoMarkerSetsTheTooltipAndLeavingClearsIt()
    {
        var (canvas, point) = CanvasWithMarker();
        var pointer = new Pointer(1, PointerType.Mouse, true);
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, pointer,
            canvas, point, 0, new PointerPointProperties(), KeyModifiers.None));

        Assert.That(ToolTip.GetTip(canvas), Does.Contain("Add a test for Value."));
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, canvas, pointer,
            canvas, point, 1, new PointerPointProperties(), KeyModifiers.None));
        Assert.That(ToolTip.GetTip(canvas), Is.Null);
        canvas.StopAnimations();
    }

    [Test]
    public void FolderLabelTooltipShowsFullPathAfterCompactingAncestors()
    {
        var canvas = new DiffLensControl { RepositoryRootPath = System.IO.Path.GetFullPath("example") };
        canvas.Arrange(new Rect(0, 0, 1400, 900));
        canvas.SetFiles("example", [new DiffLensCell(new DiffLensEntry("src/Core/Example.cs", null, "Unsupported"))]);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var root = (DiffLensFolder)typeof(DiffLensControl).GetField("m_root", flags)!.GetValue(canvas);
        var labelMethod = typeof(DiffLensControl).GetMethod("FolderLabel", flags)!;
        foreach (var folder in root.Descendants)
        {
            var label = (Rect)labelMethod.Invoke(canvas, [folder]);
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas,
                new Pointer(1, PointerType.Mouse, true), canvas, label.Center, 0,
                new PointerPointProperties(), KeyModifiers.None));
            Assert.That(ToolTip.GetTip(canvas), Is.EqualTo(System.IO.Path.Combine(canvas.RepositoryRootPath,
                folder == root ? "" : System.IO.Path.Combine("src", "Core"))));
        }
        canvas.StopAnimations();
    }

    private static (DiffLensControl Canvas, Point Marker) CanvasWithMarker()
    {
        var file = new DiffLensFile("/tmp/Example.cs", "Example.cs", "HEAD", "class Example {}",
            System.Text.Encoding.UTF8.GetBytes("class Example {\n    public int Value() => 1;\n}"));
        var cell = new DiffLensCell(new DiffLensEntry("Example.cs", file, null)) { Findings = new[] { Finding(2) } };
        var canvas = new DiffLensControl();
        canvas.Arrange(new Rect(0, 0, 1100, 600));
        canvas.SetFiles("example", [cell]);
        canvas.FocusFinding("Example.cs", 2);
        var left = (canvas.Bounds.Width - DiffLensCell.Width * canvas.Zoom) / 2;
        return (canvas, new Point(left + 2 * canvas.Zoom, canvas.Bounds.Height / 2 + DiffLensCell.RowHeight * canvas.Zoom / 2));
    }

    private static CodeSmellFinding Finding(int line) =>
        new(CodeReviewFindingSeverity.Suggestion, "example-rule", "Example.cs", line, "Add a test for Value.");
}

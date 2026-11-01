using Avalonia;
using Avalonia.Input;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Views.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensNavigationTests
{
    private static readonly System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static T Field<T>(DiffLensControl canvas, string name) => (T)typeof(DiffLensControl).GetField(name, Private)!.GetValue(canvas)!;

    private static void Click(DiffLensControl canvas, Point point, bool middle = false, int count = 1)
    {
        var pointer = new Pointer(1, PointerType.Mouse, true);
        canvas.RaiseEvent(new PointerPressedEventArgs(canvas, pointer, canvas, point, 0,
            new PointerPointProperties(middle ? RawInputModifiers.MiddleMouseButton : RawInputModifiers.LeftMouseButton,
                middle ? PointerUpdateKind.MiddleButtonPressed : PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, count));
        canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, pointer, canvas, point, 0,
            new PointerPointProperties(), KeyModifiers.None, middle ? MouseButton.Middle : MouseButton.Left));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OnlyLeftClickFitsFileHeaderAndResizePreservesFileFocus(bool middle)
    {
        var canvas = new DiffLensControl();
        canvas.Measure(new Size(1200, 800));
        canvas.Arrange(new Rect(0, 0, 1200, 800));
        var cells = new[] { new DiffLensCell(new DiffLensEntry("first.cpp", null, "preview")),
            new DiffLensCell(new DiffLensEntry("second.cpp", null, "preview")) };
        canvas.SetFiles("repo", cells);
        var point = (cells[1].Position + new Vector(100, 20)) * canvas.Zoom + Field<Vector>(canvas, "m_offset");
        Click(canvas, point, middle);
        Assert.That(Field<string>(canvas, "m_fitFilePath"), middle ? Is.Null : Is.EqualTo("second.cpp"));
        canvas.Measure(new Size(900, 600));
        canvas.Arrange(new Rect(0, 0, 900, 600));
        Assert.That(Field<Avalonia.Threading.DispatcherTimer>(canvas, "m_resizeTimer").IsEnabled, Is.True);
        typeof(DiffLensControl).GetMethod("RefitAfterResize", Private)!.Invoke(canvas, null);
        var target = middle ? Field<DiffLensFolder>(canvas, "m_root").Bounds : cells[1].Bounds;
        Assert.That(Field<double>(canvas, "m_fitEndZoom"), Is.EqualTo(Math.Min(middle ? 0.72 : 2.5,
            Math.Min(840 / target.Width, 540 / target.Height))).Within(0.001));
        canvas.StopAnimations();
    }

    [Test]
    public void BackgroundDoubleClickFitsInnermostGroupOrEverything()
    {
        var canvas = new DiffLensControl();
        canvas.Measure(new Size(1200, 800));
        canvas.Arrange(new Rect(0, 0, 1200, 800));
        canvas.SetFiles("repo", [new DiffLensCell(new DiffLensEntry("src/nested/file.cpp", null, "preview"))]);
        var root = Field<DiffLensFolder>(canvas, "m_root");
        var nested = root.Descendants.Single(f => f.RelativePath == "src/nested");
        var point = (nested.Bounds.BottomRight - new Vector(2, 2)) * canvas.Zoom + Field<Vector>(canvas, "m_offset");
        Click(canvas, point, count: 2);
        Assert.That(Field<string>(canvas, "m_fitFolderPath"), Is.EqualTo("src/nested"));
        typeof(DiffLensControl).GetMethod("RefitAfterResize", Private)!.Invoke(canvas, null);
        Assert.That(Field<string>(canvas, "m_fitFolderPath"), Is.EqualTo("src/nested"));
        Click(canvas, new Point(-10, -10), count: 2);
        Assert.That(Field<string>(canvas, "m_fitFolderPath"), Is.EqualTo(root.RelativePath));
        canvas.StopAnimations();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClickingAddedLineTargetsItsWorkingLine(bool split)
    {
        var rows = DiffLensRows.Build("before\nafter", "before\ninserted\nafter", 0, split);
        var index = rows.ToList().FindIndex(row => row.Right?.Text == "inserted");
        Assert.That(DiffLensRows.FindWorkingLine(rows, index), Is.EqualTo(2));
    }

    [Test]
    public void ClickingDeletedLineTargetsNextSurvivingWorkingLine()
    {
        var rows = DiffLensRows.Build("before\ndeleted\nafter", "before\nafter", 4, false);
        var index = rows.ToList().FindIndex(row => row.Left?.Text == "deleted");
        Assert.That(DiffLensRows.FindWorkingLine(rows, index), Is.EqualTo(2));
    }

    [Test]
    public void ClickingDeletedEndTargetsPreviousSurvivingWorkingLine()
    {
        var rows = DiffLensRows.Build("before\ndeleted", "before", 4, false);
        var index = rows.ToList().FindIndex(row => row.Left?.Text == "deleted");
        Assert.That(DiffLensRows.FindWorkingLine(rows, index), Is.EqualTo(1));
    }

    [Test]
    public void TrackpadPinchAppliesIncrementalZoomAndConsumesGesture()
    {
        var canvas = new DiffLensControl();
        var initialZoom = canvas.Zoom;
        var pointer = new Pointer(1, PointerType.Mouse, true);
        foreach (var delta in new[] { 0.2, -0.1 })
        {
            var gesture = new PointerDeltaEventArgs(
                Gestures.PointerTouchPadGestureMagnifyEvent, canvas, pointer, canvas,
                new Point(100, 80), 0, new PointerPointProperties(), KeyModifiers.None,
                new Vector(delta, delta));

            canvas.RaiseEvent(gesture);

            Assert.That(gesture.Handled, Is.True);
        }

        Assert.That(canvas.Zoom, Is.EqualTo(initialZoom * 1.2 * 0.9).Within(0.000001));
    }

    [TestCase(100, 2.5)]
    [TestCase(-1, 0.001)]
    public void TrackpadPinchRespectsZoomLimits(double delta, double expectedZoom)
    {
        var canvas = new DiffLensControl();
        var gesture = new PointerDeltaEventArgs(
            Gestures.PointerTouchPadGestureMagnifyEvent, canvas,
            new Pointer(1, PointerType.Mouse, true), canvas, new Point(100, 80), 0,
            new PointerPointProperties(), KeyModifiers.None, new Vector(delta, delta));

        canvas.RaiseEvent(gesture);

        Assert.That(canvas.Zoom, Is.EqualTo(expectedZoom));
    }


    [TestCase(KeyModifiers.None)]
    [TestCase(KeyModifiers.Control)]
    [TestCase(KeyModifiers.Meta)]
    public void ScrollZoomUsesControlOrMacCommandAndLeavesPlainScrollingForPanning(KeyModifiers modifiers)
    {
        var canvas = new DiffLensControl();
        var initialZoom = canvas.Zoom;
        var wheel = new PointerWheelEventArgs(
            canvas, new Pointer(1, PointerType.Mouse, true), canvas, new Point(100, 80), 0,
            new PointerPointProperties(), modifiers, new Vector(0.25, 0.5));

        canvas.RaiseEvent(wheel);

        var zooms = modifiers == KeyModifiers.Control ||
                    (OperatingSystem.IsMacOS() && modifiers == KeyModifiers.Meta);
        Assert.That(canvas.Zoom, Is.EqualTo(zooms ? initialZoom * Math.Pow(1.15, 0.5) : initialZoom).Within(0.000001));
        Assert.That(wheel.Handled, Is.True);
    }

}

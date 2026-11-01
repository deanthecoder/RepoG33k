// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Views.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensFilePanelTests
{
    [Test]
    public void SelectionKeepsOnlyOneHighlightedFileAcrossComparisonReloads()
    {
        DiffLensEntry Entry(string path) => new(path,
            new DiffLensFile(Path.Combine(Path.GetTempPath(), path), path, "HEAD", "old", Encoding.UTF8.GetBytes("new")), null);
        var first = Entry("first.cs");
        var second = Entry("second.cs");
        var panel = new DiffLensFilePanel(true);
        var root = (Grid)((Border)panel.Content).Child;
        var expanded = (Grid)root.Children[0];
        var content = (Grid)expanded.Children[1];
        var rows = (StackPanel)((ScrollViewer)content.Children[0]).Content;

        void AssertSelected(string path)
        {
            var selected = rows.Children.OfType<Button>()
                .Where(button => button.Background is ISolidColorBrush brush && brush.Color == Color.Parse("#233E3B"))
                .Select(button => ((TextBlock)button.Content).Text).ToArray();
            Assert.That(selected, Is.EqualTo(new[] { path }));
        }

        panel.UpdateFiles(Path.GetTempPath(), new DiffLensScope("unstaged", "Unstaged"), [first, second], first.Path);
        AssertSelected(first.Path);
        panel.SelectPath(second.Path);
        AssertSelected(second.Path);
        panel.UpdateFiles(Path.GetTempPath(), new DiffLensScope("unstaged", "Unstaged"), [first, second], first.Path);
        panel.SelectPath(second.Path);
        panel.SelectPath(first.Path);
        AssertSelected(first.Path);
        Assert.That(rows.Children.Count, Is.EqualTo(2));
    }

    [Test]
    public void FileMenuIncludesHostGitActionsAndReveal()
    {
        var path = "sample.cs";
        var file = new DiffLensFile(Path.Combine(Path.GetTempPath(), path), path, "HEAD", "old", Encoding.UTF8.GetBytes("new"));
        var panel = new DiffLensFilePanel(true) { CreateGitActions = _ => [new MenuItem { Header = "Stage" }] };
        panel.UpdateFiles(Path.GetTempPath(), new DiffLensScope("unstaged", "Unstaged"), [new DiffLensEntry(path, file, null)], path);
        var root = (Grid)((Border)panel.Content).Child;
        var content = (Grid)((Grid)root.Children[0]).Children[1];
        var rows = (StackPanel)((ScrollViewer)content.Children[0]).Content;
        var menu = ((Button)rows.Children[0]).ContextMenu;
        var headers = ((IEnumerable<MenuItem>)menu.ItemsSource).Select(item => item.Header.ToString()).ToArray();
        Assert.That(headers[0], Is.EqualTo("Stage"));
        Assert.That(headers[1], Does.StartWith("Reveal in"));
    }
}

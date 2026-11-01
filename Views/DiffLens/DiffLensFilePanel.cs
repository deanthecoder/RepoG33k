// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Views.DiffLens;

/// <summary>Shows the files in the current DiffLens comparison.</summary>
/// <remarks>Both hosts share navigation and reveal actions; RepoG33k supplies optional Git actions.</remarks>
internal sealed class DiffLensFilePanel : UserControl
{
    private readonly StackPanel m_rows = new();
    private readonly Dictionary<string, Button> m_buttons = new(StringComparer.Ordinal);
    private readonly TextBlock m_empty = new() { Text = "No files in this comparison", FontSize = 11, Foreground = Brush.Parse("#8B9DAF"), Margin = new Thickness(12, 8), IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly Grid m_expanded = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly CodeLocationOpener m_opener = new();
    private string m_selectedPath;
    private string m_root;

    public event Action<string> FileSelected;
    public event Action<bool> ExpandedChanged;
    public Func<string, IReadOnlyList<MenuItem>> CreateGitActions { get; set; }
    public Action<string> ReportError { get; set; }
    public bool IsExpanded { get; private set; }

    public DiffLensFilePanel(bool expanded)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Files", FontSize = 18 });
        m_expanded.Children.Add(header);
        var content = new Grid();
        content.Children.Add(new ScrollViewer { Content = m_rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        content.Children.Add(m_empty);
        Grid.SetRow(content, 1);
        m_expanded.Children.Add(content);
        var root = new Grid();
        root.Children.Add(m_expanded);
        Content = new Border { Background = Brush.Parse("#121C27"), BorderBrush = Brush.Parse("#2A3948"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(12, 12, 0, 0), Padding = new Thickness(14), Child = root };
        IsExpanded = expanded;
        ApplyExpansion();
    }

    private Button CreateRow(DiffLensEntry entry)
    {
        var text = new TextBlock { Text = entry.Path, FontSize = 11, Foreground = Brush.Parse("#D9E3EE"), TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new Button
        {
            Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 7), CornerRadius = new CornerRadius(4)
        };
        Avalonia.Automation.AutomationProperties.SetName(row, entry.Path);
        ToolTip.SetTip(row, entry.Error ?? entry.Path);
        row.Click += (_, _) => FileSelected?.Invoke(entry.Path);
        row.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down))
                return;
            var index = m_rows.Children.IndexOf(row) + (e.Key == Key.Up ? -1 : 1);
            if (index >= 0 && index < m_rows.Children.Count && m_rows.Children[index] is Button next)
            {
                next.Focus();
                FileSelected?.Invoke(Avalonia.Automation.AutomationProperties.GetName(next));
            }
            e.Handled = true;
        };
        var menu = new ContextMenu();
        void PopulateMenu()
        {
            var items = new List<MenuItem>();
            if (CreateGitActions != null)
                items.AddRange(CreateGitActions(entry.Path));
            var fullPath = Path.Combine(m_root, entry.Path);
            var reveal = new MenuItem { Header = "Reveal in " + m_opener.GetDisplayName(CodeLocationOpenTarget.FileBrowser), IsEnabled = File.Exists(fullPath) };
            reveal.Click += (_, _) =>
            {
                if (!m_opener.TryOpenAtLocation(CodeLocationOpenTarget.FileBrowser, fullPath, 1, out var error))
                    ReportError?.Invoke(error);
            };
            items.Add(reveal);
            menu.ItemsSource = items;
        }
        PopulateMenu();
        menu.Opening += (_, _) => PopulateMenu();
        row.ContextMenu = menu;
        row.ContextRequested += (_, e) => { menu.Open(row); e.Handled = true; };
        return row;
    }

    public void UpdateFiles(string root, DiffLensScope scope, IReadOnlyList<DiffLensEntry> entries, string selectedPath)
    {
        m_root = root;
        var files = entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Path, StringComparer.Ordinal).ToArray();
        m_empty.IsVisible = files.Length == 0;
        m_rows.Children.Clear();
        m_buttons.Clear();
        m_selectedPath = null;
        foreach (var entry in files)
        {
            var row = CreateRow(entry);
            m_buttons[entry.Path] = row;
            m_rows.Children.Add(row);
        }
        SelectPath(selectedPath);
    }

    public void SelectPath(string path)
    {
        if (path == m_selectedPath)
            return;
        if (m_selectedPath != null && m_buttons.TryGetValue(m_selectedPath, out var previous))
            SetSelected(previous, false);
        m_selectedPath = path != null && m_buttons.ContainsKey(path) ? path : null;
        if (m_selectedPath != null && m_buttons.TryGetValue(m_selectedPath, out var selected))
        {
            SetSelected(selected, true);
            if (IsExpanded)
                Dispatcher.UIThread.Post(() =>
                {
                    if (m_selectedPath == path && IsExpanded && m_buttons.TryGetValue(path, out var current) && ReferenceEquals(current, selected))
                        selected.BringIntoView();
                }, DispatcherPriority.Loaded);
        }
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Background = selected ? Brush.Parse("#233E3B") : Brushes.Transparent;
        Avalonia.Automation.AutomationProperties.SetHelpText(button, selected ? "Selected file" : null);
        if (button.Content is TextBlock text)
            text.Foreground = Brush.Parse(selected ? "#79DDC4" : "#D9E3EE");
    }

    public void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded)
            return;
        IsExpanded = expanded;
        ApplyExpansion();
        ExpandedChanged?.Invoke(expanded);
        if (expanded && m_selectedPath != null && m_buttons.TryGetValue(m_selectedPath, out var selected))
        {
            var path = m_selectedPath;
            Dispatcher.UIThread.Post(() =>
            {
                if (IsExpanded && m_selectedPath == path && m_buttons.TryGetValue(path, out var current) && ReferenceEquals(current, selected))
                    selected.BringIntoView();
            }, DispatcherPriority.Loaded);
        }
    }

    private void ApplyExpansion()
    {
        m_expanded.IsVisible = IsExpanded;

    }
}

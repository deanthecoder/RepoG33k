// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Collections.ObjectModel;
using Avalonia.Media;

namespace ReviewG33k.Services;

internal sealed class MainWindowLogFeedService
{
    private static readonly IBrush TimestampedLogBrush = Brushes.Gainsboro;
    private static readonly IBrush DetailLogBrush = Brushes.Gray;
    private static readonly IBrush ErrorLogBrush = Brushes.IndianRed;
    private static readonly IBrush WarningLogBrush = Brushes.Orange;
    private static readonly IBrush HintLogBrush = Brushes.LightSteelBlue;
    private static readonly IBrush PassLogBrush = Brushes.LimeGreen;

    public ObservableCollection<LogLineEntry> Entries { get; } = [];

    public void Append(string message, Action<LogLineEntry> onEntryAdded = null)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var normalized = (message ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineText = index == 0 ? $"[{timestamp}] {lines[index]}" : lines[index];
            var entry = new LogLineEntry(lineText, SelectLogLineBrush(lineText));
            Entries.Add(entry);
            onEntryAdded?.Invoke(entry);
        }
    }

    private static IBrush SelectLogLineBrush(string line)
    {
        if (ContainsErrorMarker(line))
            return ErrorLogBrush;
        if (ContainsPassMarker(line))
            return PassLogBrush;
        if (ContainsWarningMarker(line))
            return WarningLogBrush;
        if (ContainsHintMarker(line))
            return HintLogBrush;

        return HasTimestampPrefix(line) ? TimestampedLogBrush : DetailLogBrush;
    }

    private static bool ContainsErrorMarker(string line) =>
        line?.Contains("ERROR", StringComparison.OrdinalIgnoreCase) == true ||
        line?.Contains("fatal", StringComparison.OrdinalIgnoreCase) == true ||
        line?.Contains("IMPORTANT:", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsWarningMarker(string line) =>
        line?.Contains("CHECK WARNING:", StringComparison.OrdinalIgnoreCase) == true ||
        line?.Contains("SUGGESTION:", StringComparison.OrdinalIgnoreCase) == true ||
        line?.Contains("WARNING:", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsHintMarker(string line) =>
        line?.Contains("HINT:", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsPassMarker(string line) =>
        line?.Contains("CHECK PASS:", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasTimestampPrefix(string line) =>
        !string.IsNullOrEmpty(line) &&
        line.Length >= 10 &&
        line[0] == '[' &&
        line[3] == ':' &&
        line[6] == ':' &&
        line[9] == ']';
}

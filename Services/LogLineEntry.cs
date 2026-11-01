// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Media;

namespace ReviewG33k.Services;

internal sealed class LogLineEntry
{
    public LogLineEntry(string text, IBrush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }

    public IBrush Foreground { get; }
}

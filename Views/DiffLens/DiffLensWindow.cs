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
using Avalonia.Controls;
using Avalonia.Threading;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Views.DiffLens;

/// <summary>
/// Hosts the shared DiffLens view in ReviewG33k's standalone window.
/// </summary>
/// <remarks>
/// Both applications use the same canvas, comparisons, editor, and save lifecycle.
/// </remarks>
internal sealed class DiffLensWindow : Window
{
    private bool m_closeReady;
    private bool m_closing;

    public DiffLensWindow(string[] args = null, DiffLensReviewSnapshot review = null, CommandLineReviewMode? reviewMode = null)
    {
        var view = new DiffLensView(args, review, reviewMode);
        Content = view;
        Title = view.Title;
        Width = 1420;
        Height = 920;
        MinWidth = 950;
        MinHeight = 600;
        view.SnapshotChanged += (_, _) => Title = view.Title;
        Opened += async (_, _) => await view.InitializeAsync();
        Closing += async (_, e) =>
        {
            if (m_closeReady)
                return;
            e.Cancel = true;
            if (m_closing)
                return;
            m_closing = true;
            if (await view.PrepareToCloseAsync())
            {
                // Let macOS's native input callback finish before disposing its window.
                DispatcherTimer.RunOnce(() => { m_closeReady = true; Close(); }, TimeSpan.FromMilliseconds(1), DispatcherPriority.Background);
            }
            else
                m_closing = false;
        };
    }
}

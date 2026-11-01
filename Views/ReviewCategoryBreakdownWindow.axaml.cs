// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Markup.Xaml;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Views;

/// <summary>
/// Hosts the visual category breakdown for review findings, including the live pie chart and visibility toggles.
/// </summary>
/// <remarks>
/// Useful for helping users focus a review session by hiding or restoring whole issue categories while keeping the
/// underlying review-results state shared with the main results window for the current app session.
/// </remarks>
public partial class ReviewCategoryBreakdownWindow : global::Avalonia.Controls.Window
{
    internal ReviewCategoryBreakdownWindow(ReviewResultsWindowViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }
}

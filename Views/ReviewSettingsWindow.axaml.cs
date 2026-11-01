// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Views;

/// <summary>
/// Hosts persistent review-rule preferences for issue types shown by ReviewG33k.
/// </summary>
/// <remarks>
/// Useful for letting users tune future scans without affecting existing review-results windows.
/// </remarks>
public partial class ReviewSettingsWindow : global::Avalonia.Controls.Window
{
    private readonly ReviewSettingsWindowViewModel m_viewModel;

    internal ReviewSettingsWindow(ReviewSettingsWindowViewModel viewModel)
    {
        m_viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        m_viewModel?.Save();
        Close(true);
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        Close(false);
}

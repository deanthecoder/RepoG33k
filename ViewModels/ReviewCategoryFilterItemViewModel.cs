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
using DTC.Core.ViewModels;

namespace ReviewG33k.ViewModels;

/// <summary>
/// Represents a review-finding category with its count, accent color, and current visibility state.
/// </summary>
/// <remarks>
/// Useful for keeping category filtering and chart display synchronized across the review results window
/// and its category-breakdown companion window during the current app session.
/// </remarks>
public sealed class ReviewCategoryFilterItemViewModel : ViewModelBase
{
    private bool m_isVisible;

    public ReviewCategoryFilterItemViewModel(string categoryName, int count, string colorHex, bool isVisible)
    {
        CategoryName = string.IsNullOrWhiteSpace(categoryName)
            ? "Other"
            : categoryName.Trim();
        Count = count;
        var colorHex1 = string.IsNullOrWhiteSpace(colorHex)
            ? "#9FB0D0"
            : colorHex.Trim();
        m_isVisible = isVisible;
        ColorBrush = new SolidColorBrush(Color.Parse(colorHex1));
    }

    public string CategoryName { get; }

    public IBrush ColorBrush { get; }

    public int Count { get; }

    public bool IsVisible
    {
        get => m_isVisible;
        set
        {
            if (!SetField(ref m_isVisible, value))
                return;

            OnPropertyChanged(nameof(RowOpacity));
        }
    }

    public string CountText => $"{Count} issue(s)";

    public double RowOpacity => IsVisible ? 1.0 : 0.48;
}

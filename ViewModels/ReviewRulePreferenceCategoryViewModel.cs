// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReviewG33k.ViewModels;

/// <summary>
/// Groups review-rule preference rows under the category used by review results.
/// </summary>
/// <remarks>
/// Useful for keeping settings navigation aligned with the same issue categories users see after a scan.
/// </remarks>
public sealed class ReviewRulePreferenceCategoryViewModel
{
    public ReviewRulePreferenceCategoryViewModel(string categoryName, IEnumerable<ReviewRulePreferenceItemViewModel> rules)
    {
        CategoryName = string.IsNullOrWhiteSpace(categoryName)
            ? "Other"
            : categoryName.Trim();
        Rules = new ObservableCollection<ReviewRulePreferenceItemViewModel>((rules ?? []).Where(rule => rule != null));
    }

    public string CategoryName { get; }

    public ObservableCollection<ReviewRulePreferenceItemViewModel> Rules { get; }

    public string CountText => $"{Rules.Count} issue type(s)";
}

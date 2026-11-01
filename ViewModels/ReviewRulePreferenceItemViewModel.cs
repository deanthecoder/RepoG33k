// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.ViewModels;

namespace ReviewG33k.ViewModels;

/// <summary>
/// Represents one code-review rule in the review settings dialog.
/// </summary>
/// <remarks>
/// Useful for letting users persistently opt in or out of specific issue types without changing the
/// rule implementation itself.
/// </remarks>
public sealed class ReviewRulePreferenceItemViewModel : ViewModelBase
{
    private bool m_isEnabled;

    public ReviewRulePreferenceItemViewModel(string ruleId, string displayName, bool isEnabled)
    {
        RuleId = string.IsNullOrWhiteSpace(ruleId)
            ? string.Empty
            : ruleId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? RuleId
            : displayName.Trim();
        m_isEnabled = isEnabled;
    }

    public string RuleId { get; }

    public string DisplayName { get; }

    public bool IsEnabled
    {
        get => m_isEnabled;
        set => SetField(ref m_isEnabled, value);
    }
}

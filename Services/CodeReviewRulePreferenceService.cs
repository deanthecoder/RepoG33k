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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Services;

/// <summary>
/// Persists and resolves the code-review rule IDs that a user has disabled.
/// </summary>
/// <remarks>
/// Useful for keeping long-lived review preferences separate from the per-results-window visibility filters.
/// </remarks>
public sealed class CodeReviewRulePreferenceService
{
    private readonly Settings m_settings;

    public CodeReviewRulePreferenceService(Settings settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlySet<string> DisabledRuleIds => CreateRuleIdSet(m_settings.DisabledCodeReviewRuleIds);

    public bool IsRuleEnabled(string ruleId)
    {
        var normalizedRuleId = NormalizeRuleId(ruleId);
        return string.IsNullOrWhiteSpace(normalizedRuleId) ||
               !DisabledRuleIds.Contains(normalizedRuleId);
    }

    public void SaveDisabledRuleIds(IEnumerable<string> ruleIds)
    {
        m_settings.DisabledCodeReviewRuleIds = NormalizeRuleIds(ruleIds).ToArray();
        SaveSettingsSafely();
    }

    public void ResetToDefaults() =>
        SaveDisabledRuleIds([]);

    public static IReadOnlyList<string> NormalizeRuleIds(IEnumerable<string> ruleIds) =>
        (ruleIds ?? [])
            .Select(NormalizeRuleId)
            .Where(ruleId => !string.IsNullOrWhiteSpace(ruleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ruleId => ruleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeRuleId(string ruleId) =>
        string.IsNullOrWhiteSpace(ruleId)
            ? null
            : ruleId.Trim();

    private static IReadOnlySet<string> CreateRuleIdSet(IEnumerable<string> ruleIds) =>
        new HashSet<string>(NormalizeRuleIds(ruleIds), StringComparer.OrdinalIgnoreCase);

    private void SaveSettingsSafely()
    {
        try
        {
            m_settings.Save();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Failed to persist code review rule preferences: {exception}");
        }
    }
}

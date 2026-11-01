// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Text.RegularExpressions;

namespace RepoG33k.Services;

/// <summary>Applies a repository's commit prefix without asking the model to invent a story ID.</summary>
/// <remarks>Branch detection handles Jira IDs such as feature/TS-1234-SpeedUpApp and reports ambiguity.</remarks>
internal static partial class CommitConvention
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Z][A-Z0-9]*-\d+)(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex JiraIdPattern();

    [GeneratedRegex(@"^(?:\[[A-Z][A-Z0-9]*-\d+\]\s*|(?:Fix|Feature|Other):\s*)", RegexOptions.CultureInvariant)]
    private static partial Regex ExistingPrefixPattern();

    public static string[] DetectJiraIds(string branch) => JiraIdPattern().Matches(branch ?? string.Empty)
        .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

    public static string Apply(string summary, RepositoryPreferences preferences, string branch)
    {
        var text = ExistingPrefixPattern().Replace(summary?.Trim() ?? string.Empty, string.Empty);
        if (text.Length == 0)
            throw new InvalidOperationException("Enter a commit summary.");
        if (preferences.Convention == "jira")
        {
            var ids = DetectJiraIds(branch);
            var id = string.IsNullOrWhiteSpace(preferences.JiraOverride) ? ids.Length == 1 ? ids[0] : null : preferences.JiraOverride.Trim();
            if (id == null || DetectJiraIds(id) is not [var found] || found != id)
                throw new InvalidOperationException("Choose a Jira story ID in commit conventions. The branch must contain exactly one ID, or you can enter an override.");
            return $"[{id}] {text}";
        }
        var category = preferences.Category;
        if (category is not ("Fix" or "Feature" or "Other"))
            throw new InvalidOperationException("Choose Fix, Feature, or Other for this commit.");
        return $"{category}: {text}";
    }
}

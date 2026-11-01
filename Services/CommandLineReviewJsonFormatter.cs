// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services;

/// <summary>
/// Formats command-line review results as stable, machine-readable JSON.
/// </summary>
/// <remarks>
/// Provides a versioned contract for Codex skills and other automation without coupling them to UI models.
/// </remarks>
internal sealed class CommandLineReviewJsonFormatter
{
    private static readonly JsonSerializerSettings s_settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include
    };

    internal string FormatResult(CommandLineReviewOptions options, MainWindowReviewWorkflowApplyResult result)
    {
        var report = result?.Report;
        if (report == null)
            return FormatError("No report was produced.");

        var findings = report.Findings
            .Where(finding => finding.Severity != CodeReviewFindingSeverity.Ok)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.FilePath, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.LineNumber)
            .Select(finding => new
            {
                finding.RuleId,
                Severity = finding.Severity.ToString().ToLowerInvariant(),
                Category = CodeReviewFindingCategoryResolver.ResolveCategory(finding.RuleId),
                File = finding.FilePath,
                Line = finding.LineNumber,
                finding.Message
            })
            .ToList();

        return JsonConvert.SerializeObject(new
        {
            SchemaVersion = 1,
            Repository = options.RepositoryPath,
            Mode = options.Mode.ToString().ToLowerInvariant(),
            BaseBranch = options.Mode == CommandLineReviewMode.Committed
                ? result.ResolvedLocalBaseBranch ?? options.BaseBranch
                : null,
            Solution = result.SolutionPath,
            Summary = new { FindingCount = findings.Count },
            Findings = findings
        }, s_settings);
    }

    internal string FormatError(string message) =>
        JsonConvert.SerializeObject(new
        {
            SchemaVersion = 1,
            Error = message ?? "Review failed."
        }, s_settings);
}

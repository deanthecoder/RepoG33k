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
using System.Linq;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Services;

/// <summary>
/// Writes a code-smell analysis report to the application log in a readable, review-focused format.
/// </summary>
/// <remarks>
/// Useful for keeping log presentation rules consistent across review modes without repeating
/// finding/status formatting logic at each execution call site.
/// </remarks>
internal static class CodeSmellReportLogService
{
    private const string CheckErrorInfoPrefix = "CHECK ERROR:";

    public static CodeSmellReport ProcessReport(CodeSmellReport report, IReadOnlyList<ICodeReviewCheck> checks, Action<string> appendLog)
    {
        foreach (var info in report.Info)
            appendLog?.Invoke(info);

        if (DidAnalyzeChangedFiles(report))
            LogCodeSmellCheckStatuses(report, checks ?? [], appendLog);

        if (report.Findings.Count == 0)
        {
            appendLog?.Invoke("Code review scan: No findings.");
            return report;
        }

        appendLog?.Invoke($"Code review scan: {report.Findings.Count} finding(s).");
        foreach (var finding in report.Findings)
        {
            var severity = finding.Severity.ToString().ToUpperInvariant();
            var location = finding.LineNumber > 0 ? $"{finding.FilePath}:{finding.LineNumber}" : finding.FilePath;
            appendLog?.Invoke($"{severity}: [{location}] {finding.Message}");
        }

        return report;
    }

    private static void LogCodeSmellCheckStatuses(CodeSmellReport report, IReadOnlyList<ICodeReviewCheck> checks, Action<string> appendLog)
    {
        var failedRuleIds = GetFailedCheckRuleIds(report);
        foreach (var check in checks)
        {
            if (failedRuleIds.Contains(check.RuleId))
                continue;

            var count = report.Findings.Count(finding => finding.RuleId.Equals(check.RuleId, StringComparison.OrdinalIgnoreCase));
            if (count == 0)
                appendLog?.Invoke($"CHECK PASS: {check.DisplayName}");
        }
    }

    private static ISet<string> GetFailedCheckRuleIds(CodeSmellReport report)
    {
        var failedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in report?.Info ?? [])
        {
            if (string.IsNullOrWhiteSpace(info) ||
                !info.StartsWith(CheckErrorInfoPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var openBracketIndex = info.IndexOf('[');
            var closeBracketIndex = info.IndexOf(']', openBracketIndex + 1);
            if (openBracketIndex < 0 || closeBracketIndex <= openBracketIndex + 1)
                continue;

            var ruleId = info[(openBracketIndex + 1)..closeBracketIndex].Trim();
            if (!string.IsNullOrWhiteSpace(ruleId))
                failedRuleIds.Add(ruleId);
        }

        return failedRuleIds;
    }

    private static bool DidAnalyzeChangedFiles(CodeSmellReport report) =>
        report?.Info?.Any(info => info.StartsWith("Code review scan: Analyzing ", StringComparison.OrdinalIgnoreCase)) == true;
}

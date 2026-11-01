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
using System.IO;
using System.Linq;

namespace ReviewG33k.Services.Checks.Support;

public sealed class CodeReviewFixDispatcher : ICodeReviewFindingFixer
{
    private readonly Dictionary<string, IFixableCodeReviewCheck> m_fixersByRuleId;

    public CodeReviewFixDispatcher(IEnumerable<ICodeReviewCheck> checks)
    {
        m_fixersByRuleId = (checks ?? [])
            .OfType<IFixableCodeReviewCheck>()
            .Where(check => !string.IsNullOrWhiteSpace(check.RuleId))
            .GroupBy(check => check.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public bool CanFix(CodeSmellFinding finding)
    {
        if (finding == null || string.IsNullOrWhiteSpace(finding.RuleId))
            return false;

        return m_fixersByRuleId.TryGetValue(finding.RuleId, out var fixer) && fixer.CanFix(finding);
    }

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        resultMessage = null;
        if (finding == null || string.IsNullOrWhiteSpace(finding.RuleId))
        {
            resultMessage = "Finding is unavailable.";
            return false;
        }

        if (!m_fixersByRuleId.TryGetValue(finding.RuleId, out var fixer))
        {
            resultMessage = "No fixer is available for this finding.";
            return false;
        }

        if (!fixer.CanFix(finding))
        {
            resultMessage = "Finding is not fixable.";
            return false;
        }

        return fixer.TryFix(finding, resolvedFile, out resultMessage);
    }
}

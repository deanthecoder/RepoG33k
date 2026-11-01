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
using System.Linq;
using System.Text.RegularExpressions;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class LockTargetCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly Regex LockTargetRegex = new(@"\block\s*\(\s*(?<target>this|[A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override string RuleId => "lock-this-or-public";

    public override string DisplayName => "Lock(this) / lock on public objects";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            foreach (var lineNumber in file.AddedLineNumbers.OrderBy(n => n))
            {
                if (lineNumber < 1 || lineNumber > file.Lines.Count)
                    continue;
                var line = file.Lines[lineNumber - 1];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                foreach (Match lockMatch in LockTargetRegex.Matches(line))
                {
                    if (!lockMatch.Success)
                        continue;

                    var target = lockMatch.Groups["target"].Value;
                    if (target.Equals("this", StringComparison.OrdinalIgnoreCase))
                        AddFinding(report, CodeReviewFindingSeverity.Important, file.Path, lineNumber, "lock(this) detected.");
                    else if (CodeReviewCheckUtilities.LooksLikePublicLockObject(file.Lines, target))
                        AddFinding(report, CodeReviewFindingSeverity.Important, file.Path, lineNumber, $"Possible lock on public object '{target}'.");
                }
            }
        }
    }
}

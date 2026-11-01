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

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports JSON files containing a comma immediately before a closing object or array delimiter.
/// </summary>
/// <remarks>
/// Catches invalid JSON commonly left behind when a final key/value pair is removed from a changed file.
/// </remarks>
public sealed class JsonTrailingCommaCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.JsonTrailingComma;

    public override string DisplayName => "JSON trailing comma";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.AllChangedFiles)
        {
            if (!IsJsonFile(file.Path) || string.IsNullOrWhiteSpace(file.Text))
                continue;

            foreach (var lineNumber in FindTrailingCommaLines(file.Text))
            {
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    "JSON contains a trailing comma before a closing object or array delimiter.");
            }
        }
    }

    private static IEnumerable<int> FindTrailingCommaLines(string text)
    {
        var inString = false;
        var isEscaped = false;
        var lineNumber = 1;
        var lastToken = '\0';
        var lastTokenLineNumber = 1;

        foreach (var c in text ?? string.Empty)
        {
            if (c == '\n')
                lineNumber++;

            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
                continue;

            if ((c == '}' || c == ']') && lastToken == ',')
                yield return lastTokenLineNumber;

            lastToken = c;
            lastTokenLineNumber = lineNumber;
        }
    }

    private static bool IsJsonFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}

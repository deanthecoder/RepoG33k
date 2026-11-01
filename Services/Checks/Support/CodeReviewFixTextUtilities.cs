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
using Microsoft.CodeAnalysis.Text;

namespace ReviewG33k.Services.Checks.Support;

public static class CodeReviewFixTextUtilities
{
    public static string CollapseConsecutiveBlankLinesNearLine(string text, int centerLineIndex)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var sourceText = SourceText.From(text);
        var lines = sourceText.Lines;
        if (lines.Count == 0)
            return text;

        var start = Math.Max(0, centerLineIndex - 4);
        var end = Math.Min(lines.Count - 1, centerLineIndex + 4);

        var changes = new List<TextChange>();
        var blankRunStart = -1;

        for (var index = start; index <= end; index++)
        {
            var isBlank = string.IsNullOrWhiteSpace(lines[index].ToString());
            if (!isBlank)
            {
                blankRunStart = -1;
                continue;
            }

            if (blankRunStart < 0)
            {
                blankRunStart = index;
                continue;
            }

            var extraBlankLine = lines[index];
            var deleteSpan = TextSpan.FromBounds(extraBlankLine.Start, extraBlankLine.EndIncludingLineBreak);
            changes.Add(new TextChange(deleteSpan, string.Empty));
        }

        if (changes.Count == 0)
            return text;

        return sourceText.WithChanges(changes).ToString();
    }
}

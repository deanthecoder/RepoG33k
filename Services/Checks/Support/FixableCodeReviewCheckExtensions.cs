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
using System.IO;
using DTC.Core.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace ReviewG33k.Services.Checks.Support;

public static class FixableCodeReviewCheckExtensions
{
    public static bool TryPrepareFix(
        this IFixableCodeReviewCheck check,
        CodeSmellFinding finding,
        FileInfo resolvedFile,
        out SourceText sourceText,
        out int lineIndex,
        out string resultMessage)
    {
        sourceText = null;
        lineIndex = -1;
        resultMessage = null;

        if (finding == null)
        {
            resultMessage = "Finding is required.";
            return false;
        }

        if (check == null || !check.CanFix(finding))
        {
            resultMessage = "Finding is not fixable.";
            return false;
        }

        if (resolvedFile?.Exists() != true)
        {
            resultMessage = "File path could not be resolved.";
            return false;
        }

        string text;
        try
        {
            text = resolvedFile.ReadAllText();
        }
        catch (Exception exception)
        {
            resultMessage = $"Could not read file: {exception.Message}";
            return false;
        }

        sourceText = SourceText.From(text);
        lineIndex = finding.LineNumber - 1;
        if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
        {
            resultMessage = "Finding line number is out of range for this file.";
            sourceText = null;
            lineIndex = -1;
            return false;
        }

        return true;
    }

    public static bool TryWriteUpdatedText(
        this IFixableCodeReviewCheck _,
        FileInfo resolvedFile,
        string updatedText,
        out string resultMessage)
    {
        resultMessage = null;

        if (resolvedFile == null)
        {
            resultMessage = "File path could not be resolved.";
            return false;
        }

        if (updatedText == null)
        {
            resultMessage = "Updated text is required.";
            return false;
        }

        try
        {
            resolvedFile.WriteAllText(updatedText);
            return true;
        }
        catch (Exception exception)
        {
            resultMessage = $"Could not write file: {exception.Message}";
            return false;
        }
    }
}

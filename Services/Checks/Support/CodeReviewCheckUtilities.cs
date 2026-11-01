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
using System.Text.RegularExpressions;

namespace ReviewG33k.Services.Checks.Support;

internal static class CodeReviewCheckUtilities
{
    private static readonly Regex CatchOpenRegex = new(@"\bcatch\b\s*(?:\([^)]*\))?\s*\{", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"//|/\*", RegexOptions.Compiled);
    private static readonly Regex LoggingCallRegex = new(
        @"(?<!\w)(?:Log|Logger|AppendLog|Trace|Debug|Warn|Warning|Error|Fatal|Console\.Write(?:Line)?)\w*(?:<[^>]+>)?\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DocumentableTypeDeclarationRegex = new(
        @"^\s*(?<modifiers>(?:(?:public|internal|sealed|abstract|partial|static)\s+)+)\b(class|interface|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool LooksLikePublicLockObject(IReadOnlyList<string> fileLines, string lockTarget)
    {
        var fieldPattern = $@"\bpublic\s+(?:static\s+)?(?:readonly\s+)?(?:object|System\.Object)\s+{Regex.Escape(lockTarget)}\b";
        var propertyPattern = $@"\bpublic\s+(?:object|System\.Object)\s+{Regex.Escape(lockTarget)}\s*\{{";

        return fileLines.Any(line => Regex.IsMatch(line, fieldPattern, RegexOptions.IgnoreCase)) ||
               fileLines.Any(line => Regex.IsMatch(line, propertyPattern, RegexOptions.IgnoreCase));
    }

    public static bool TryGetPublicTypeDeclaration(string fileText, out string typeName, out int lineNumber)
    {
        typeName = null;
        lineNumber = 0;

        var match = DocumentableTypeDeclarationRegex.Match(fileText ?? string.Empty);
        if (!match.Success)
            return false;

        var modifiers = match.Groups["modifiers"].Value;
        if (!Regex.IsMatch(modifiers, @"\bpublic\b", RegexOptions.CultureInvariant))
            return false;

        typeName = match.Groups["name"].Value;
        lineNumber = (fileText ?? string.Empty)[..match.Index].Count(character => character == '\n') + 1;
        return !string.IsNullOrWhiteSpace(typeName);
    }

    public static IEnumerable<CatchBlockInfo> EnumerateAddedCatchBlocks(CodeReviewChangedFile file)
    {
        if (file.AddedLineNumbers.Count == 0 || string.IsNullOrWhiteSpace(file.Text))
            yield break;

        var lineStarts = BuildLineStartOffsets(file.Text);
        foreach (Match catchMatch in CatchOpenRegex.Matches(file.Text))
        {
            var openBraceIndex = file.Text.IndexOf('{', catchMatch.Index);
            if (openBraceIndex < 0)
                continue;

            var closeBraceIndex = FindMatchingBrace(file.Text, openBraceIndex);
            if (closeBraceIndex < 0)
                continue;

            var startLine = GetLineNumberFromOffset(lineStarts, catchMatch.Index);
            var endLine = GetLineNumberFromOffset(lineStarts, closeBraceIndex);
            if (!file.AddedLineNumbers.Any(line => line >= startLine && line <= endLine))
                continue;

            var body = file.Text.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
            var bodyWithoutComments = StripComments(body).Trim();
            var hasComments = ContainsComments(body);
            var exceptionVariableName = TryGetCatchExceptionVariableName(catchMatch.Value);
            var exceptionTypeName = TryGetCatchExceptionTypeName(catchMatch.Value);
            yield return new CatchBlockInfo(startLine, bodyWithoutComments, hasComments, exceptionVariableName, exceptionTypeName);
        }
    }

    public static bool ContainsRethrow(string catchBody) =>
        catchBody.Contains("throw;", StringComparison.Ordinal) ||
        Regex.IsMatch(catchBody, @"\bthrow\s+[A-Za-z_]", RegexOptions.Compiled);

    public static bool ContainsLoggingCall(string catchBody) =>
        !string.IsNullOrWhiteSpace(catchBody) &&
        LoggingCallRegex.IsMatch(catchBody);

    public static bool IsCancellationCatch(CatchBlockInfo catchBlock)
    {
        if (catchBlock == null || string.IsNullOrWhiteSpace(catchBlock.ExceptionTypeName))
            return false;

        var typeName = catchBlock.ExceptionTypeName
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Trim();

        return typeName.EndsWith(".OperationCanceledException", StringComparison.Ordinal) ||
               typeName.EndsWith(".TaskCanceledException", StringComparison.Ordinal) ||
               string.Equals(typeName, "OperationCanceledException", StringComparison.Ordinal) ||
               string.Equals(typeName, "TaskCanceledException", StringComparison.Ordinal);
    }

    private static int[] BuildLineStartOffsets(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }

        return starts.ToArray();
    }

    private static int GetLineNumberFromOffset(IReadOnlyList<int> lineStarts, int offset)
    {
        var low = 0;
        var high = lineStarts.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (lineStarts[mid] <= offset)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return Math.Max(1, high + 1);
    }

    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static string StripComments(string text)
    {
        var withoutBlockComments = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var withoutLineComments = Regex.Replace(withoutBlockComments, "//.*?$", string.Empty, RegexOptions.Multiline);
        return withoutLineComments;
    }

    private static bool ContainsComments(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        CommentRegex.IsMatch(text);

    private static string TryGetCatchExceptionVariableName(string catchHeaderText)
    {
        if (string.IsNullOrWhiteSpace(catchHeaderText))
            return null;

        var openParen = catchHeaderText.IndexOf('(');
        if (openParen < 0)
            return null;

        var closeParen = catchHeaderText.LastIndexOf(')');
        if (closeParen <= openParen)
            return null;

        var declaration = catchHeaderText.Substring(openParen + 1, closeParen - openParen - 1);
        if (string.IsNullOrWhiteSpace(declaration))
            return null;

        var identifierMatches = Regex.Matches(declaration, "@?[A-Za-z_][A-Za-z0-9_]*");
        if (identifierMatches.Count == 0)
            return null;

        var candidateMatch = identifierMatches[^1];
        if (candidateMatch.Index == 0)
            return null;

        if (!char.IsWhiteSpace(declaration[candidateMatch.Index - 1]))
            return null;

        return string.IsNullOrWhiteSpace(declaration[..candidateMatch.Index])
            ? null
            : candidateMatch.Value;
    }

    private static string TryGetCatchExceptionTypeName(string catchHeaderText)
    {
        if (string.IsNullOrWhiteSpace(catchHeaderText))
            return null;

        var openParen = catchHeaderText.IndexOf('(');
        if (openParen < 0)
            return null;

        var closeParen = catchHeaderText.LastIndexOf(')');
        if (closeParen <= openParen)
            return null;

        var declaration = catchHeaderText.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        if (string.IsNullOrWhiteSpace(declaration))
            return null;

        var whenIndex = declaration.IndexOf(" when ", StringComparison.Ordinal);
        if (whenIndex >= 0)
            declaration = declaration[..whenIndex].TrimEnd();

        var lastWhitespaceIndex = declaration.LastIndexOfAny([' ', '\t', '\r', '\n']);
        if (lastWhitespaceIndex < 0)
            return declaration;

        var trailingToken = declaration[(lastWhitespaceIndex + 1)..];
        if (!Regex.IsMatch(trailingToken, "^@?[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            return declaration;

        var typeName = declaration[..lastWhitespaceIndex].TrimEnd();
        return string.IsNullOrWhiteSpace(typeName)
            ? declaration
            : typeName;
    }
}

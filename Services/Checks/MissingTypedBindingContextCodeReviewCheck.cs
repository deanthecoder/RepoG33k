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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MissingTypedBindingContextCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly Regex TypedContextRegex = new(
        @"\bx:DataType\s*=|\bd:DataContext\s*=|\bd:DesignInstance\b|\bd:DesignData\b|<\s*Design\.DataContext\b|\bDataType\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExplicitSourceRegex = new(
        @"\bRelativeSource\s*=|\bElementName\s*=|\bSource\s*=|\bx:Reference\b|\$parent\b|\$self\b|\$this\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override string RuleId => CodeReviewRuleIds.MissingTypedBindingContext;

    public override string DisplayName => "UI bindings have typed/design data context";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            if (!CodeReviewFileClassification.IsMarkupFilePath(file.Path))
                continue;
            if (string.IsNullOrWhiteSpace(file.Text))
                continue;
            if (TypedContextRegex.IsMatch(file.Text))
                continue;

            var relevantBindings = GetDataContextDependentBindings(file.Text);
            if (relevantBindings.Count == 0)
                continue;
            if (!HasRelevantBindingChange(file, relevantBindings))
                continue;

            var lineNumber = FindFirstRelevantBindingLine(file, relevantBindings);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                "Markup has binding(s) but no typed/design data context hint (for example `x:DataType` or `d:DataContext`).");
        }
    }

    private static bool HasRelevantBindingChange(CodeReviewChangedFile file, IReadOnlyList<BindingExpressionRange> relevantBindings)
    {
        if (file == null)
            return false;
        if (relevantBindings == null || relevantBindings.Count == 0)
            return false;
        if (file.IsAdded)
            return true;

        foreach (var lineNumber in file.AddedLineNumbers)
        {
            if (lineNumber < 1 || lineNumber > file.Lines.Count)
                continue;

            if (relevantBindings.Any(binding => binding.ContainsLine(lineNumber)))
                return true;
        }

        return false;
    }

    private static int FindFirstRelevantBindingLine(CodeReviewChangedFile file, IReadOnlyList<BindingExpressionRange> relevantBindings)
    {
        if (file == null || file.Lines == null || file.Lines.Count == 0)
            return 1;
        if (relevantBindings == null || relevantBindings.Count == 0)
            return 1;

        if (!file.IsAdded)
        {
            var firstAddedBindingLine = file.AddedLineNumbers
                .OrderBy(line => line)
                .FirstOrDefault(line => line >= 1 &&
                                        line <= file.Lines.Count &&
                                        relevantBindings.Any(binding => binding.ContainsLine(line)));
            if (firstAddedBindingLine > 0)
                return firstAddedBindingLine;
        }

        return relevantBindings.Min(binding => binding.StartLine);
    }

    private static IReadOnlyList<BindingExpressionRange> GetDataContextDependentBindings(string markupText)
    {
        if (string.IsNullOrWhiteSpace(markupText))
            return [];

        var results = new List<BindingExpressionRange>();
        var length = markupText.Length;
        var index = 0;
        var currentLine = 1;

        while (index < length)
        {
            var current = markupText[index];
            if (current == '{' && TryReadBindingExpression(markupText, index, currentLine, out var binding))
            {
                if (!HasExplicitSource(binding.ExpressionText))
                    results.Add(binding);

                index = binding.EndIndex + 1;
                currentLine = binding.EndLine;
                continue;
            }

            if (current == '\n')
                currentLine++;

            index++;
        }

        return results;
    }

    private static bool TryReadBindingExpression(
        string markupText,
        int startIndex,
        int startLine,
        out BindingExpressionRange binding)
    {
        binding = default;
        if (!IsBindingStart(markupText, startIndex))
            return false;

        var depth = 0;
        var line = startLine;
        var index = startIndex;
        var length = markupText.Length;

        while (index < length)
        {
            var current = markupText[index];
            if (current == '{')
                depth++;
            else if (current == '}')
                depth--;

            if (current == '\n')
                line++;

            if (depth == 0)
            {
                binding = new BindingExpressionRange(index, startLine, line, markupText[startIndex..(index + 1)]);
                return true;
            }

            index++;
        }

        binding = new BindingExpressionRange(length - 1, startLine, line, markupText[startIndex..]);
        return true;
    }

    private static bool IsBindingStart(string markupText, int startIndex)
    {
        if (startIndex < 0 || startIndex >= markupText.Length)
            return false;
        if (markupText[startIndex] != '{')
            return false;

        var index = startIndex + 1;
        while (index < markupText.Length && char.IsWhiteSpace(markupText[index]))
            index++;

        return HasToken(markupText, index, "Binding") || HasToken(markupText, index, "CompiledBinding");
    }

    private static bool HasToken(string text, int startIndex, string token)
    {
        if (startIndex < 0)
            return false;
        if (startIndex + token.Length > text.Length)
            return false;
        if (!text.AsSpan(startIndex, token.Length).Equals(token.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        var endIndex = startIndex + token.Length;
        return endIndex == text.Length || !IsIdentifierCharacter(text[endIndex]);
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static bool HasExplicitSource(string bindingExpressionText) =>
        !string.IsNullOrWhiteSpace(bindingExpressionText) && ExplicitSourceRegex.IsMatch(bindingExpressionText);

    private readonly record struct BindingExpressionRange(
        int EndIndex,
        int StartLine,
        int EndLine,
        string ExpressionText)
    {
        public bool ContainsLine(int lineNumber) => lineNumber >= StartLine && lineNumber <= EndLine;
    }
}

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
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class PublicMethodArgumentGuardsCodeReviewCheck : CodeReviewCheckBase
{
    private const int GuardWindowMaxLines = 24;

    private static readonly Regex PublicMethodSignatureRegex = new(
        @"^\s*public\s+(?:(?:static|virtual|override|sealed|abstract|unsafe|new|partial|extern|async)\s+)*(?<return>[A-Za-z_][A-Za-z0-9_<>,\.\[\]\?\s:]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>.*)\)\s*(?:where\s+.+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> KnownValueTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bool",
        "byte",
        "sbyte",
        "short",
        "ushort",
        "int",
        "uint",
        "long",
        "ulong",
        "nint",
        "nuint",
        "float",
        "double",
        "decimal",
        "char",
        "DateTime",
        "DateTimeOffset",
        "TimeSpan",
        "Guid",
        "IntPtr",
        "UIntPtr",
        "CancellationToken"
    };

    public override string RuleId => "public-method-argument-guards";

    public override string DisplayName => "New public methods guard nullable/reference args";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            if (CodeReviewFileClassification.IsTestFilePath(file.Path))
                continue;

            var semanticAnalysisAvailable = RoslynCodeReviewCheckUtilities.TryGetSemanticAnalysis(
                file,
                out var root,
                out var semanticModel,
                out var syntaxTree,
                out var diagnostics);
            if (semanticAnalysisAvailable && RoslynCodeReviewCheckUtilities.HasSourceErrorsForTree(diagnostics, syntaxTree))
                semanticAnalysisAvailable = false;

            foreach (var lineNumber in file.AddedLineNumbers.OrderBy(number => number))
            {
                if (!TryGetPublicMethodSignature(file.Lines, lineNumber, out var signature, out var bodyOpenLine, out var bodyCloseLine))
                    continue;
                if (IsProgramEntryPointSignature(signature))
                    continue;

                var parametersToGuard = GetParametersRequiringGuard(signature.ParameterListText);
                if (semanticAnalysisAvailable)
                {
                    parametersToGuard = FilterResolvedValueTypeParameters(
                        parametersToGuard,
                        root,
                        semanticModel,
                        lineNumber,
                        signature.MethodName);
                }
                if (parametersToGuard.Count == 0)
                    continue;

                var guardWindowLines = GetGuardWindow(file.Lines, bodyOpenLine, bodyCloseLine, maxLines: GuardWindowMaxLines);
                var methodBodyLines = GetMethodBodyLines(file.Lines, bodyOpenLine, bodyCloseLine);
                var unguardedParameters = parametersToGuard
                    .Where(parameter => ParameterHasUnsafeLocalUsage(methodBodyLines, parameter))
                    .Where(parameter => !HasArgumentGuard(guardWindowLines, parameter))
                    .ToArray();

                if (unguardedParameters.Length == 0)
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Public method '{signature.MethodName}' may be missing argument guard(s): {string.Join(", ", unguardedParameters)}.");
            }
        }
    }

    private static IReadOnlyList<string> FilterResolvedValueTypeParameters(
        IReadOnlyList<string> parameterNames,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        int methodLineNumber,
        string methodName)
    {
        if (parameterNames == null || parameterNames.Count == 0 || root == null || semanticModel == null)
            return parameterNames ?? [];

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(candidate =>
                RoslynCodeReviewCheckUtilities.GetStartLine(candidate) == methodLineNumber &&
                string.Equals(candidate.Identifier.ValueText, methodName, StringComparison.Ordinal));
        if (method == null)
            return parameterNames;

        var parametersByName = method.ParameterList?.Parameters
            .Where(parameter => parameter != null)
            .GroupBy(parameter => parameter.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, ParameterSyntax>(StringComparer.Ordinal);

        return parameterNames
            .Where(parameterName =>
                !parametersByName.TryGetValue(parameterName, out var parameterSyntax) ||
                semanticModel.GetDeclaredSymbol(parameterSyntax) is not IParameterSymbol parameterSymbol ||
                !parameterSymbol.Type.IsValueType)
            .ToArray();
    }

    private static bool TryGetPublicMethodSignature(
        IReadOnlyList<string> lines,
        int startLineNumber,
        out (string MethodName, string ParameterListText, string NormalizedSignatureText) signature,
        out int bodyOpenLine,
        out int bodyCloseLine)
    {
        signature = default;
        bodyOpenLine = 0;
        bodyCloseLine = 0;

        if (startLineNumber < 1 || startLineNumber > lines.Count)
            return false;

        var startIndex = startLineNumber - 1;
        var firstLine = lines[startIndex];
        if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.Contains("public", StringComparison.Ordinal) || !firstLine.Contains('('))
            return false;

        var signatureBuilder = new StringBuilder();
        var openParenthesisBalance = 0;
        var hasSeenOpenParenthesis = false;
        var foundBodyOpenBrace = false;

        for (var lineIndex = startIndex; lineIndex < lines.Count && lineIndex < startIndex + 16; lineIndex++)
        {
            var line = lines[lineIndex];
            if (signatureBuilder.Length > 0)
                signatureBuilder.Append(' ');

            signatureBuilder.Append(line.Trim());

            foreach (var character in line)
            {
                if (character == '(')
                {
                    openParenthesisBalance++;
                    hasSeenOpenParenthesis = true;
                }
                else if (character == ')')
                {
                    openParenthesisBalance--;
                }
            }

            if (line.Contains("=>", StringComparison.Ordinal))
                return false;

            if (line.Contains('{'))
            {
                bodyOpenLine = lineIndex + 1;
                foundBodyOpenBrace = true;
            }

            if (!hasSeenOpenParenthesis || openParenthesisBalance > 0)
                continue;

            if (!foundBodyOpenBrace && lineIndex + 1 < lines.Count)
            {
                var nextLine = lines[lineIndex + 1];
                if (nextLine.Contains('{'))
                {
                    bodyOpenLine = lineIndex + 2;
                    foundBodyOpenBrace = true;
                }
            }

            break;
        }

        if (!foundBodyOpenBrace || bodyOpenLine <= 0)
            return false;

        var normalizedSignature = Regex.Replace(signatureBuilder.ToString(), @"\s+", " ").Trim();
        var signatureMatch = PublicMethodSignatureRegex.Match(normalizedSignature);
        if (!signatureMatch.Success)
            return false;

        var methodName = signatureMatch.Groups["name"].Value;
        var parameterListText = signatureMatch.Groups["params"].Value;
        if (string.IsNullOrWhiteSpace(methodName))
            return false;

        bodyCloseLine = FindMethodBodyCloseLine(lines, bodyOpenLine);
        if (bodyCloseLine <= bodyOpenLine)
            return false;

        signature = (methodName, parameterListText, normalizedSignature);
        return true;
    }

    private static bool IsProgramEntryPointSignature((string MethodName, string ParameterListText, string NormalizedSignatureText) signature)
    {
        if (!string.Equals(signature.MethodName, "Main", StringComparison.Ordinal))
            return false;
        if (!Regex.IsMatch(signature.NormalizedSignatureText ?? string.Empty, @"\bstatic\b", RegexOptions.CultureInvariant))
            return false;

        var normalizedParameters = Regex.Replace(signature.ParameterListText ?? string.Empty, @"\s+", string.Empty);
        return string.IsNullOrWhiteSpace(normalizedParameters) ||
               string.Equals(normalizedParameters, "string[]args", StringComparison.Ordinal) ||
               string.Equals(normalizedParameters, "string[]@args", StringComparison.Ordinal);
    }

    private static int FindMethodBodyCloseLine(IReadOnlyList<string> lines, int bodyOpenLine)
    {
        var depth = 0;
        for (var lineIndex = bodyOpenLine - 1; lineIndex < lines.Count; lineIndex++)
        {
            foreach (var character in lines[lineIndex])
            {
                if (character == '{')
                    depth++;
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                        return lineIndex + 1;
                }
            }
        }

        return 0;
    }

    private static IReadOnlyList<string> GetGuardWindow(IReadOnlyList<string> lines, int bodyOpenLine, int bodyCloseLine, int maxLines)
    {
        var window = new List<string>();
        var startLineIndex = Math.Min(lines.Count - 1, bodyOpenLine);
        var endLineIndex = Math.Min(lines.Count - 1, bodyCloseLine - 2);

        for (var lineIndex = startLineIndex; lineIndex <= endLineIndex; lineIndex++)
        {
            var line = lines[lineIndex].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            window.Add(line);
            if (window.Count >= maxLines)
                break;
        }

        return window;
    }

    private static IReadOnlyList<string> GetMethodBodyLines(IReadOnlyList<string> lines, int bodyOpenLine, int bodyCloseLine)
    {
        var bodyLines = new List<string>();
        var startLineIndex = Math.Min(lines.Count - 1, bodyOpenLine);
        var endLineIndex = Math.Min(lines.Count - 1, bodyCloseLine - 2);

        for (var lineIndex = startLineIndex; lineIndex <= endLineIndex; lineIndex++)
            bodyLines.Add(lines[lineIndex]);

        return bodyLines;
    }

    private static IReadOnlyList<string> GetParametersRequiringGuard(string parameterListText)
    {
        var parameterNames = new List<string>();
        if (string.IsNullOrWhiteSpace(parameterListText))
            return parameterNames;

        foreach (var rawParameter in SplitParameters(parameterListText))
        {
            if (HasParameterModifier(rawParameter, "this"))
                continue;
            if (HasParameterModifier(rawParameter, "out"))
                continue;
            if (IsOptionalParameter(rawParameter))
                continue;

            if (!TryParseParameter(rawParameter, out var typeName, out var parameterName))
                continue;

            if (string.IsNullOrWhiteSpace(parameterName) || !RequiresNullGuard(typeName))
                continue;

            parameterNames.Add(parameterName);
        }

        return parameterNames;
    }

    private static bool HasParameterModifier(string rawParameter, string modifier)
    {
        if (string.IsNullOrWhiteSpace(rawParameter) || string.IsNullOrWhiteSpace(modifier))
            return false;

        var parameterText = StripLeadingParameterAttributes(rawParameter);
        parameterText = parameterText.TrimStart();
        if (parameterText.Length == 0)
            return false;

        return Regex.IsMatch(
            parameterText,
            $@"^(?:scoped\s+)?{Regex.Escape(modifier)}\b",
            RegexOptions.IgnoreCase);
    }

    private static bool IsOptionalParameter(string rawParameter)
    {
        if (string.IsNullOrWhiteSpace(rawParameter))
            return false;

        var parameterText = StripLeadingParameterAttributes(rawParameter);
        return parameterText.IndexOf('=') >= 0;
    }

    private static IEnumerable<string> SplitParameters(string parameterListText)
    {
        var current = new StringBuilder();
        var depthAngle = 0;
        var depthParen = 0;
        var depthBracket = 0;

        foreach (var character in parameterListText)
        {
            switch (character)
            {
                case '<':
                    depthAngle++;
                    break;
                case '>':
                    depthAngle = Math.Max(0, depthAngle - 1);
                    break;
                case '(':
                    depthParen++;
                    break;
                case ')':
                    depthParen = Math.Max(0, depthParen - 1);
                    break;
                case '[':
                    depthBracket++;
                    break;
                case ']':
                    depthBracket = Math.Max(0, depthBracket - 1);
                    break;
                case ',' when depthAngle == 0 && depthParen == 0 && depthBracket == 0:
                    yield return current.ToString();
                    current.Clear();
                    continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static bool TryParseParameter(string rawParameter, out string typeName, out string parameterName)
    {
        typeName = null;
        parameterName = null;

        if (string.IsNullOrWhiteSpace(rawParameter))
            return false;

        var parameterText = StripLeadingParameterAttributes(rawParameter);
        parameterText = parameterText.Trim();
        if (parameterText.Length == 0)
            return false;

        var equalsIndex = parameterText.IndexOf('=');
        if (equalsIndex >= 0)
            parameterText = parameterText[..equalsIndex].Trim();

        var tokens = parameterText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !token.Equals("this", StringComparison.OrdinalIgnoreCase))
            .Where(token => !token.Equals("ref", StringComparison.OrdinalIgnoreCase))
            .Where(token => !token.Equals("out", StringComparison.OrdinalIgnoreCase))
            .Where(token => !token.Equals("in", StringComparison.OrdinalIgnoreCase))
            .Where(token => !token.Equals("params", StringComparison.OrdinalIgnoreCase))
            .Where(token => !token.Equals("scoped", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (tokens.Length < 2)
            return false;

        parameterName = tokens[^1].TrimStart('@');
        typeName = string.Join(' ', tokens.Take(tokens.Length - 1));
        return parameterName.Length > 0 && typeName.Length > 0;
    }

    private static string StripLeadingParameterAttributes(string rawParameter)
    {
        var parameterText = rawParameter ?? string.Empty;
        while (true)
        {
            var attributeMatch = Regex.Match(parameterText, @"^\s*\[[^\]]+\]\s*");
            if (!attributeMatch.Success)
                break;

            parameterText = parameterText[attributeMatch.Length..];
        }

        return parameterText;
    }

    private static bool RequiresNullGuard(string typeName)
    {
        var normalizedTypeName = typeName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTypeName))
            return false;

        if (normalizedTypeName.StartsWith("Nullable<", StringComparison.Ordinal) ||
            normalizedTypeName.StartsWith("System.Nullable<", StringComparison.Ordinal))
            return true;

        if (normalizedTypeName.EndsWith("?", StringComparison.Ordinal))
            return true;

        if (normalizedTypeName.StartsWith("(", StringComparison.Ordinal))
            return false;

        if (KnownValueTypes.Contains(normalizedTypeName))
            return false;

        return true;
    }

    private static bool HasArgumentGuard(IReadOnlyList<string> methodOpeningLines, string parameterName)
    {
        if (methodOpeningLines.Count == 0 || string.IsNullOrWhiteSpace(parameterName))
            return false;

        var escapedParameterName = Regex.Escape(parameterName);
        var throwIfNullPattern = $@"\bArgumentNullException\.ThrowIfNull\s*\(\s*{escapedParameterName}\s*\)";
        var nullCheckPattern = $@"\bif\s*\([^)]*\b{escapedParameterName}\s*(?:==\s*null|is\s+null)\b[^)]*\)";
        var nullCoalesceThrowPattern = $@"\b{escapedParameterName}\s*\?\?\s*throw\s+new\s+ArgumentNullException\b";
        var stringGuardPattern = $@"\b(?:(?:global::)?System\.)?String\.(?:IsNullOrEmpty|IsNullOrWhiteSpace)\s*\(\s*{escapedParameterName}\s*\)";
        var stringAliasGuardPattern = $@"\bstring\.(?:IsNullOrEmpty|IsNullOrWhiteSpace)\s*\(\s*{escapedParameterName}\s*\)";
        var guardAgainstPattern = $@"\bGuard\.Against\.Null\s*\(\s*{escapedParameterName}\s*[,\)]";

        foreach (var line in methodOpeningLines)
        {
            if (Regex.IsMatch(line, throwIfNullPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, nullCheckPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, nullCoalesceThrowPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, stringGuardPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, stringAliasGuardPattern, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, guardAgainstPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ParameterHasUnsafeLocalUsage(IReadOnlyList<string> methodBodyLines, string parameterName)
    {
        if (methodBodyLines == null || methodBodyLines.Count == 0 || string.IsNullOrWhiteSpace(parameterName))
            return false;

        var bodyText = string.Join('\n', methodBodyLines);
        var withoutBlockComments = Regex.Replace(bodyText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var withoutLineComments = Regex.Replace(withoutBlockComments, "//.*?$", string.Empty, RegexOptions.Multiline);
        var escapedParameterName = Regex.Escape(parameterName);

        if (Regex.IsMatch(withoutLineComments, $@"\bforeach\s*\([^)]*\bin\s+{escapedParameterName}\b", RegexOptions.Compiled))
            return true;
        if (Regex.IsMatch(withoutLineComments, $@"\b{escapedParameterName}\b\s*\.", RegexOptions.Compiled))
            return true;
        if (Regex.IsMatch(withoutLineComments, $@"\b{escapedParameterName}\b\s*\[", RegexOptions.Compiled))
            return true;
        if (Regex.IsMatch(withoutLineComments, $@"\b{escapedParameterName}\b\s*\(", RegexOptions.Compiled))
            return true;

        return false;
    }
}

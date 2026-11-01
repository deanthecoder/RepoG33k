// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class SwallowingCatchCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly Regex ReturnSuccessRegex = new(@"\breturn\s+true\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReturnFailureRegex = new(@"\breturn\s+(false|null|default)\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ErrorFlagAssignmentRegex = new(@"\b\w*(error|fail|fault|invalid)\w*\s*=\s*true\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LikelyCleanupCallRegex = new(@"\b(Cleanup|Dispose|Close|Release|Rollback)\w*\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override string RuleId => "swallowing-catch";

    public override string DisplayName => "Exception swallowing in catch";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var tryPatternCatchStartLines = GetTryPatternCatchStartLines(file.Text);
            foreach (var catchBlock in CodeReviewCheckUtilities.EnumerateAddedCatchBlocks(file))
            {
                if (CodeReviewCheckUtilities.IsCancellationCatch(catchBlock))
                    continue;

                if (string.IsNullOrWhiteSpace(catchBlock.BodyWithoutComments))
                    continue;

                if (CodeReviewCheckUtilities.ContainsRethrow(catchBlock.BodyWithoutComments))
                    continue;

                if (UsesExceptionVariable(catchBlock))
                    continue;

                var (severity, message) = ClassifyCatchBody(catchBlock.BodyWithoutComments);
                if (severity == CodeReviewFindingSeverity.Ok)
                    continue;

                if (severity == CodeReviewFindingSeverity.Hint && tryPatternCatchStartLines.Contains(catchBlock.StartLine))
                    continue;

                AddFinding(report, severity, file.Path, catchBlock.StartLine, message);
            }
        }
    }

    private static bool UsesExceptionVariable(CatchBlockInfo catchBlock)
    {
        if (catchBlock == null ||
            string.IsNullOrWhiteSpace(catchBlock.ExceptionVariableName) ||
            string.IsNullOrWhiteSpace(catchBlock.BodyWithoutComments))
        {
            return false;
        }

        return Regex.IsMatch(
            catchBlock.BodyWithoutComments,
            $"(?<![A-Za-z0-9_]){Regex.Escape(catchBlock.ExceptionVariableName)}(?![A-Za-z0-9_])",
            RegexOptions.Compiled);
    }

    private static (CodeReviewFindingSeverity Severity, string Message) ClassifyCatchBody(string catchBody)
    {
        if (ReturnSuccessRegex.IsMatch(catchBody))
        {
            return (
                CodeReviewFindingSeverity.Important,
                "Catch block returns success despite handling an exception.");
        }

        if (ReturnFailureRegex.IsMatch(catchBody))
        {
            return (
                CodeReviewFindingSeverity.Hint,
                "Catch block converts an exception into a return value. Consider logging context or rethrowing when appropriate.");
        }

        if (ErrorFlagAssignmentRegex.IsMatch(catchBody))
        {
            return (
                CodeReviewFindingSeverity.Suggestion,
                "Catch block sets an error flag but does not rethrow. Consider stronger error propagation.");
        }

        if (CodeReviewCheckUtilities.ContainsLoggingCall(catchBody))
        {
            return (
                CodeReviewFindingSeverity.Suggestion,
                "Catch block logs an exception but does not rethrow or return a failure signal.");
        }

        if (LikelyCleanupCallRegex.IsMatch(catchBody))
        {
            return (
                CodeReviewFindingSeverity.Suggestion,
                "Catch block performs cleanup but appears to swallow the exception.");
        }

        return (
            CodeReviewFindingSeverity.Important,
            "Catch block appears to swallow exceptions.");
    }

    private static HashSet<int> GetTryPatternCatchStartLines(string sourceText)
    {
        var tryPatternCatchStartLines = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(sourceText))
            return tryPatternCatchStartLines;

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetCompilationUnitRoot();
        foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            var method = catchClause.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (!IsTryBooleanMethod(method))
                continue;

            var catchLineSpan = syntaxTree.GetLineSpan(catchClause.Span);
            tryPatternCatchStartLines.Add(catchLineSpan.StartLinePosition.Line + 1);
        }

        return tryPatternCatchStartLines;
    }

    private static bool IsTryBooleanMethod(MethodDeclarationSyntax method)
    {
        if (method == null)
            return false;

        if (!method.Identifier.ValueText.StartsWith("Try", System.StringComparison.Ordinal))
            return false;

        var returnTypeText = method.ReturnType.ToString();
        return returnTypeText == "bool" ||
               returnTypeText == "System.Boolean" ||
               returnTypeText == "global::System.Boolean";
    }
}

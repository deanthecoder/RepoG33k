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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects culture-sensitive numeric-to-string formatting in file-writing paths.
/// </summary>
/// <remarks>
/// Useful for preventing locale-specific output issues when persisting numeric values to files (for
/// example decimal comma vs decimal point). The check flags floating-point string conversions that do
/// not use invariant culture so file content stays stable across developer machines and environments.
/// </remarks>
public sealed class NumericStringCultureForFileWriteCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    private static readonly HashSet<string> ContentParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "contents",
        "value",
        "text",
        "s",
        "format"
    };

    private static readonly HashSet<string> PathParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "path",
        "fileName",
        "filename",
        "filePath",
        "filepath"
    };

    public override string RuleId => CodeReviewRuleIds.NumericStringCultureForFileWrite;

    public override string DisplayName => "Numeric formatting for file writes uses invariant culture";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                continue;

            var invocationOperation = semanticModel.GetOperation(invocation) as IInvocationOperation;
            if (invocationOperation == null)
                continue;
            if (!TryGetCandidateContentArguments(invocationOperation, out var candidateArguments))
                continue;

            foreach (var candidateArgument in candidateArguments)
            {
                if (!TryFindCultureSensitiveNumericFormatting(candidateArgument.Value, out var location, out var reason))
                    continue;

                var lineNumber = location.GetLineSpan().StartLinePosition.Line + 1;
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    $"Numeric value is converted to text for file output using culture-sensitive formatting ({reason}). Use `CultureInfo.InvariantCulture` when persisting numeric text.");
                break;
            }
        }
    }

    private static bool TryGetCandidateContentArguments(
        IInvocationOperation invocationOperation,
        out IReadOnlyList<IArgumentOperation> candidateArguments)
    {
        candidateArguments = [];
        var method = invocationOperation.TargetMethod;
        if (method == null)
            return false;
        if (!IsSupportedFileWriteMethod(method))
            return false;

        var contentArguments = invocationOperation.Arguments
            .Where(argument => IsCandidateContentArgument(argument, method))
            .ToArray();
        if (contentArguments.Length == 0)
            return false;

        candidateArguments = contentArguments;
        return true;
    }

    private static bool IsSupportedFileWriteMethod(IMethodSymbol method)
    {
        var containingTypeName = method.ContainingType?.ToDisplayString();
        if (string.Equals(containingTypeName, "System.IO.File", StringComparison.Ordinal))
        {
            return string.Equals(method.Name, "WriteAllText", StringComparison.Ordinal) ||
                   string.Equals(method.Name, "AppendAllText", StringComparison.Ordinal);
        }

        if (string.Equals(containingTypeName, "DTC.Core.Extensions.FileInfoExtensions", StringComparison.Ordinal) ||
            string.Equals(containingTypeName, "DTC.Core.Extensions.TempFileExtensions", StringComparison.Ordinal))
        {
            return string.Equals(method.Name, "WriteAllText", StringComparison.Ordinal);
        }

        return IsTextWriterWriteMethod(method);
    }

    private static bool IsTextWriterWriteMethod(IMethodSymbol method)
    {
        if (!string.Equals(method.Name, "Write", StringComparison.Ordinal) &&
            !string.Equals(method.Name, "WriteLine", StringComparison.Ordinal))
        {
            return false;
        }

        for (var containingType = method.ContainingType; containingType != null; containingType = containingType.BaseType)
        {
            if (string.Equals(containingType.ToDisplayString(), "System.IO.TextWriter", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsCandidateContentArgument(IArgumentOperation argumentOperation, IMethodSymbol method)
    {
        var parameter = argumentOperation?.Parameter;
        if (parameter == null)
            return false;

        var parameterName = parameter.Name ?? string.Empty;
        if (PathParameterNames.Contains(parameterName))
            return false;
        if (ContentParameterNames.Contains(parameterName))
            return true;
        if (parameter.Type?.SpecialType == SpecialType.System_String)
            return true;

        return IsTextWriterWriteMethod(method) && string.Equals(parameterName, "value", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindCultureSensitiveNumericFormatting(
        IOperation operation,
        out Location location,
        out string reason)
    {
        location = null;
        reason = null;
        if (operation == null)
            return false;

        if (operation is IInvocationOperation invocationOperation &&
            IsNumericToStringInvocation(invocationOperation))
        {
            if (IsCultureSensitiveNumericToStringInvocation(invocationOperation))
            {
                location = invocationOperation.Syntax.GetLocation();
                reason = "`ToString(...)` without `InvariantCulture`";
                return true;
            }

            return false;
        }

        if (operation is IInterpolationOperation interpolationOperation &&
            IsFloatingPointNumericType(interpolationOperation.Expression?.Type))
        {
            location = interpolationOperation.Syntax.GetLocation();
            reason = "interpolated numeric value";
            return true;
        }

        if (operation is IConversionOperation conversionOperation &&
            conversionOperation.Type?.SpecialType == SpecialType.System_String &&
            IsFloatingPointNumericType(conversionOperation.Operand?.Type))
        {
            location = conversionOperation.Syntax.GetLocation();
            reason = "implicit numeric-to-string conversion";
            return true;
        }

        if (operation is IBinaryOperation binaryOperation &&
            binaryOperation.OperatorKind == BinaryOperatorKind.Add &&
            binaryOperation.Type?.SpecialType == SpecialType.System_String &&
            (IsFloatingPointNumericType(binaryOperation.LeftOperand?.Type) ||
             IsFloatingPointNumericType(binaryOperation.RightOperand?.Type)))
        {
            location = binaryOperation.Syntax.GetLocation();
            reason = "string concatenation with numeric value";
            return true;
        }

        foreach (var childOperation in operation.ChildOperations)
        {
            if (TryFindCultureSensitiveNumericFormatting(childOperation, out location, out reason))
                return true;
        }

        return false;
    }

    private static bool IsCultureSensitiveNumericToStringInvocation(IInvocationOperation invocationOperation)
    {
        if (invocationOperation == null)
            return false;
        if (!IsNumericToStringInvocation(invocationOperation))
            return false;

        var formatProviderArgument = invocationOperation.Arguments
            .FirstOrDefault(argument => IsFormatProviderParameter(argument.Parameter));
        if (formatProviderArgument == null)
            return true;

        return !IsInvariantCultureExpression(formatProviderArgument.Value);
    }

    private static bool IsNumericToStringInvocation(IInvocationOperation invocationOperation)
    {
        if (invocationOperation == null)
            return false;
        if (!string.Equals(invocationOperation.TargetMethod?.Name, "ToString", StringComparison.Ordinal))
            return false;

        return IsFloatingPointNumericType(invocationOperation.Instance?.Type);
    }

    private static bool IsFormatProviderParameter(IParameterSymbol parameterSymbol)
    {
        if (parameterSymbol?.Type == null)
            return false;

        var parameterType = parameterSymbol.Type;
        if (string.Equals(parameterType.OriginalDefinition?.ToDisplayString(), "System.IFormatProvider", StringComparison.Ordinal))
            return true;

        return parameterType.AllInterfaces.Any(interfaceSymbol =>
            string.Equals(interfaceSymbol.OriginalDefinition?.ToDisplayString(), "System.IFormatProvider", StringComparison.Ordinal));
    }

    private static bool IsInvariantCultureExpression(IOperation operation)
    {
        switch (operation)
        {
            case null:
                return false;

            case IConversionOperation conversionOperation:
                return IsInvariantCultureExpression(conversionOperation.Operand);

            case IPropertyReferenceOperation propertyReferenceOperation:
                return string.Equals(propertyReferenceOperation.Property?.Name, "InvariantCulture", StringComparison.Ordinal) &&
                       string.Equals(
                           propertyReferenceOperation.Property?.ContainingType?.ToDisplayString(),
                           "System.Globalization.CultureInfo",
                           StringComparison.Ordinal);

            default:
                return false;
        }
    }

    private static bool IsFloatingPointNumericType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        return typeSymbol.SpecialType is SpecialType.System_Double or
            SpecialType.System_Single or
            SpecialType.System_Decimal;
    }
}

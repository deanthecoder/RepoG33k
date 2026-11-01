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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports single-type source files whose filename does not match the contained type.
/// </summary>
/// <remarks>
/// Keeps simple one-type C# files easy to navigate by nudging new files toward the repository's expected filename convention.
/// </remarks>
public sealed class SourceFileNameMismatchCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.SourceFileNameMismatch;

    public override string DisplayName => "Source filename does not match type";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            if (file == null ||
                string.IsNullOrWhiteSpace(file.Path) ||
                !file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                CodeReviewFileClassification.IsGeneratedFilePath(file.Path))
            {
                continue;
            }

            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var topLevelTypes = root.Members
                .SelectMany(GetTopLevelTypes)
                .ToArray();
            if (topLevelTypes.Length != 1)
                continue;

            var type = topLevelTypes[0];
            if (!IsEligibleType(type))
                continue;
            var typeLine = RoslynCodeReviewCheckUtilities.GetStartLine(type);
            if (!file.IsAdded && !file.AddedLineNumbers.Contains(typeLine))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(file.Path);
            var typeName = GetTypeName(type);
            if (string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(typeName) ||
                string.Equals(fileName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                typeLine,
                $"File `{fileName}.cs` contains single type `{typeName}`. Consider renaming the file to `{typeName}.cs`.");
        }
    }

    private static MemberDeclarationSyntax[] GetTopLevelTypes(MemberDeclarationSyntax member) =>
        member switch
        {
            BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Members.SelectMany(GetTopLevelTypes).ToArray(),
            TypeDeclarationSyntax typeDeclaration when IsTopLevelType(typeDeclaration) => [typeDeclaration],
            EnumDeclarationSyntax enumDeclaration when IsTopLevelType(enumDeclaration) => [enumDeclaration],
            _ => []
        };

    private static bool IsEligibleType(MemberDeclarationSyntax type) =>
        type switch
        {
            TypeDeclarationSyntax typeDeclaration => !typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)),
            EnumDeclarationSyntax => true,
            _ => false
        };

    private static bool IsTopLevelType(MemberDeclarationSyntax type) =>
        type?.Ancestors().All(ancestor => ancestor is not TypeDeclarationSyntax) == true;

    private static string GetTypeName(MemberDeclarationSyntax type) =>
        type switch
        {
            TypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
            EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.ValueText,
            _ => string.Empty
        };
}

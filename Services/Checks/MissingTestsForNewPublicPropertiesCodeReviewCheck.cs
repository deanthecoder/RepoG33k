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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DTC.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Checks that newly added public properties have nearby test coverage changes.
/// </summary>
/// <remarks>
/// This mirrors the public-method coverage heuristic so property API changes get the same lightweight review prompt.
/// </remarks>
public sealed class MissingTestsForNewPublicPropertiesCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly Regex TestFixtureAttributeNameRegex = new(
        @"(?:^|\.)(?:TestFixture|TextFixture)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override string RuleId => CodeReviewRuleIds.MissingTestsForPublicProperties;

    public override string DisplayName => "New public properties have test changes";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        var changedTestFiles = context.Files
            .Where(CodeReviewFileClassification.IsLikelyTestCodeFile)
            .ToArray();
        var changedTestFileNames = new HashSet<string>(
            changedTestFiles.Select(file => Path.GetFileName(file.Path)),
            StringComparer.OrdinalIgnoreCase);
        var hasAnyChangedTests = changedTestFiles.Length > 0;
        var repositoryTestFilesByRoot = new Dictionary<string, IReadOnlyList<FileInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in context.Files)
        {
            if (CodeReviewFileClassification.IsTestFilePath(file.Path) ||
                CodeReviewFileClassification.IsGeneratedFilePath(file.Path) ||
                CodeReviewFileClassification.IsCodeBehindFilePath(file.Path) ||
                CodeReviewFileClassification.IsLikelyUiCodeFile(file))
                continue;

            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, property))
                    continue;
                if (!IsEligiblePublicProperty(property))
                    continue;

                var containingTypes = property.Ancestors().OfType<TypeDeclarationSyntax>().ToArray();
                var containingType = containingTypes.FirstOrDefault();
                if (containingType == null || containingType is InterfaceDeclarationSyntax)
                    continue;
                if (IsProgramType(containingType))
                    continue;
                if (IsLikelyTestFixtureType(containingType))
                    continue;

                var relevantTypeNames = GetRelevantTypeNames(containingTypes);
                if (relevantTypeNames.Count == 0)
                    continue;

                var propertyName = property.Identifier.ValueText;
                if (HasLikelyMatchingTestFile(changedTestFileNames, relevantTypeNames))
                    continue;
                if (HasLikelyExistingRepositoryTest(
                        file,
                        repositoryTestFilesByRoot,
                        relevantTypeNames,
                        propertyName))
                {
                    continue;
                }

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(property);
                var severity = hasAnyChangedTests
                    ? CodeReviewFindingSeverity.Hint
                    : CodeReviewFindingSeverity.Suggestion;

                AddFinding(
                    report,
                    severity,
                    file.Path,
                    lineNumber,
                    hasAnyChangedTests ? $"New public property '{propertyName}' has no likely matching unit test(s)." : $"Public property '{propertyName}' has no unit test changes.");
            }
        }
    }

    private static bool IsEligiblePublicProperty(PropertyDeclarationSyntax property)
    {
        if (property == null)
            return false;
        if (property.Modifiers.All(modifier => modifier.RawKind != (int)SyntaxKind.PublicKeyword))
            return false;
        if (RoslynCodeReviewCheckUtilities.IsExplicitInterfaceImplementation(property))
            return false;
        if (property.Modifiers.Any(modifier =>
            modifier.RawKind == (int)SyntaxKind.AbstractKeyword ||
            modifier.RawKind == (int)SyntaxKind.OverrideKeyword ||
            modifier.RawKind == (int)SyntaxKind.ExternKeyword))
        {
            return false;
        }

        return property.AccessorList != null || property.ExpressionBody != null;
    }

    private static bool HasLikelyMatchingTestFile(IReadOnlySet<string> changedTestFileNames, IReadOnlyCollection<string> typeNames)
    {
        if (changedTestFileNames == null || changedTestFileNames.Count == 0 || typeNames == null || typeNames.Count == 0)
            return false;

        return changedTestFileNames.Any(fileName =>
            typeNames.Any(typeName => IsLikelyMatchingTestFileName(fileName, typeName)));
    }

    private static bool HasLikelyExistingRepositoryTest(
        CodeReviewChangedFile file,
        IDictionary<string, IReadOnlyList<FileInfo>> repositoryTestFilesByRoot,
        IReadOnlyCollection<string> typeNames,
        string propertyName)
    {
        if (file == null ||
            typeNames == null ||
            typeNames.Count == 0 ||
            string.IsNullOrWhiteSpace(propertyName) ||
            !DuplicateCodeBlockUtilities.TryGetRepositoryRootPath(file, out var repositoryRootPath) ||
            string.IsNullOrWhiteSpace(repositoryRootPath))
        {
            return false;
        }

        if (!repositoryTestFilesByRoot.TryGetValue(repositoryRootPath, out var repositoryTestFiles))
        {
            repositoryTestFiles = EnumerateRepositoryTestFiles(repositoryRootPath);
            repositoryTestFilesByRoot[repositoryRootPath] = repositoryTestFiles;
        }

        return repositoryTestFiles.Any(testFile =>
            typeNames.Any(typeName => IsLikelyMatchingTestFileName(testFile.Name, typeName)) &&
            testFile.ReadAllText().Contains(propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetRelevantTypeNames(IEnumerable<TypeDeclarationSyntax> containingTypes)
    {
        if (containingTypes == null)
            return [];

        return containingTypes
            .Select(type => type?.Identifier.ValueText)
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<FileInfo> EnumerateRepositoryTestFiles(string repositoryRootPath) =>
        RepositoryTestFileSearch.Enumerate(repositoryRootPath);

    private static bool IsLikelyMatchingTestFileName(string fileName, string typeName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(typeName))
            return false;

        var exactName = $"{typeName}Tests.cs";
        if (string.Equals(fileName, exactName, StringComparison.OrdinalIgnoreCase))
            return true;

        return
            fileName.Contains(typeName, StringComparison.OrdinalIgnoreCase) &&
            fileName.Contains("Test", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyTestFixtureType(TypeDeclarationSyntax typeDeclaration)
    {
        if (typeDeclaration == null)
            return false;

        foreach (var attributeList in typeDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute?.Name?.ToString().Trim();
                if (string.IsNullOrWhiteSpace(attributeName))
                    continue;

                if (TestFixtureAttributeNameRegex.IsMatch(attributeName))
                    return true;
            }
        }

        return false;
    }

    private static bool IsProgramType(TypeDeclarationSyntax typeDeclaration) =>
        typeDeclaration != null &&
        string.Equals(typeDeclaration.Identifier.ValueText, "Program", StringComparison.Ordinal);
}

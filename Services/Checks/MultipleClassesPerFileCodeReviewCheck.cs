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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MultipleClassesPerFileCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.MultipleClassesPerFile;

    public override string DisplayName => "Multiple classes per file";

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

            var topLevelClasses = RoslynCodeReviewCheckUtilities.ParseRoot(file)
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(IsTopLevelClass)
                .OrderBy(classDeclaration => classDeclaration.SpanStart)
                .ToArray();
            if (topLevelClasses.Length <= 1)
                continue;

            var triggeringClass = GetTriggeringClass(file, topLevelClasses);
            if (triggeringClass == null)
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(triggeringClass),
                $"File defines {topLevelClasses.Length} classes. Prefer one class per file.");
        }
    }

    private static ClassDeclarationSyntax GetTriggeringClass(CodeReviewChangedFile file, IReadOnlyList<ClassDeclarationSyntax> classes)
    {
        if (file == null || classes == null || classes.Count <= 1)
            return null;
        if (file.IsAdded)
            return classes[1];

        return classes
            .Skip(1)
            .FirstOrDefault(classDeclaration => RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, classDeclaration.Span));
    }

    private static bool IsTopLevelClass(ClassDeclarationSyntax classDeclaration) =>
        classDeclaration?.Ancestors().All(ancestor => ancestor is not TypeDeclarationSyntax) == true;
}

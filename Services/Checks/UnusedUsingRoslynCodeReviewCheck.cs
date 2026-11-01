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
using System.Xml.Linq;
using DTC.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class UnusedUsingRoslynCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    private const string UnusedUsingDiagnosticId = "CS8019";
    private const string DuplicateUsingDiagnosticId = "CS0105";
    private const string RedundantGlobalUsingDiagnosticId = "CS8933";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly CSharpCompilationOptions CompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
        {
            { UnusedUsingDiagnosticId, ReportDiagnostic.Warn },
            { DuplicateUsingDiagnosticId, ReportDiagnostic.Warn },
            { RedundantGlobalUsingDiagnosticId, ReportDiagnostic.Warn }
        });
    private static readonly Lazy<IReadOnlyList<MetadataReference>> MetadataReferences = new(CreateMetadataReferences);
    private static readonly object ProjectGlobalUsingsLock = new();
    private static readonly Dictionary<string, IReadOnlyList<string>> ProjectGlobalUsingsCache = new(PathComparer);

    public override string RuleId => "unused-usings-roslyn";

    public override string DisplayName => "Unused using directives (Roslyn)";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out var lineIndex,
                out resultMessage))
        {
            return false;
        }

        var line = sourceText.Lines[lineIndex];
        var lineText = line.ToString();
        var trimmed = lineText.TrimStart();
        if (trimmed.StartsWith("global using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("global using\t", StringComparison.Ordinal))
        {
            resultMessage = "Global using directives are excluded from this check.";
            return false;
        }

        if (!trimmed.StartsWith("using ", StringComparison.Ordinal) &&
            !trimmed.StartsWith("using\t", StringComparison.Ordinal) &&
            !trimmed.StartsWith("global using ", StringComparison.Ordinal) &&
            !trimmed.StartsWith("global using\t", StringComparison.Ordinal))
        {
            resultMessage = "Target line is not a using directive.";
            return false;
        }

        var spanToRemove = TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak);
        var updatedText = sourceText.WithChanges(new TextChange(spanToRemove, string.Empty)).ToString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
        {
            return false;
        }

        resultMessage = "Removed unused using directive.";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Text))
                continue;

            var syntaxTree = RoslynCodeReviewCheckUtilities.ParseTree(file);
            var syntaxTrees = BuildSyntaxTrees(file, syntaxTree);
            var compilation = CSharpCompilation.Create(
                assemblyName: "ReviewG33k.UnusedUsings",
                syntaxTrees: syntaxTrees,
                references: MetadataReferences.Value,
                options: CompilationOptions);
            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Length == 0)
                continue;

            if (syntaxTree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                continue;

            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var reportedLines = new HashSet<int>();
            var hasSourceSemanticErrors = HasSourceSemanticErrorsForTree(diagnostics, syntaxTree);
            foreach (var diagnostic in diagnostics.Where(IsUnusedUsingDiagnostic))
            {
                if (diagnostic.Location.SourceTree != syntaxTree)
                    continue;
                if (hasSourceSemanticErrors && diagnostic.Id == UnusedUsingDiagnosticId)
                    continue;

                var lineNumber = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                if (!reportedLines.Add(lineNumber))
                    continue;

                var usingDirective = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>();
                if (usingDirective?.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) == true)
                    continue;

                var usingText = BuildUsingText(usingDirective);
                var message = string.IsNullOrWhiteSpace(usingText)
                    ? "Unused using directive detected."
                    : $"Unused using directive `{usingText}`.";
                AddFinding(report, CodeReviewFindingSeverity.Hint, file.Path, lineNumber, message);
            }
        }
    }

    private static bool IsUnusedUsingDiagnostic(Diagnostic diagnostic) =>
        diagnostic is { Id: UnusedUsingDiagnosticId or DuplicateUsingDiagnosticId or RedundantGlobalUsingDiagnosticId } &&
        diagnostic.Location != Location.None &&
        diagnostic.Location.IsInSource;

    private static bool HasSourceSemanticErrorsForTree(IEnumerable<Diagnostic> diagnostics, SyntaxTree syntaxTree)
    {
        if (diagnostics == null || syntaxTree == null)
            return false;

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Location != Location.None &&
            diagnostic.Location.IsInSource &&
            diagnostic.Location.SourceTree == syntaxTree);
    }

    private static string BuildUsingText(UsingDirectiveSyntax usingDirective)
    {
        var text = usingDirective?.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.EndsWith(';') ? text : $"{text};";
    }

    private static IReadOnlyList<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
            return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => path.ToFile().Exists())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToArray();
    }

    private static IReadOnlyList<SyntaxTree> BuildSyntaxTrees(CodeReviewChangedFile file, SyntaxTree fileSyntaxTree)
    {
        var syntaxTrees = new List<SyntaxTree> { fileSyntaxTree };
        var globalUsingsSource = BuildProjectGlobalUsingsSource(file);
        if (string.IsNullOrWhiteSpace(globalUsingsSource))
            return syntaxTrees;

        syntaxTrees.Add(CSharpSyntaxTree.ParseText(globalUsingsSource));
        return syntaxTrees;
    }

    private static string BuildProjectGlobalUsingsSource(CodeReviewChangedFile file)
    {
        var globalUsings = GetProjectGlobalUsings(file);
        if (globalUsings.Count == 0)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            globalUsings.Select(@using => $"global using {@using};"));
    }

    private static IReadOnlyList<string> GetProjectGlobalUsings(CodeReviewChangedFile file)
    {
        var projectPath = ResolveOwningProjectPath(file?.FullPath);
        if (string.IsNullOrWhiteSpace(projectPath))
            return [];

        lock (ProjectGlobalUsingsLock)
        {
            if (ProjectGlobalUsingsCache.TryGetValue(projectPath, out var cachedUsings))
                return cachedUsings;
        }

        var resolvedUsings = LoadProjectGlobalUsings(projectPath);
        lock (ProjectGlobalUsingsLock)
        {
            ProjectGlobalUsingsCache[projectPath] = resolvedUsings;
        }

        return resolvedUsings;
    }

    private static string ResolveOwningProjectPath(string fileFullPath)
    {
        if (string.IsNullOrWhiteSpace(fileFullPath))
            return null;

        var directoryPath = Path.GetDirectoryName(fileFullPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
            return null;

        for (var directory = directoryPath.ToDir(); directory?.Exists() == true; directory = directory.Parent)
        {
            var projectFiles = directory.GetFiles("*.csproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length == 0)
                continue;

            return projectFiles[0].FullName;
        }

        return null;
    }

    private static IReadOnlyList<string> LoadProjectGlobalUsings(string projectPath)
    {
        try
        {
            if (!projectPath.ToFile().Exists())
                return [];

            var document = XDocument.Load(projectPath);
            var project = document.Root;
            if (project == null)
                return [];

            var hasImplicitUsingsEnabled = project
                .Descendants()
                .Where(node => string.Equals(node.Name.LocalName, "ImplicitUsings", StringComparison.OrdinalIgnoreCase))
                .Select(node => (node.Value ?? string.Empty).Trim())
                .Any(value => string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

            var implicitGeneratedUsings = hasImplicitUsingsEnabled
                ? LoadGeneratedImplicitGlobalUsings(projectPath)
                : [];
            var configuredUsings = project
                .Descendants()
                .Where(node => string.Equals(node.Name.LocalName, "Using", StringComparison.OrdinalIgnoreCase))
                .Select(node => node.Attribute("Include")?.Value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            var globalUsings = new List<string>();
            globalUsings.AddRange(implicitGeneratedUsings);
            globalUsings.AddRange(configuredUsings);
            return globalUsings
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> LoadGeneratedImplicitGlobalUsings(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return [];

        var objDirectory = Path.Combine(projectDirectory, "obj");
        if (!Directory.Exists(objDirectory))
            return [];

        var generatedFiles = Directory
            .EnumerateFiles(objDirectory, "*.GlobalUsings.g.cs", SearchOption.AllDirectories)
            .ToArray();
        if (generatedFiles.Length == 0)
            return [];

        var globalUsings = new List<string>();
        foreach (var generatedFile in generatedFiles)
        {
            foreach (var line in File.ReadLines(generatedFile))
            {
                if (!TryParseGeneratedGlobalUsingLine(line, out var globalUsing))
                    continue;

                globalUsings.Add(globalUsing);
            }
        }

        return globalUsings
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryParseGeneratedGlobalUsingLine(string line, out string globalUsing)
    {
        globalUsing = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        const string prefix = "global using ";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        if (!trimmed.EndsWith(';'))
            return false;

        var value = trimmed.Substring(prefix.Length, trimmed.Length - prefix.Length - 1).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        globalUsing = value;
        return true;
    }
}

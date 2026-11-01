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
using System.Text;
using System.Text.RegularExpressions;
using DTC.Core.Extensions;

namespace ReviewG33k.Services.Checks.Support;

internal static class DuplicateCodeBlockUtilities
{
    private static readonly string[] DuplicateCheckSearchPatterns = ["*.cs", "*.axaml", "*.xaml"];
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MarkupAttributeOnlyLineRegex = new(@"^[A-Za-z_][A-Za-z0-9_:\.-]*\s*=", RegexOptions.Compiled);
    private static readonly HashSet<string> TrivialMarkupElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grid",
        "Grid.RowDefinitions",
        "Grid.ColumnDefinitions",
        "RowDefinition",
        "ColumnDefinition"
    };

    public static bool TryGetRepositoryRootPath(CodeReviewChangedFile file, out string repositoryRootPath)
    {
        repositoryRootPath = null;
        if (file == null ||
            string.IsNullOrWhiteSpace(file.FullPath) ||
            string.IsNullOrWhiteSpace(file.Path))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(file.FullPath)?.ToDir();
        if (directory?.Exists() != true)
            return false;

        var pathSegments = RepositoryUtilities.NormalizeRepoPath(file.Path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentCount = Math.Max(0, pathSegments.Length - 1);
        for (var i = 0; i < parentCount; i++)
        {
            directory = directory.Parent;
            if (directory == null)
                return false;
        }

        repositoryRootPath = directory.FullName;
        return true;
    }

    public static FileInfo[] EnumerateDuplicateCheckFiles(string repositoryRootPath, GitCommandRunner gitCommandRunner = null)
    {
        var repositoryRoot = repositoryRootPath.ToDir();
        if (repositoryRoot?.Exists() != true)
            return [];

        if (gitCommandRunner != null && RepositoryUtilities.IsGitRepository(repositoryRootPath))
        {
            var gitFiles = TryEnumerateAnalyzableGitFiles(repositoryRoot, gitCommandRunner);
            if (gitFiles != null)
                return gitFiles;
        }

        var pendingDirectories = new Stack<DirectoryInfo>();
        pendingDirectories.Push(repositoryRoot);

        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var file in EnumerateMatchingFiles(repositoryRoot, directory))
                files[file.FullName] = file;

            foreach (var childDirectory in EnumerateChildDirectories(repositoryRoot, directory))
                pendingDirectories.Push(childDirectory);
        }

        return files.Values
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static NormalizedCodeFile NormalizeCodeFile(CodeReviewChangedFile file)
    {
        if (file == null)
            return new NormalizedCodeFile(null, null, []);

        return new NormalizedCodeFile(file.Path, file.FullPath, NormalizeLines(file.Path, file.Lines));
    }

    public static NormalizedCodeFile NormalizeCodeFile(FileInfo file, string relativePath)
    {
        if (file?.Exists() != true)
            return new NormalizedCodeFile(relativePath, file?.FullName, []);

        var text = file.ReadAllText();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return new NormalizedCodeFile(relativePath, file.FullName, NormalizeLines(relativePath, lines));
    }

    public static List<CodeBlockWindow> CreateWindows(
        NormalizedCodeFile file,
        int windowSize,
        IReadOnlySet<int> requiredOriginalLines = null)
    {
        var windows = new List<CodeBlockWindow>();
        if (file?.Lines == null || file.Lines.Count < windowSize)
            return windows;

        for (var startIndex = 0; startIndex <= file.Lines.Count - windowSize; startIndex++)
        {
            var slice = file.Lines.Skip(startIndex).Take(windowSize).ToArray();
            if (requiredOriginalLines != null && slice.Any(line => !requiredOriginalLines.Contains(line.OriginalLineNumber)))
                continue;
            if (CodeReviewFileClassification.IsMarkupFilePath(file.RelativePath) &&
                slice.Count(line => line.IsSubstantive) < 2)
            {
                continue;
            }

            var normalizedText = string.Join("\n", slice.Select(line => line.NormalizedText));
            if (string.IsNullOrWhiteSpace(normalizedText))
                continue;

            windows.Add(new CodeBlockWindow(
                file.RelativePath,
                file.FullPath,
                startIndex,
                slice[0].OriginalLineNumber,
                slice[^1].OriginalLineNumber,
                normalizedText.Fnv1a64(),
                normalizedText));
        }

        return windows;
    }

    private static IReadOnlyList<NormalizedCodeLine> NormalizeLines(string relativePath, IReadOnlyList<string> lines)
    {
        var normalizedLines = new List<NormalizedCodeLine>();
        if (lines == null || lines.Count == 0)
            return normalizedLines;

        var isMarkupFile = CodeReviewFileClassification.IsMarkupFilePath(relativePath);
        var insideBlockComment = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var cleanedLine = StripComments(lines[index] ?? string.Empty, ref insideBlockComment);
            var normalizedLine = NormalizeCodeLine(cleanedLine);
            if (normalizedLine == null)
                continue;

            normalizedLines.Add(new NormalizedCodeLine(
                index + 1,
                normalizedLine,
                !isMarkupFile || IsSubstantiveMarkupLine(normalizedLine)));
        }

        return normalizedLines;
    }

    private static string NormalizeCodeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = WhitespaceRegex.Replace(line, " ").Trim();
        if (trimmed.Length < 2)
            return null;
        if (trimmed is "{" or "}" or "};")
            return null;
        if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("namespace ", StringComparison.Ordinal) ||
            trimmed.StartsWith("#", StringComparison.Ordinal) ||
            trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    private static bool IsSubstantiveMarkupLine(string normalizedLine)
    {
        if (string.IsNullOrWhiteSpace(normalizedLine))
            return false;

        if (MarkupAttributeOnlyLineRegex.IsMatch(normalizedLine))
            return false;

        if (TryGetMarkupElementName(normalizedLine, out var elementName) &&
            TrivialMarkupElementNames.Contains(elementName))
        {
            return false;
        }

        return normalizedLine.Contains("=") ||
               normalizedLine.Contains("{") ||
               normalizedLine.Contains(">") && normalizedLine.Contains("</", StringComparison.Ordinal);
    }

    private static bool TryGetMarkupElementName(string normalizedLine, out string elementName)
    {
        elementName = null;
        if (string.IsNullOrWhiteSpace(normalizedLine) || normalizedLine[0] != '<')
            return false;

        var startIndex = normalizedLine.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
        var endIndex = startIndex;
        while (endIndex < normalizedLine.Length)
        {
            var current = normalizedLine[endIndex];
            if (char.IsWhiteSpace(current) || current is '>' or '/')
                break;

            endIndex++;
        }

        if (endIndex <= startIndex)
            return false;

        elementName = normalizedLine[startIndex..endIndex];
        return true;
    }

    private static string StripComments(string line, ref bool insideBlockComment)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        var builder = new StringBuilder();
        for (var index = 0; index < line.Length; index++)
        {
            if (insideBlockComment)
            {
                if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '/')
                {
                    insideBlockComment = false;
                    index++;
                }

                continue;
            }

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                break;

            if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
            {
                insideBlockComment = true;
                index++;
                continue;
            }

            builder.Append(line[index]);
        }

        return builder.ToString();
    }

    private static IEnumerable<FileInfo> EnumerateMatchingFiles(DirectoryInfo repositoryRoot, DirectoryInfo directory)
    {
        foreach (var searchPattern in DuplicateCheckSearchPatterns)
        {
            IEnumerable<FileInfo> files;
            try
            {
                files = directory.EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!file.Exists())
                    continue;

                var relativePath = RepositoryUtilities.NormalizeRepoPath(Path.GetRelativePath(repositoryRoot.FullName, file.FullName));
                if (!CodeReviewFileClassification.IsDuplicateCodeCheckPath(relativePath) ||
                    CodeReviewFileClassification.IsTestFilePath(relativePath))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static FileInfo[] TryEnumerateAnalyzableGitFiles(DirectoryInfo repositoryRoot, GitCommandRunner gitCommandRunner)
    {
        try
        {
            var result = gitCommandRunner
                .RunAsync(
                    repositoryRoot.FullName,
                    "ls-files",
                    "--cached",
                    "--others",
                    "--exclude-standard")
                .GetAwaiter()
                .GetResult();
            if (!result.IsSuccess)
                return null;

            return (result.StandardOutput ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(RepositoryUtilities.NormalizeRepoPath)
                .Where(CodeReviewFileClassification.IsDuplicateCodeCheckPath)
                .Where(relativePath => !CodeReviewFileClassification.IsTestFilePath(relativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(relativePath => repositoryRoot.GetFile(relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Where(file => file.Exists())
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateChildDirectories(DirectoryInfo repositoryRoot, DirectoryInfo directory)
    {
        IEnumerable<DirectoryInfo> childDirectories;
        try
        {
            childDirectories = directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var childDirectory in childDirectories)
        {
            var relativeDirectoryPath = RepositoryUtilities.NormalizeRepoPath(
                Path.GetRelativePath(repositoryRoot.FullName, childDirectory.FullName));
            if (!CodeReviewFileClassification.IsIgnoredPath(relativeDirectoryPath))
                yield return childDirectory;
        }
    }
}

internal sealed record NormalizedCodeFile(string RelativePath, string FullPath, IReadOnlyList<NormalizedCodeLine> Lines);

internal sealed record NormalizedCodeLine(int OriginalLineNumber, string NormalizedText, bool IsSubstantive);

internal sealed record CodeBlockWindow(
    string RelativePath,
    string FullPath,
    int StartNormalizedIndex,
    int StartLineNumber,
    int EndLineNumber,
    ulong Hash,
    string NormalizedText);

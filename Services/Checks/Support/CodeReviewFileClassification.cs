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
using System.Text.RegularExpressions;

namespace ReviewG33k.Services.Checks.Support;

internal static class CodeReviewFileClassification
{
    private static readonly string[] IgnoredDirectoryNames =
    [
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "dist",
        "node_modules"
    ];

    private static readonly HashSet<string> SourceCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".c",
        ".cc",
        ".cpp",
        ".cxx",
        ".h",
        ".hh",
        ".hpp",
        ".hxx",
        ".js",
        ".jsx",
        ".mjs",
        ".cjs",
        ".ts",
        ".tsx",
        ".java",
        ".kt",
        ".kts",
        ".go",
        ".rs",
        ".swift",
        ".py",
        ".rb",
        ".php"
    };

    private static readonly Regex UiUsingRegex = new(
        @"^\s*using\s+(?<ns>(Avalonia\.(Controls|VisualTree)|System\.Windows(?:\.(Controls|Data|Documents|Forms|Input|Interop|Markup|Media|Navigation|Shapes|Threading))?|Windows\.UI\.Xaml(?:\.(Controls|Data|Documents|Input|Interop|Markup|Media|Navigation|Shapes))?))\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TestAttributeRegex = new(
        @"\[\s*(?:\w+\.)*(?:TestFixture|TextFixture|Test|TestCase|TestCaseSource|Theory|Fact|TestClass|TestMethod|DataTestMethod)\b",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool IsAnalyzableChangedCSharpPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !IsIgnoredPath(path) &&
        (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    public static bool IsAnalyzableResxPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !IsIgnoredPath(path) &&
        path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase);

    public static bool IsDuplicateCodeCheckPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !IsIgnoredPath(path) &&
        (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    public static bool IsAnalyzableChangedPath(string path) =>
        IsAnalyzableChangedCSharpPath(path) || IsAnalyzableResxPath(path);

    public static bool IsTestFilePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
         ContainsLikelyTestDirectorySegment(path));

    public static bool IsGeneratedFilePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase));

    public static bool IsIgnoredPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (IsGeneratedFilePath(path) || IsUnderIgnoredDirectory(path));

    public static bool IsLikelySourceCodePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsIgnoredPath(path))
            return false;

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SourceCodeExtensions.Contains(extension);
    }

    public static bool IsCodeBehindFilePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase));

    public static bool IsMarkupFilePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    public static bool IsLikelyInterfaceFilePath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Length < 4 ||
            fileName[0] != 'I')
        {
            return false;
        }

        return char.IsUpper(fileName[1]);
    }

    public static bool IsLikelyUiCodeFile(CodeReviewChangedFile file) =>
        file != null &&
        !string.IsNullOrWhiteSpace(file.Text) &&
        UiUsingRegex.IsMatch(file.Text);

    public static bool IsLikelyTestCodeFile(CodeReviewChangedFile file) =>
        file != null &&
        (IsTestFilePath(file.Path) ||
         (!string.IsNullOrWhiteSpace(file.Text) && TestAttributeRegex.IsMatch(file.Text)));

    private static bool ContainsLikelyTestDirectorySegment(string path)
    {
        var normalizedPath = (path ?? string.Empty).Replace('\\', '/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Tests", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "UnitTests", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith(".UnitTests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderIgnoredDirectory(string path)
    {
        var normalizedPath = "/" + (path ?? string.Empty).Replace('\\', '/').Trim('/') + "/";
        foreach (var directoryName in IgnoredDirectoryNames)
        {
            if (normalizedPath.Contains($"/{directoryName}/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

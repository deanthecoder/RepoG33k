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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DTC.Core.Extensions;
using Material.Icons;

namespace ReviewG33k.Services;

public sealed class CodeLocationOpener
{
    private bool m_vsCodeDetectionAttempted;
    private bool m_vsCodeUsesCommandShell;
    private string m_vsCodeExecutablePath;
    private bool m_visualStudioDetectionAttempted;
    private string m_visualStudioExecutablePath;
    private bool m_riderDetectionAttempted;
    private bool m_riderUsesCommandShell;
    private string m_riderExecutablePath;
    private readonly IReadOnlyDictionary<CodeLocationOpenTarget, CodeLocationOpenTargetDefinition> m_targetDefinitionsByTarget;

    public CodeLocationOpener()
    {
        TargetDefinitions =
        [
            new CodeLocationOpenTargetDefinition(
                CodeLocationOpenTarget.VsCode,
                "VS Code",
                MaterialIconKind.VsCode,
                isUiHandled: false,
                isAvailable: () => TryDetectVsCode(out _, out _),
                openAtLocation: (filePath, lineNumber) =>
                {
                    var success = TryLaunchVsCodeAtLine(filePath, lineNumber, out var error);
                    return (success, error);
                }),
            new CodeLocationOpenTargetDefinition(
                CodeLocationOpenTarget.VisualStudio,
                "Visual Studio",
                MaterialIconKind.MicrosoftVisualStudio,
                isUiHandled: false,
                isAvailable: () => TryDetectVisualStudio(out _),
                openAtLocation: (filePath, _) =>
                {
                    var success = TryLaunchVisualStudio(filePath, out var error);
                    return (success, error);
                }),
            new CodeLocationOpenTargetDefinition(
                CodeLocationOpenTarget.Rider,
                "Rider",
                MaterialIconKind.LetterRBoxOutline,
                isUiHandled: false,
                isAvailable: () => TryDetectRider(out _, out _),
                openAtLocation: (filePath, lineNumber) =>
                {
                    var success = TryLaunchRiderAtLine(filePath, lineNumber, out var error);
                    return (success, error);
                }),
            new CodeLocationOpenTargetDefinition(
                CodeLocationOpenTarget.FileBrowser,
                GetFileBrowserDisplayName(),
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? MaterialIconKind.AppleFinder
                    : MaterialIconKind.FolderSearchOutline,
                isUiHandled: false,
                isAvailable: () => true,
                openAtLocation: (filePath, _) =>
                {
                    var success = TryOpenInFileBrowser(filePath, out var error);
                    return (success, error);
                }),
            new CodeLocationOpenTargetDefinition(
                CodeLocationOpenTarget.Clipboard,
                "Clipboard",
                MaterialIconKind.ClipboardOutline,
                isUiHandled: true,
                isAvailable: () => true,
                openAtLocation: (_, _) => (false, "Clipboard open is handled by UI."))
        ];

        m_targetDefinitionsByTarget = TargetDefinitions.ToDictionary(definition => definition.Target);
        AllTargets = TargetDefinitions.Select(definition => definition.Target).ToArray();
    }

    public IReadOnlyList<CodeLocationOpenTargetDefinition> TargetDefinitions { get; }

    public IReadOnlyList<CodeLocationOpenTarget> AllTargets { get; }

    public bool TryGetTargetDefinition(CodeLocationOpenTarget target, out CodeLocationOpenTargetDefinition definition) =>
        m_targetDefinitionsByTarget.TryGetValue(target, out definition);

    public string GetDisplayName(CodeLocationOpenTarget target) =>
        TryGetTargetDefinition(target, out var definition)
            ? definition.DisplayName
            : target.ToString();

    public bool IsTargetAvailable(CodeLocationOpenTarget target)
    {
        return TryGetTargetDefinition(target, out var definition) &&
               definition.IsAvailable();
    }

    public bool TryOpenAtLocation(CodeLocationOpenTarget target, string filePath, int lineNumber, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "File path is required.";
            return false;
        }

        var resolvedFile = filePath.ToFile();
        if (!resolvedFile.Exists())
        {
            error = $"File does not exist: {filePath}";
            return false;
        }

        if (!TryGetTargetDefinition(target, out var definition))
        {
            error = $"Unsupported open target: {target}.";
            return false;
        }

        var boundedLineNumber = lineNumber > 0 ? lineNumber : 1;
        return definition.TryOpenAtLocation(resolvedFile.FullName, boundedLineNumber, out error);
    }

    private static string GetFileBrowserDisplayName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Explorer"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "Finder"
                : "File Manager";

    private bool TryLaunchVsCodeAtLine(string filePath, int lineNumber, out string error)
    {
        error = null;
        if (!TryDetectVsCode(out var vsCodePath, out var useCommandShell))
        {
            error = "VS Code was not detected.";
            return false;
        }

        var target = $"{filePath}:{lineNumber}";
        return TryStartAny(
            BuildVsCodeLaunchAttempts(vsCodePath, useCommandShell, target),
            out error,
            "VS Code");
    }

    private bool TryLaunchVisualStudio(string filePath, out string error)
    {
        error = null;
        if (!TryDetectVisualStudio(out var visualStudioPath))
        {
            error = "Visual Studio was not detected.";
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            error = "Visual Studio open is currently supported on Windows only.";
            return false;
        }

        return TryStartAny(
            [CreateStartInfo(visualStudioPath, $"/Edit \"{filePath}\"")],
            out error,
            "Visual Studio");
    }

    private bool TryLaunchRiderAtLine(string filePath, int lineNumber, out string error)
    {
        error = null;
        if (!TryDetectRider(out var riderPath, out var useCommandShell))
        {
            error = "Rider was not detected.";
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && useCommandShell)
        {
            return TryStartAny(
                [CreateCommandShellStartInfo($"\"{riderPath}\" --line {lineNumber} \"{filePath}\"")],
                out error,
                "Rider");
        }

        return TryStartAny(
            [CreateStartInfo(riderPath, $"--line {lineNumber} \"{filePath}\"")],
            out error,
            "Rider");
    }

    private static bool TryOpenInFileBrowser(string filePath, out string error)
    {
        error = null;
        var containingDirectory = filePath.ToFile().Directory?.FullName;
        if (string.IsNullOrWhiteSpace(containingDirectory))
        {
            error = "Could not resolve containing folder.";
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryStartAny(
                [CreateStartInfo("explorer.exe", $"/select,\"{filePath}\"")],
                out error,
                "Explorer");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryStartAny(
                [CreateStartInfo("open", $"-R \"{filePath}\"")],
                out error,
                "Finder");
        }

        return TryStartAny(
            [CreateStartInfo("xdg-open", $"\"{containingDirectory}\"")],
            out error,
            "File manager");
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments) =>
        new(fileName)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    private static ProcessStartInfo CreateCommandShellStartInfo(string command) =>
        CreateStartInfo("cmd.exe", $"/c \"{command}\"");

    private static bool IsCommandShellScript(string executablePath) =>
        executablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
        executablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static bool TryStartAny(
        IEnumerable<ProcessStartInfo> attempts,
        out string error,
        string appDisplayName)
    {
        error = null;
        Exception lastException = null;
        foreach (var startInfo in attempts ?? [])
        {
            try
            {
                var process = Process.Start(startInfo);
                if (process != null)
                    return true;
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        error = lastException == null
            ? $"{appDisplayName} could not be launched."
            : $"{appDisplayName} could not be launched. {lastException.Message}";
        return false;
    }

    private bool TryDetectVsCode(out string vsCodePath, out bool useCommandShell)
    {
        return TryResolveExecutable(
            ref m_vsCodeDetectionAttempted,
            ref m_vsCodeExecutablePath,
            ref m_vsCodeUsesCommandShell,
            GetVsCodeCandidates(),
            out vsCodePath,
            out useCommandShell);
    }

    private bool TryDetectVisualStudio(out string visualStudioPath)
    {
        return TryResolveExecutable(
            ref m_visualStudioDetectionAttempted,
            ref m_visualStudioExecutablePath,
            GetVisualStudioCandidates(),
            out visualStudioPath);
    }

    private bool TryDetectRider(out string riderPath, out bool useCommandShell)
    {
        return TryResolveExecutable(
            ref m_riderDetectionAttempted,
            ref m_riderExecutablePath,
            ref m_riderUsesCommandShell,
            GetRiderCandidates(),
            out riderPath,
            out useCommandShell);
    }

    private static bool TryResolveExecutable(
        ref bool detectionAttempted,
        ref string executablePath,
        IEnumerable<string> candidatePaths,
        out string resolvedPath)
    {
        if (!detectionAttempted)
        {
            detectionAttempted = true;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidatePath in candidatePaths ?? [])
            {
                if (string.IsNullOrWhiteSpace(candidatePath) || !seen.Add(candidatePath))
                    continue;

                if (!candidatePath.ToFile().Exists())
                    continue;

                executablePath = candidatePath;
                break;
            }
        }

        resolvedPath = executablePath;
        return !string.IsNullOrWhiteSpace(resolvedPath);
    }

    private static bool TryResolveExecutable(
        ref bool detectionAttempted,
        ref string executablePath,
        ref bool usesCommandShell,
        IEnumerable<string> candidatePaths,
        out string resolvedPath,
        out bool resolvedUsesCommandShell)
    {
        var resolved = TryResolveExecutable(
            ref detectionAttempted,
            ref executablePath,
            candidatePaths,
            out resolvedPath);

        if (resolved)
            usesCommandShell = IsCommandShellScript(resolvedPath);

        resolvedUsesCommandShell = usesCommandShell;
        return resolved;
    }

    private static IEnumerable<string> GetVsCodeCandidates()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        foreach (var pathExecutable in EnumeratePathExecutables(isWindows ? ["code.cmd", "code.exe", "code.bat"] : ["code"]))
            yield return pathExecutable;

        yield return "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
        yield return "/Applications/Visual Studio Code - Insiders.app/Contents/Resources/app/bin/code";
        yield return "/usr/local/bin/code";
        yield return "/opt/homebrew/bin/code";
        yield return "/snap/bin/code";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            yield return localAppData.ToDir().GetFile("Programs/Microsoft VS Code/Code.exe").FullName;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return programFiles.ToDir().GetFile("Microsoft VS Code/Code.exe").FullName;

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return programFilesX86.ToDir().GetFile("Microsoft VS Code/Code.exe").FullName;
    }

    private static IEnumerable<string> GetVisualStudioCandidates()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield break;

        foreach (var pathExecutable in EnumeratePathExecutables(["devenv.exe"]))
            yield return pathExecutable;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            yield break;

        var vsRoot = programFiles.ToDir().GetDir("Microsoft Visual Studio");
        if (!vsRoot.Exists())
            yield break;

        foreach (var yearDir in vsRoot.EnumerateDirectories())
        foreach (var skuDir in yearDir.EnumerateDirectories())
        {
            var devenv = skuDir.GetFile("Common7/IDE/devenv.exe");
            yield return devenv.FullName;
        }
    }

    private static IEnumerable<string> GetRiderCandidates()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        foreach (var pathExecutable in EnumeratePathExecutables(isWindows ? ["rider64.exe", "rider.cmd"] : ["rider", "rider.sh"]))
            yield return pathExecutable;

        if (isWindows)
        {
            foreach (var candidate in GetWindowsRiderCandidates())
                yield return candidate;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Rider.app/Contents/MacOS/rider";
        }
        else
        {
            yield return "/usr/local/bin/rider";
            yield return "/snap/bin/rider";
            yield return "/opt/rider/bin/rider.sh";
        }
    }

    private static IEnumerable<string> GetWindowsRiderCandidates()
    {
        const string riderExecutableName = "rider64.exe";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var localAppDataDir = localAppData.ToDir();
            yield return localAppDataDir.GetDir("Programs/Rider/bin").GetFile(riderExecutableName).FullName;

            var toolboxAppsRoot = localAppDataDir.GetDir("JetBrains/Toolbox/apps/Rider");
            if (toolboxAppsRoot.Exists())
            {
                foreach (var candidate in toolboxAppsRoot.TryGetFiles(riderExecutableName, SearchOption.AllDirectories))
                    yield return candidate.FullName;
            }
        }

        foreach (var programFilesRoot in GetProgramFilesRoots())
        {
            var jetBrainsRoot = programFilesRoot.ToDir().GetDir("JetBrains");
            if (!jetBrainsRoot.Exists())
                continue;

            yield return jetBrainsRoot.GetDir("Rider/bin").GetFile(riderExecutableName).FullName;

            foreach (var installDir in jetBrainsRoot.TryGetDirs())
            {
                if (!installDir.Name.Contains("Rider", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return installDir.GetDir("bin").GetFile(riderExecutableName).FullName;
            }
        }
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder) || !seen.Add(folder))
                continue;

            yield return folder;
        }
    }

    private static IEnumerable<string> EnumeratePathExecutables(IReadOnlyList<string> executableNames)
    {
        if (executableNames == null || executableNames.Count == 0)
            yield break;

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            yield break;

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedDirectory = directory.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedDirectory))
                continue;

            var directoryInfo = trimmedDirectory.ToDir();
            foreach (var executableName in executableNames)
                yield return directoryInfo.GetFile(executableName).FullName;
        }
    }

    private static IEnumerable<ProcessStartInfo> BuildVsCodeLaunchAttempts(string vsCodePath, bool useCommandShell, string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (useCommandShell)
            {
                yield return CreateCommandShellStartInfo($"\"{vsCodePath}\" --goto \"{target}\"");
            }
            else
            {
                yield return CreateStartInfo(vsCodePath, $"--goto \"{target}\"");

                yield return CreateCommandShellStartInfo($"\"{vsCodePath}\" --goto \"{target}\"");
            }

            yield break;
        }

        yield return CreateStartInfo(vsCodePath, $"--goto \"{target}\"");
    }
}

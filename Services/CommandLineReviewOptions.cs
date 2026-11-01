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

namespace ReviewG33k.Services;

/// <summary>
/// Describes a non-UI ReviewG33k run requested from command-line arguments.
/// </summary>
/// <remarks>
/// Keeps argument parsing separate from application startup so automation can invoke the same
/// local review flows without launching Avalonia.
/// </remarks>
internal sealed class CommandLineReviewOptions
{
    private CommandLineReviewOptions(
        bool shouldRun,
        bool showHelp,
        CommandLineReviewMode mode,
        string repositoryPath,
        string baseBranch,
        bool includeFullModifiedFiles,
        CommandLineOutputFormat outputFormat,
        string error)
    {
        ShouldRun = shouldRun;
        ShowHelp = showHelp;
        Mode = mode;
        RepositoryPath = repositoryPath;
        BaseBranch = baseBranch;
        IncludeFullModifiedFiles = includeFullModifiedFiles;
        OutputFormat = outputFormat;
        Error = error;
    }

    internal bool ShouldRun { get; }

    internal bool ShowHelp { get; }

    internal CommandLineReviewMode Mode { get; }

    internal string RepositoryPath { get; }

    internal string BaseBranch { get; }

    internal bool IncludeFullModifiedFiles { get; }

    internal CommandLineOutputFormat OutputFormat { get; }

    internal string Error { get; }

    internal static bool IsCommandLineReview(string[] args)
    {
        foreach (var arg in args ?? [])
        {
            if (IsAny(arg, "--cli", "/cli", "--json", "/json", "--help", "/help", "/?"))
                return true;
        }

        return false;
    }

    internal static CommandLineReviewOptions Parse(string[] args)
    {
        if (!IsCommandLineReview(args))
            return new CommandLineReviewOptions(false, false, CommandLineReviewMode.Uncommitted, null, "main", false, CommandLineOutputFormat.Console, null);

        var values = new Queue<string>(args ?? []);
        var mode = CommandLineReviewMode.Uncommitted;
        var repositoryPath = Directory.GetCurrentDirectory();
        var baseBranch = "main";
        var includeFullModifiedFiles = false;
        var outputFormat = CommandLineOutputFormat.Console;

        while (values.Count > 0)
        {
            var arg = values.Dequeue();
            if (IsAny(arg, "--cli", "/cli"))
                continue;

            if (IsAny(arg, "--help", "/help", "/?"))
                return new CommandLineReviewOptions(true, true, mode, repositoryPath, baseBranch, includeFullModifiedFiles, outputFormat, null);

            if (IsAny(arg, "--full", "/full"))
            {
                includeFullModifiedFiles = true;
                continue;
            }

            if (IsAny(arg, "--json", "/json"))
            {
                outputFormat = CommandLineOutputFormat.Json;
                continue;
            }

            if (TryConsumeValue(arg, values, ["--format", "/format"], out var formatValue))
            {
                if (!TryParseOutputFormat(formatValue, out outputFormat))
                    return Failed($"Unknown output format `{formatValue}`. Use `console` or `json`.");
                continue;
            }

            if (TryConsumeValue(arg, values, ["--repo", "/repo"], out var repoValue))
            {
                repositoryPath = repoValue;
                continue;
            }

            if (TryConsumeValue(arg, values, ["--mode", "/mode"], out var modeValue))
            {
                if (!TryParseMode(modeValue, out mode))
                    return Failed($"Unknown review mode `{modeValue}`. Use `uncommitted`, `committed`, or `tree`.");
                continue;
            }

            if (TryConsumeValue(arg, values, ["--base", "/base"], out var baseValue))
            {
                baseBranch = baseValue;
                continue;
            }

            return Failed($"Unknown argument `{arg}`.");
        }

        if (string.IsNullOrWhiteSpace(repositoryPath))
            return Failed("Repository path is required.");

        if (mode == CommandLineReviewMode.Committed && string.IsNullOrWhiteSpace(baseBranch))
            return Failed("Base branch is required when `--mode committed` is used.");

        return new CommandLineReviewOptions(
            true,
            false,
            mode,
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(repositoryPath.Trim())),
            string.IsNullOrWhiteSpace(baseBranch) ? null : baseBranch.Trim(),
            includeFullModifiedFiles,
            outputFormat,
            null);

        CommandLineReviewOptions Failed(string error) =>
            new(true, false, CommandLineReviewMode.Uncommitted, null, "main", false, outputFormat, error);
    }

    private static bool TryConsumeValue(string arg, Queue<string> values, string[] optionNames, out string value)
    {
        value = null;
        foreach (var optionName in optionNames)
        {
            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.Count > 0 ? values.Dequeue() : null;
                return true;
            }

            var prefix = optionName + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = arg[prefix.Length..];
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMode(string value, out CommandLineReviewMode mode)
    {
        mode = CommandLineReviewMode.Uncommitted;
        return value?.Trim().ToLowerInvariant() switch
        {
            "uncommitted" => SetMode(CommandLineReviewMode.Uncommitted, out mode),
            "committed" => SetMode(CommandLineReviewMode.Committed, out mode),
            "tree" => SetMode(CommandLineReviewMode.Tree, out mode),
            _ => false
        };
    }

    private static bool TryParseOutputFormat(string value, out CommandLineOutputFormat format)
    {
        format = CommandLineOutputFormat.Console;
        return value?.Trim().ToLowerInvariant() switch
        {
            "console" => true,
            "json" => SetOutputFormat(CommandLineOutputFormat.Json, out format),
            _ => false
        };
    }

    private static bool SetOutputFormat(CommandLineOutputFormat value, out CommandLineOutputFormat format)
    {
        format = value;
        return true;
    }

    private static bool SetMode(CommandLineReviewMode value, out CommandLineReviewMode mode)
    {
        mode = value;
        return true;
    }

    private static bool IsAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

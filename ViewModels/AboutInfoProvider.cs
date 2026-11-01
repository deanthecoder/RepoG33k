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
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DTC.Core.Extensions;
using DTC.Core.UI;

namespace ReviewG33k.ViewModels;

/// <summary>
/// Provides static metadata used by the native app menu and About dialog.
/// </summary>
/// <remarks>
/// Useful for sharing the same title, version, copyright, website, and icon wherever the app needs a standard
/// ReviewG33k About experience.
/// </remarks>
internal static class AboutInfoProvider
{
    public static AboutInfo Info => new()
    {
        Title = "ReviewG33k",
        Version = GetDisplayVersion(),
        Copyright = "Copyright (c) 2026 Dean Edis.",
        WebsiteUrl = "https://github.com/deanthecoder/ReviewG33k",
        Icon = LoadIcon()
    };

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutInfoProvider).Assembly;
        return assembly.GetDisplayVersion() ?? "Unknown";
    }

    private static Bitmap LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://ReviewG33k.Shared/Assets/app.ico"));
        return new Bitmap(stream);
    }
}

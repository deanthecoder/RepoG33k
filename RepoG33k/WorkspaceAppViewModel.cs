// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using DTC.Core.UI;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RepoG33k;

/// <summary>Provides the standard DTC About dialog for the RepoG33k native app menu.</summary>
/// <remarks>Uses the standard DTC About dialog with RepoG33k branding.</remarks>
public sealed class WorkspaceAppViewModel : AppViewModelBase
{
    public WorkspaceAppViewModel() : base(CreateInfo()) { }

    private static AboutInfo CreateInfo()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://ReviewG33k.Shared/Assets/repog33k.png"));
        return new AboutInfo
        {
            Title = "RepoG33k", Version = typeof(WorkspaceAppViewModel).Assembly.GetName().Version?.ToString(3), Copyright = "Copyright (c) 2026 Dean Edis.",
            WebsiteUrl = "https://github.com/deanthecoder/RepoG33k", Icon = new Bitmap(iconStream)
        };
    }
}

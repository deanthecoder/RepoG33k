// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.ComponentModel;
using ReviewG33k.Models;

namespace RepoG33k.Services;

/// <summary>Describes the checked-out branch relative to locally known upstream refs.</summary>
/// <remarks>Synchronization never implies a server check; fetch time is shown separately.</remarks>
internal sealed record RepositoryBranchStatus(string Branch, string Upstream, int Ahead, int Behind,
    DateTimeOffset? LastFetched, DateTimeOffset CheckedAt, string Error = null)
{
    public string Badge => Error != null ? "Unavailable" : Branch == "Detached HEAD" ? "Detached HEAD"
        : Upstream == null ? "No upstream" : Ahead > 0 && Behind > 0 ? $"↑ {Ahead} ↓ {Behind}"
        : Behind > 0 ? $"↓ {Behind} Behind" : Ahead > 0 ? $"↑ {Ahead} Ahead" : "✓ Synced";
    public string Color => Error != null || Upstream == null ? "#8B9DAF" : Behind > 0 ? "#E9BC70" : "#79DDC4";
    public string FetchLabel => LastFetched is { } time ? $"Fetched {time.ToLocalTime():dd MMM HH:mm}" : "Fetch time unknown";
    public string Description => Error ?? $"Branch: {Branch}\nUpstream: {Upstream ?? "not configured"}\n{FetchLabel}\nChecked {CheckedAt.ToLocalTime():HH:mm}. Compared with locally known refs; fetch to check the server.";
}

/// <summary>Updates one repository selector row without rebuilding an open dropdown.</summary>
/// <remarks>Status reads are cached in memory and do not delay repository selection.</remarks>
internal sealed class RepositoryPickerItem(LocalRepositoryOption option) : INotifyPropertyChanged
{
    public LocalRepositoryOption Option { get; } = option;
    public string FullPath => Option.FullPath;
    public RepositoryBranchStatus Status { get; private set; }
    public string Badge => Status?.Badge ?? "Checking…";
    public string Color => Status?.Color ?? "#8B9DAF";
    public string Description => FullPath + "\n" + (Status?.Description ?? "Reading local branch status…");
    public event PropertyChangedEventHandler PropertyChanged;
    public override string ToString() => Option.DisplayName;

    public void Update(RepositoryBranchStatus status)
    {
        if (Status != null && Status.CheckedAt > status.CheckedAt)
            return;
        Status = status;
        foreach (var name in new[] { nameof(Badge), nameof(Color), nameof(Description) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

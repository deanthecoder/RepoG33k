// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using Avalonia;

namespace RepoG33k;

/// <summary>Starts the repository-focused RepoG33k application.</summary>
/// <remarks>RepoG33k provides a standalone repository workspace.</remarks>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}

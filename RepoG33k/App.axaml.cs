// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace RepoG33k;

/// <summary>Configures RepoG33k's independent application shell.</summary>
/// <remarks>The editor and review engine come from the shared ReviewG33k library.</remarks>
public sealed class App : Application
{
    public App() => DataContext = new WorkspaceAppViewModel();
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new WorkspaceWindow(desktop.Args ?? []);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => window.CleanupTemporaryReviews();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

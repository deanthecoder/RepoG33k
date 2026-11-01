// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.UI;

namespace ReviewG33k.ViewModels;

/// <summary>
/// Application-level view model that exposes shared app menu commands.
/// </summary>
/// <remarks>
/// Useful for keeping native shell integration, such as the macOS About menu item, separate from the main window
/// workflow view model.
/// </remarks>
public sealed class AppViewModel : AppViewModelBase
{
    public AppViewModel()
        : base(AboutInfoProvider.Info)
    {
    }
}

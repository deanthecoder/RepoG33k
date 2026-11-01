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
using Material.Icons;

namespace ReviewG33k.Services;

public sealed class CodeLocationOpenTargetDefinition
{
    private readonly Func<bool> m_isAvailable;
    private readonly Func<string, int, (bool Success, string Error)> m_openAtLocation;

    public CodeLocationOpenTargetDefinition(
        CodeLocationOpenTarget target,
        string displayName,
        MaterialIconKind iconKind,
        bool isUiHandled,
        Func<bool> isAvailable,
        Func<string, int, (bool Success, string Error)> openAtLocation)
    {
        Target = target;
        DisplayName = displayName ?? target.ToString();
        IconKind = iconKind;
        IsUiHandled = isUiHandled;
        m_isAvailable = isAvailable;
        m_openAtLocation = openAtLocation;
    }

    public CodeLocationOpenTarget Target { get; }

    public string DisplayName { get; }

    public MaterialIconKind IconKind { get; }

    public bool IsUiHandled { get; }

    public bool IsAvailable() =>
        m_isAvailable?.Invoke() ?? false;

    public bool TryOpenAtLocation(string filePath, int lineNumber, out string error)
    {
        if (m_openAtLocation == null)
        {
            error = "Open action is not configured.";
            return false;
        }

        var result = m_openAtLocation(filePath, lineNumber);
        error = result.Error;
        return result.Success;
    }
}

// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Settings;

namespace ReviewG33k.ViewModels;

public sealed class Settings : UserSettingsBase
{
    private const int DefaultReviewModeIndex = -1;

    public static Settings Instance { get; } = new();

    protected override void ApplyDefaults()
    {
        RepositoryRootPath = string.Empty;
        LocalReviewRepositoryPath = string.Empty;
        LocalReviewBaseBranch = "main";
        CodeViewOpenTarget = "VSCode";
        ReviewModeIndex = DefaultReviewModeIndex;
        UseLocalCommittedReview = false;
        IncludeFullModifiedFiles = false;
        DisabledCodeReviewRuleIds = [];
        DiffLensFilePanelExpanded = true;
        DiffLensFilePanelWidth = 260;
    }

    public string RepositoryRootPath
    {
        get => Get<string>();
        set => Set(value);
    }

    public string LocalReviewRepositoryPath
    {
        get => Get<string>();
        set => Set(value);
    }

    public string LocalReviewBaseBranch
    {
        get => Get<string>();
        set => Set(value);
    }

    public string CodeViewOpenTarget
    {
        get => Get<string>();
        set => Set(value);
    }

    public int ReviewModeIndex
    {
        get
        {
            var rawValue = Get<long>();
            return rawValue is >= int.MinValue and <= int.MaxValue
                ? (int)rawValue
                : DefaultReviewModeIndex;
        }
        set => Set((long)value);
    }

    public bool UseLocalCommittedReview
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IncludeFullModifiedFiles
    {
        get => Get<bool>();
        set => Set(value);
    }

    public string[] DisabledCodeReviewRuleIds
    {
        get => Get<string[]>() ?? [];
        set => Set(value ?? []);
    }

    public bool DiffLensFilePanelExpanded
    {
        get => Get<bool>();
        set => Set(value);
    }

    public double DiffLensFilePanelWidth
    {
        get => Get<double>();
        set => Set(value);
    }
}

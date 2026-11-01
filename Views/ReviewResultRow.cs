// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Media;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Views;

public sealed class ReviewResultRow : INotifyPropertyChanged
{
    public enum ActionAvailability
    {
        Hidden,
        Disabled,
        Enabled
    }

    private static readonly IBrush IncludedIssueBrush = new SolidColorBrush(Color.Parse("#E8F0FF"));
    private static readonly IBrush ExcludedIssueBrush = new SolidColorBrush(Color.Parse("#6F7D9A"));
    private static readonly IBrush IncludedLocationBrush = new SolidColorBrush(Color.Parse("#9FB0D0"));
    private static readonly IBrush ExcludedLocationBrush = new SolidColorBrush(Color.Parse("#5D6A84"));

    private readonly ActionAvailability m_commentAvailability;
    private readonly ActionAvailability m_fixAvailability;
    private readonly ActionAvailability m_codexPromptAvailability;

    private bool m_isIncluded = true;
    private bool m_isIgnored;
    private bool m_isPostingComment;
    private bool m_hasPostedComment;
    private bool m_isFixing;
    private bool m_hasBeenFixed;

    public ReviewResultRow(
        CodeSmellFinding finding,
        bool canOpen,
        ActionAvailability commentAvailability,
        ActionAvailability fixAvailability,
        ActionAvailability codexPromptAvailability,
        bool hasExistingComment = false)
    {
        Finding = finding;
        RuleId = finding?.RuleId ?? string.Empty;
        CategoryText = CodeReviewFindingCategoryResolver.ResolveCategory(RuleId);
        IssueFull = finding?.Message ?? string.Empty;
        IssueSummary = CollapseDisplayText(IssueFull);
        IssueLocation = finding == null
            ? string.Empty
            : finding.LineNumber > 0
                ? $"{finding.FilePath}:{finding.LineNumber}"
                : finding.FilePath ?? string.Empty;
        CanOpen = canOpen;
        m_commentAvailability = commentAvailability;
        m_fixAvailability = fixAvailability;
        m_codexPromptAvailability = codexPromptAvailability;
        m_hasPostedComment = hasExistingComment;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public CodeSmellFinding Finding { get; }

    public string RuleId { get; }

    public string CategoryText { get; }

    public string IssueSummary { get; }

    public string IssueFull { get; }

    public string IssueLocation { get; }

    public bool CanOpen { get; }

    public bool CanComment => m_commentAvailability == ActionAvailability.Enabled;

    public bool ShowCommentButton => m_commentAvailability != ActionAvailability.Hidden;

    public bool ShowFixButton => m_fixAvailability != ActionAvailability.Hidden;

    public bool CanFix => m_fixAvailability == ActionAvailability.Enabled;

    public bool ShowCodexPromptButton => m_codexPromptAvailability != ActionAvailability.Hidden;

    public bool CanCreateCodexPrompt => m_codexPromptAvailability == ActionAvailability.Enabled;

    public bool CanIgnore { get; set; }

    public bool IsIgnored
    {
        get => m_isIgnored;
        set
        {
            if (m_isIgnored == value)
                return;
            m_isIgnored = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IgnoreActionText));
            RaisePropertyChanged(nameof(IssueForeground));
            RaisePropertyChanged(nameof(IssueLocationForeground));
        }
    }

    public string IgnoreActionText => IsIgnored ? "Restore" : "Ignore";

    public bool IsIncluded
    {
        get => m_isIncluded;
        set
        {
            if (m_isIncluded == value)
                return;

            m_isIncluded = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanOpenActive));
            RaisePropertyChanged(nameof(CanCommentActive));
            RaisePropertyChanged(nameof(CanFixActive));
            RaisePropertyChanged(nameof(CanCodexPromptActive));
            RaisePropertyChanged(nameof(IssueForeground));
            RaisePropertyChanged(nameof(IssueLocationForeground));
        }
    }

    public bool IsPostingComment
    {
        get => m_isPostingComment;
        set
        {
            if (m_isPostingComment == value)
                return;

            m_isPostingComment = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanCommentActive));
        }
    }

    public bool IsFixing
    {
        get => m_isFixing;
        set
        {
            if (m_isFixing == value)
                return;

            m_isFixing = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanFixActive));
        }
    }

    public bool HasBeenFixed
    {
        get => m_hasBeenFixed;
        set
        {
            if (m_hasBeenFixed == value)
                return;

            m_hasBeenFixed = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanFixActive));
        }
    }

    public bool HasPostedComment
    {
        get => m_hasPostedComment;
        set
        {
            if (m_hasPostedComment == value)
                return;

            m_hasPostedComment = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanCommentActive));
        }
    }

    public bool CanOpenActive => CanOpen && IsIncluded;

    public bool CanCommentActive => CanComment && IsIncluded && !IsPostingComment && !HasPostedComment;

    public bool CanFixActive => CanFix && IsIncluded && !IsFixing && !HasBeenFixed;

    public bool CanCodexPromptActive => CanCreateCodexPrompt && IsIncluded;

    public string TickSameFileMenuHeader => CanIgnore ? "Restore issues in this file" : "Select all issues in this file";
    public string UntickSameFileMenuHeader => CanIgnore ? "Ignore issues in this file" : "Deselect all issues in this file";

    public string UntickSameTypeMenuHeader => CanIgnore ? "Ignore issues of this type" :
        string.IsNullOrWhiteSpace(RuleId)
            ? "Ignore issues of this type"
            : $"Ignore all `{RuleId}` issues";

    public string TickSameTypeMenuHeader => CanIgnore ? "Restore issues of this type" :
        string.IsNullOrWhiteSpace(RuleId)
            ? "Select issues of this type"
            : $"Select all `{RuleId}` issues";

    public IBrush IssueForeground => IsIncluded && !IsIgnored ? IncludedIssueBrush : ExcludedIssueBrush;

    public IBrush IssueLocationForeground => IsIncluded && !IsIgnored ? IncludedLocationBrush : ExcludedLocationBrush;

    private static string CollapseDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private void RaisePropertyChanged([CallerMemberName] string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

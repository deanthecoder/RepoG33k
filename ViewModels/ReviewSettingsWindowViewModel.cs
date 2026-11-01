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
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DTC.Core.Commands;
using DTC.Core.ViewModels;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.ViewModels;

/// <summary>
/// View-model state for the review settings dialog.
/// </summary>
/// <remarks>
/// Useful for editing persistent per-rule preferences while keeping the analyzer's available check metadata as
/// the single source of truth.
/// </remarks>
public sealed class ReviewSettingsWindowViewModel : ViewModelBase
{
    private readonly CodeReviewRulePreferenceService m_rulePreferenceService;

    public ReviewSettingsWindowViewModel()
        : this([], new CodeReviewRulePreferenceService(new Settings()))
    {
    }

    public ReviewSettingsWindowViewModel(
        IEnumerable<ICodeReviewCheck> checks,
        CodeReviewRulePreferenceService rulePreferenceService)
    {
        m_rulePreferenceService = rulePreferenceService ?? throw new ArgumentNullException(nameof(rulePreferenceService));
        Categories = new ObservableCollection<ReviewRulePreferenceCategoryViewModel>(
            BuildCategories(checks, m_rulePreferenceService.DisabledRuleIds));
        foreach (var rule in EnumerateRules())
            rule.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReviewRulePreferenceItemViewModel.IsEnabled))
                    RaiseSummaryChanged();
            };

        EnableAllCommand = new RelayCommand(_ => SetAllEnabled(true));
        DisableAllCommand = new RelayCommand(_ => SetAllEnabled(false));
    }

    public ObservableCollection<ReviewRulePreferenceCategoryViewModel> Categories { get; }

    public ICommand EnableAllCommand { get; }

    public ICommand DisableAllCommand { get; }

    public int EnabledCount => EnumerateRules().Count(rule => rule.IsEnabled);

    public int TotalCount => EnumerateRules().Count();

    public string SummaryText => $"{EnabledCount} of {TotalCount} issue type(s) enabled";

    public void Save() =>
        m_rulePreferenceService.SaveDisabledRuleIds(
            EnumerateRules()
                .Where(rule => !rule.IsEnabled)
                .Select(rule => rule.RuleId));

    private static IReadOnlyList<ReviewRulePreferenceCategoryViewModel> BuildCategories(
        IEnumerable<ICodeReviewCheck> checks,
        IReadOnlySet<string> disabledRuleIds)
    {
        return (checks ?? [])
            .Where(check => check != null)
            .GroupBy(check => CodeReviewFindingCategoryResolver.ResolveCategory(check.RuleId), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => GetCategorySortOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReviewRulePreferenceCategoryViewModel(
                group.Key,
                group
                    .OrderBy(check => check.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(check => new ReviewRulePreferenceItemViewModel(
                        check.RuleId,
                        check.DisplayName,
                        !disabledRuleIds.Contains(check.RuleId)))))
            .ToArray();
    }

    private void SetAllEnabled(bool isEnabled)
    {
        foreach (var rule in EnumerateRules())
            rule.IsEnabled = isEnabled;

        RaiseSummaryChanged();
    }

    private IEnumerable<ReviewRulePreferenceItemViewModel> EnumerateRules() =>
        Categories.SelectMany(category => category.Rules);

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private static int GetCategorySortOrder(string category) =>
        category switch
        {
            CodeReviewFindingCategoryResolver.Correctness => 0,
            CodeReviewFindingCategoryResolver.Threading => 1,
            CodeReviewFindingCategoryResolver.Performance => 2,
            CodeReviewFindingCategoryResolver.Resources => 3,
            CodeReviewFindingCategoryResolver.ApiDesign => 4,
            CodeReviewFindingCategoryResolver.Readability => 5,
            CodeReviewFindingCategoryResolver.Maintainability => 6,
            CodeReviewFindingCategoryResolver.Testing => 7,
            CodeReviewFindingCategoryResolver.Documentation => 8,
            CodeReviewFindingCategoryResolver.Ui => 9,
            CodeReviewFindingCategoryResolver.RepoHygiene => 10,
            _ => 11
        };
}

// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services.Checks;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class CodeReviewFindingCategoryResolverTests
{
    [Test]
    public void ResolveCategoryWhenRuleIsKnownReturnsExpectedCategory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.TaskRunAsync),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Threading));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.EmptyMethod),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Correctness));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.PropertySetterIgnoresValue),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Correctness));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.JsonTrailingComma),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Correctness));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.ResxMissingLocaleKeys),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Resources));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.EagerMaterializedEnumerableReturn),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Performance));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.LinqCountComparedToZero),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Performance));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.FormattingOnlyChange),
                Is.EqualTo(CodeReviewFindingCategoryResolver.RepoHygiene));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.EmptyDispose),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Resources));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.MissingTypedBindingContext),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Ui));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.MissingTests),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Testing));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.PrivateFieldUsedInSingleMethod),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.PrivateBooleanMethodAlwaysReturnsSameValue),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.NamedReflectionMemberLookup),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.UnusedMethodParameter),
                Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
            Assert.That(
                CodeReviewFindingCategoryResolver.ResolveCategory(CodeReviewRuleIds.SourceFileNameMismatch),
                Is.EqualTo(CodeReviewFindingCategoryResolver.ApiDesign));
        });
    }

    [Test]
    public void ResolveCategoryWhenRuleIsUnknownReturnsMaintainability()
    {
        var category = CodeReviewFindingCategoryResolver.ResolveCategory("unknown-check-id");
        Assert.That(category, Is.EqualTo(CodeReviewFindingCategoryResolver.Maintainability));
    }
}

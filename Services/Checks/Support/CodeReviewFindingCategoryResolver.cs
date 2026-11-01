// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services.Checks.Support;

internal static class CodeReviewFindingCategoryResolver
{
    public const string Correctness = "Correctness";
    public const string Threading = "Threading";
    public const string Performance = "Performance";
    public const string Resources = "Resources";
    public const string ApiDesign = "API/Design";
    public const string Readability = "Readability";
    public const string Maintainability = "Maintainability";
    public const string Testing = "Testing";
    public const string Documentation = "Documentation";
    public const string Ui = "UI";
    public const string RepoHygiene = "Repo Hygiene";

    public static string ResolveCategory(string ruleId)
    {
        var normalizedRuleId = ruleId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRuleId))
            return Maintainability;

        return normalizedRuleId switch
        {
            CodeReviewRuleIds.EmptyCatch or
            CodeReviewRuleIds.EmptyMethod or
            CodeReviewRuleIds.SwallowingCatch or
            CodeReviewRuleIds.ThrowExInCatch or
            CodeReviewRuleIds.PublicMethodArgumentGuards or
            CodeReviewRuleIds.ConstructorEventSubscriptionLifecycle or
            CodeReviewRuleIds.PropertySetterIgnoresValue or
            CodeReviewRuleIds.JsonTrailingComma or
            CodeReviewRuleIds.NumericStringCultureForFileWrite => Correctness,

            CodeReviewRuleIds.AsyncVoid or
            CodeReviewRuleIds.LockThisOrPublic or
            CodeReviewRuleIds.TaskRunAsync or
            CodeReviewRuleIds.UnnecessaryTaskCompletion or
            CodeReviewRuleIds.ThreadSleep or
            CodeReviewRuleIds.UnobservedTaskResult => Threading,

            CodeReviewRuleIds.StringConcatSameTarget or
            CodeReviewRuleIds.MultipleEnumeration or
            CodeReviewRuleIds.EagerMaterializedEnumerableReturn or
            CodeReviewRuleIds.LinqCountComparedToZero => Performance,

            CodeReviewRuleIds.DisposableNotDisposed or
            CodeReviewRuleIds.DisposeWithoutIDisposable or
            CodeReviewRuleIds.EmptyDispose or
            CodeReviewRuleIds.ResxMissingLocaleKeys or
            CodeReviewRuleIds.ResxUnexpectedExtraKeys or
            CodeReviewRuleIds.ResxEmptyTranslationValues or
            CodeReviewRuleIds.ResxValueBoundaryWhitespace or
            CodeReviewRuleIds.ResxMixedEnglishDialect or
            CodeReviewRuleIds.ResxAmericanEnglishInBritishLocale or
            CodeReviewRuleIds.ResxBritishEnglishInAmericanLocale => Resources,

            CodeReviewRuleIds.PropertyCanBeAutoProperty or
            CodeReviewRuleIds.PrivateGetOnlyAutoPropertyShouldBeField or
            CodeReviewRuleIds.PrivatePropertyShouldBeField or
            CodeReviewRuleIds.PrivateFieldCanBeReadonly or
            CodeReviewRuleIds.MethodParameterCount or
            CodeReviewRuleIds.GenericTypeNameSuffix or
            CodeReviewRuleIds.MethodCanBeStatic or
            CodeReviewRuleIds.PublicMutableStaticState or
            CodeReviewRuleIds.MultipleClassesPerFile or
            CodeReviewRuleIds.SourceFileNameMismatch => ApiDesign,

            CodeReviewRuleIds.AsyncMethodNameSuffix or
            CodeReviewRuleIds.MissingBlankLineBetweenMethods or
            CodeReviewRuleIds.BlankLineBetweenBracePairs or
            CodeReviewRuleIds.IfElseBraceConsistency or
            CodeReviewRuleIds.EmptyIf or
            CodeReviewRuleIds.IfElseUnnecessaryBraces or
            CodeReviewRuleIds.BooleanLiteralComparison or
            CodeReviewRuleIds.UnnecessaryCast or
            CodeReviewRuleIds.UnnecessaryEnumMemberValue or
            CodeReviewRuleIds.UnnecessaryVerbatimStringPrefix or
            CodeReviewRuleIds.RedundantSelfLookup or
            CodeReviewRuleIds.LambdaCanBeMethodGroup or
            CodeReviewRuleIds.ConsecutiveBooleanArguments or
            CodeReviewRuleIds.ConsecutiveNullArguments => Readability,
            CodeReviewRuleIds.LocalVariablePrefix => Readability,

            CodeReviewRuleIds.ConstructorTooLong or
            "local-variable-can-be-const" or
            CodeReviewRuleIds.DuplicateCodeBlock or
            CodeReviewRuleIds.BooleanMethodAlwaysReturnsSameValue or
            CodeReviewRuleIds.PrivateBooleanMethodAlwaysReturnsSameValue or
            CodeReviewRuleIds.NamedReflectionMemberLookup or
            CodeReviewRuleIds.PrivateFieldUsedInSingleMethod or
            CodeReviewRuleIds.UnusedLocalVariable or
            CodeReviewRuleIds.UnusedMethodParameter or
            CodeReviewRuleIds.UnusedPrivateMember => Maintainability,

            CodeReviewRuleIds.MissingTests or
            CodeReviewRuleIds.MissingTestsForPublicMethods or
            CodeReviewRuleIds.MissingTestsForPublicProperties => Testing,

            CodeReviewRuleIds.MissingXmlDocs or
            CodeReviewRuleIds.EmptyXmlDocContent or
            CodeReviewRuleIds.MissingReadmeForNewProject => Documentation,

            CodeReviewRuleIds.MissingTypedBindingContext or
            CodeReviewRuleIds.FixedSizeLayoutContainer or
            CodeReviewRuleIds.SingleChildWrapperContainer or
            CodeReviewRuleIds.NestedSamePanelWrapper or
            CodeReviewRuleIds.EmptyContainer => Ui,

            CodeReviewRuleIds.WarningSuppression or
            CodeReviewRuleIds.UnusedUsingsRoslyn or
            CodeReviewRuleIds.MissingDisclaimerForNewSourceFile or
            CodeReviewRuleIds.FileEncodingChanged or
            CodeReviewRuleIds.FileNewlineChanged or
            CodeReviewRuleIds.FormattingOnlyChange or
            CodeReviewRuleIds.TrailingWhitespaceOnlyChange => RepoHygiene,

            _ => Maintainability
        };
    }
}

// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services.Checks;

public static class CodeReviewRuleIds
{
    public const string AsyncVoid = "async-void";
    public const string AsyncMethodNameSuffix = "async-method-name-suffix";
    public const string PublicMethodArgumentGuards = "public-method-argument-guards";
    public const string EmptyCatch = "empty-catch";
    public const string EmptyMethod = "empty-method";
    public const string EmptyIf = "empty-if";
    public const string SwallowingCatch = "swallowing-catch";
    public const string LockThisOrPublic = "lock-this-or-public";
    public const string TaskRunAsync = "task-run-async";
    public const string UnnecessaryTaskCompletion = "unnecessary-task-completion";
    public const string MissingXmlDocs = "missing-xml-docs";
    public const string EmptyXmlDocContent = "empty-xml-doc-content";
    public const string MissingTests = "missing-tests";
    public const string MissingTestsForPublicMethods = "missing-tests-public-methods";
    public const string MissingTestsForPublicProperties = "missing-tests-public-properties";
    public const string PropertyCanBeAutoProperty = "property-can-be-auto-property";
    public const string PrivatePropertyShouldBeField = "private-property-should-be-field";
    public const string PropertySetterIgnoresValue = "property-setter-ignores-value";
    public const string PrivateGetOnlyAutoPropertyShouldBeField = "private-get-only-auto-property-should-be-field";
    public const string PrivateFieldCanBeReadonly = "private-field-can-be-readonly";
    public const string PrivateFieldUsedInSingleMethod = "private-field-used-in-single-method";
    public const string MissingBlankLineBetweenMethods = "missing-blank-line-between-methods";
    public const string BlankLineBetweenBracePairs = "blank-line-between-brace-pairs";
    public const string MethodParameterCount = "method-parameter-count";
    public const string GenericTypeNameSuffix = "generic-type-name-suffix";
    public const string IfElseBraceConsistency = "if-else-brace-consistency";
    public const string IfElseUnnecessaryBraces = "if-else-unnecessary-braces";
    public const string ConstructorTooLong = "constructor-too-long";
    public const string StringConcatSameTarget = "string-concat-same-target";
    public const string ConstructorEventSubscriptionLifecycle = "constructor-event-subscription-lifecycle";
    public const string ThreadSleep = "thread-sleep";
    public const string ThrowExInCatch = "throw-ex-in-catch";
    public const string MethodCanBeStatic = "method-can-be-static";
    public const string RedundantSelfLookup = "redundant-self-lookup";
    public const string UnnecessaryCast = "unnecessary-cast";
    public const string UnnecessaryVerbatimStringPrefix = "unnecessary-verbatim-string-prefix";
    public const string BooleanLiteralComparison = "boolean-literal-comparison";
    public const string UnobservedTaskResult = "unobserved-task-result";
    public const string DisposableNotDisposed = "disposable-not-disposed";
    public const string DisposeWithoutIDisposable = "dispose-without-idisposable";
    public const string EmptyDispose = "empty-dispose";
    public const string MultipleEnumeration = "multiple-enumeration";
    public const string EagerMaterializedEnumerableReturn = "eager-materialized-enumerable-return";
    public const string LinqCountComparedToZero = "linq-count-compared-to-zero";
    public const string PublicMutableStaticState = "public-mutable-static-state";
    public const string UnusedPrivateMember = "unused-private-member";
    public const string UnusedMethodParameter = "unused-method-parameter";
    public const string ResxMissingLocaleKeys = "resx-missing-locale-keys";
    public const string ResxUnexpectedExtraKeys = "resx-unexpected-extra-keys";
    public const string ResxEmptyTranslationValues = "resx-empty-translation-values";
    public const string ResxValueBoundaryWhitespace = "resx-value-boundary-whitespace";
    public const string ResxMixedEnglishDialect = "resx-mixed-english-dialect";
    public const string ResxAmericanEnglishInBritishLocale = "resx-american-english-in-british-locale";
    public const string ResxBritishEnglishInAmericanLocale = "resx-british-english-in-american-locale";
    public const string UnusedUsingsRoslyn = "unused-usings-roslyn";
    public const string WarningSuppression = "warning-suppression";
    public const string FileEncodingChanged = "file-encoding-changed";
    public const string FileNewlineChanged = "file-newline-changed";
    public const string TrailingWhitespaceOnlyChange = "trailing-whitespace-only-change";
    public const string FormattingOnlyChange = "formatting-only-change";
    public const string MissingTypedBindingContext = "missing-typed-binding-context";
    public const string FixedSizeLayoutContainer = "fixed-size-layout-container";
    public const string SingleChildWrapperContainer = "single-child-wrapper-container";
    public const string NestedSamePanelWrapper = "nested-same-panel-wrapper";
    public const string EmptyContainer = "empty-container";
    public const string MultipleClassesPerFile = "multiple-classes-per-file";
    public const string SourceFileNameMismatch = "source-file-name-mismatch";
    public const string MissingReadmeForNewProject = "missing-readme-for-new-project";
    public const string MissingDisclaimerForNewSourceFile = "missing-disclaimer-for-new-source-file";
    public const string UnusedLocalVariable = "unused-local-variable";
    public const string LocalVariablePrefix = "local-variable-prefix";
    public const string UnnecessaryEnumMemberValue = "unnecessary-enum-member-value";
    public const string ConsecutiveBooleanArguments = "consecutive-boolean-arguments";
    public const string ConsecutiveNullArguments = "consecutive-null-arguments";
    public const string LambdaCanBeMethodGroup = "lambda-can-be-method-group";
    public const string DuplicateCodeBlock = "duplicate-code-block";
    public const string NumericStringCultureForFileWrite = "numeric-string-culture-for-file-write";
    public const string BooleanMethodAlwaysReturnsSameValue = "bool-method-always-returns-same-value";
    public const string PrivateBooleanMethodAlwaysReturnsSameValue = "private-bool-method-always-returns-same-value";
    public const string NamedReflectionMemberLookup = "named-reflection-member-lookup";
    public const string JsonTrailingComma = "json-trailing-comma";
}

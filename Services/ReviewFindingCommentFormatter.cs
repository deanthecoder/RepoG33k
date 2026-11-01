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

namespace ReviewG33k.Services;

/// <summary>
/// Formats review findings into short pull-request comments that read more naturally than raw check messages.
/// </summary>
/// <remarks>
/// Useful for keeping ReviewG33k's on-screen findings brief while posting Bitbucket comments in a more
/// reviewer-friendly tone. Unknown rules fall back to the original finding message.
/// </remarks>
internal sealed class ReviewFindingCommentFormatter
{
    public string Format(CodeSmellFinding finding)
    {
        if (finding == null)
            return string.Empty;

        return finding.RuleId switch
        {
            CodeReviewRuleIds.MethodCanBeStatic => "This method looks like it could be made static, since it does not appear to use instance state.",
            CodeReviewRuleIds.ConsecutiveBooleanArguments => "These adjacent boolean arguments are a little hard to read positionally. Naming them would make the intent clearer.",
            CodeReviewRuleIds.ConsecutiveNullArguments => "These adjacent null arguments are hard to read positionally. Naming them would make the intent clearer.",
            CodeReviewRuleIds.LambdaCanBeMethodGroup => "This lambda looks like it can be simplified to a method group.",
            CodeReviewRuleIds.LocalVariablePrefix => "This local variable uses a field-style prefix, which makes it harder to distinguish from actual fields.",
            CodeReviewRuleIds.PublicMethodArgumentGuards => "This method uses reference or nullable arguments in a way that may benefit from an early guard for clarity and safety.",
            CodeReviewRuleIds.NumericStringCultureForFileWrite => "This numeric value may be written using the current culture. Using invariant culture would make the output more stable across locales.",
            CodeReviewRuleIds.DuplicateCodeBlock => "This block looks very similar to code that already exists elsewhere in the repository. It may be worth reusing or extracting the shared logic.",
            CodeReviewRuleIds.BlankLineBetweenBracePairs => "The blank line between these brace lines looks unnecessary and could be removed for consistency.",
            CodeReviewRuleIds.PrivateFieldCanBeReadonly => "This field looks like it could be marked readonly, since it is only assigned during initialization.",
            CodeReviewRuleIds.PrivateFieldUsedInSingleMethod => "This field seems to be used in just one method, so a local variable may be clearer.",
            CodeReviewRuleIds.UnusedLocalVariable => "This local variable does not appear to be used and may be safe to remove.",
            CodeReviewRuleIds.UnusedPrivateMember => "This private member does not appear to be used and may be safe to remove.",
            CodeReviewRuleIds.UnusedUsingsRoslyn => "This using directive does not appear to be needed anymore.",
            CodeReviewRuleIds.PropertyCanBeAutoProperty => "This property looks like it can be simplified to an auto-property.",
            CodeReviewRuleIds.PrivatePropertyShouldBeField => "This private property looks like it could be a field instead.",
            CodeReviewRuleIds.PrivateGetOnlyAutoPropertyShouldBeField => "This private get-only auto-property looks like it could be a field instead.",
            CodeReviewRuleIds.MissingBlankLineBetweenMethods => "A blank line between these methods would make the code a little easier to scan.",
            CodeReviewRuleIds.IfElseUnnecessaryBraces => "These braces look unnecessary and could likely be removed to simplify the control flow.",
            CodeReviewRuleIds.IfElseBraceConsistency => "The brace style here looks inconsistent. Aligning both branches would make it easier to read.",
            CodeReviewRuleIds.UnnecessaryEnumMemberValue => "This explicit enum value looks unnecessary, since it matches the default sequence.",
            CodeReviewRuleIds.BooleanLiteralComparison => "This boolean comparison looks more complicated than it needs to be and could likely be simplified.",
            CodeReviewRuleIds.UnnecessaryCast => "This cast looks unnecessary and may be safe to remove.",
            CodeReviewRuleIds.RedundantSelfLookup => "This lookup appears to resolve the current instance again, which looks redundant.",
            CodeReviewRuleIds.UnnecessaryVerbatimStringPrefix => "The verbatim string prefix here looks unnecessary.",
            CodeReviewRuleIds.UnnecessaryTaskCompletion => "This method returns an already-completed Task without doing asynchronous work. Returning a plain value may keep the caller chain simpler.",
            _ => finding.Message ?? string.Empty
        };
    }
}

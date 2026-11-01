# RepoG33k daily-workflow UX review

Reviewed on October 31, 2026 against the running local macOS build and its implementation.

The canvas-first approach works well for understanding a change across files. The main gaps are around making Git state visible, choosing the right operation, and finishing a PR review. It is already useful as a change viewer; several routine tasks still send the user back to Fork or Bitbucket.

## Daily task checklist

| User task | Current experience | Assessment / next improvement |
| --- | --- | --- |
| Find and open a repository | Remembered parent folder, colored selector, selectable disk path | Good foundation. With 92 discovered repos, add search/filtering and distinguish nested repos/submodules. |
| Know which branch is checked out | Header branch and starred local branch | Good. Keep the checked-out branch visually distinct from the branch being compared. |
| Get the latest remote state | Explicit Fetch; persistent upstream, sync status, and last-fetch timestamp; cached repo-list badges | Implemented local status visibility. Fetch still explicitly checks the server; a Synced badge alone does not. |
| Pull current branch | Fast-forward-only pull | Safe default, but divergence fails after clicking. Explain the state and offer the appropriate next action, such as rebase onto upstream. |
| Create a feature branch | Branch-name and starting-point dialog; defaults to the current branch | Explicit remote tracking actions retain their selected remote starting point. Local/remote choices are labeled. Next: show freshness. |
| Switch branches | Right-click branch, then Switch | Available but hidden. Add a visible Switch action for the selected branch; preserve single-click comparison behavior. Explain when working changes would carry across. |
| Delete a finished branch | Branch context menu with local/remote labels and confirmation | Checked-out branches are protected; unmerged local deletion is explicit and saves a recovery ref. Remote deletion checks that the server branch has not moved. |
| Inspect all, staged, or unstaged changes | Three scopes feeding one canvas | Strong core workflow. Make staged versus unstaged state easier to read on each file row, especially partially staged files. |
| Find a specific change in a large set | Alphabetical list; click focuses canvas; reveal-file menu | Good navigation. Add filename/path filtering and search across the comparison. |
| Choose what goes into a commit | Whole-file +/− and Stage all / Unstage all | Enough for simple commits. Hunk/selection staging is the biggest gap for splitting AI-generated changes into sensible commits. |
| Set aside unfinished work | No stash UI | Deferred at the user's preference. A future version can add stash with a message, list/inspect, apply/pop, and clear handling of conflicts. Rebase currently tells users to stash without providing a way to do it. |
| Commit or amend | Prefix conventions, editable draft, amend confirmation, staged-tree guard | Good foundation. Disable actions when prerequisites are missing and explain why beside the action, rather than showing a generic error after clicking. |
| Rebase a feature branch | Explicit dialog, recovery ref, continue/abort banner | Fixed invisible target/default fallback. Next: show current branch → target, dirty-state blockers, incoming commit summary, and clear upstream/base choices before starting. |
| Resolve a conflict and continue | Edit, stage resolved file, Continue/Abort, copy Codex task | Useful primitives. Add a conflicts-only list, unresolved count, and guidance explaining the next step. Continue should reflect whether conflicts remain. Automatic Codex resolution is not implemented. |
| Push current work | Push, first-push remote prompt, force-with-lease in context menu | Available. Show destination and outgoing commits persistently; make the rewritten-history path discoverable when normal push cannot work. |
| Review a branch or commit | Context-menu review, canvas markers, shared Results dialog | Good. Results are currently session-local and retain only the most recent review. Label reviewed revision/time and make stale results clear after edits or new commits. |
| Open a Bitbucket PR | Paste URL; isolated review worktree; separate canvas window | Fixed clone-root default, misleading URL example, and required-field validation. Next: retain PR identity/source/target prominently and provide the same results-list entry point in that window. |
| Finish a PR review | Inspect findings and copy fix instructions | Incomplete within RepoG33k. There is no PR comment, approve/request-changes, or merge UI in this workflow. Add an explicit Open PR in browser action first; decide later how much Bitbucket interaction belongs here. |
| Recover from interrupted/failed work | Status messages, dialogs, comparison cancellation, rebase recovery ref | Keep a small operation history with useful Git output and an accessible recovery action. A recovery ref that users cannot discover is only a partial solution. |

## Fixes made during this pass

- Rebase now displays a real default target using full ref identity and readable branch labels. A missing target disables Rebase; confirmation has no silent fallback. The current local branch is excluded from targets. The repository service also rejects an empty target before changing Git state.
- Base selection uses exact ref names, so a local branch named `origin/main` cannot shadow the real remote-tracking branch. Pickers label local and remote choices explicitly.
- New branch uses the same picker and requires both a name and a starting branch. It no longer silently substitutes HEAD for a blank selection.
- Rebase identifies the checked-out branch in its prompt, since selecting another branch in the sidebar only changes the comparison.
- Required-field dialogs now show inline validation, disable their primary action until valid, and support Enter/Escape.
- PR opening uses the chosen repositories parent folder as its default. Its example matches the existing Bitbucket Server/Data Center URL parser; an unsupported URL or missing clone folder is caught before starting a review.
- The collapsed commit composer removes inherited minimum height/padding and uses a compact 26-pixel header, with no reserved space for hidden fields. Collapsed sidebar sections also stop reserving the theme's larger minimum height.

## Suggested order of work

1. **Make Git state obvious:** upstream/ahead/behind/fetch timestamp and repo badges are implemented. Next are clearer operation prerequisites and a more explicit distinction between checkout and comparison.
2. **Complete the everyday branch loop:** visible switching and a guided rebase flow from the current branch onto an explicit target. Stash/restore is deferred.
3. **Complete the PR review loop:** shared results list in PR windows, PR metadata, and a browser handoff for commenting/approval. Keep isolated worktrees.
4. **Improve commit selection:** file-state badges, path search, then hunk/selection staging.
5. **Make history and recovery useful:** searchable commit list, outgoing/incoming views, last-review identity, operation log, and discoverable recovery refs. A full commit graph is less urgent than these tasks.

## Verification and boundaries

The original rebase, create-branch, and PR dialogs were inspected in the running app. The rebuilt dialogs were checked for visible defaults, disabled actions on incomplete input, and Escape-to-cancel. The collapsed composer was visually checked. Git-changing actions were not executed against the user's working repository during this audit. All 837 tests passed, including missing-rebase-target tests against disposable repositories. Remote authentication, real PR submission, and Windows Explorer behavior were not exercised in this macOS pass.

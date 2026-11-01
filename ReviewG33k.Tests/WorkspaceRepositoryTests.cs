// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using RepoG33k.Services;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Tests;

/// <summary>Exercises repository mutations against disposable Git repositories.</summary>
/// <remarks>These tests protect partial staging, snapshot selection, and branch recovery behavior.</remarks>
[TestFixture]
public sealed class WorkspaceRepositoryTests
{
    private string m_root;
    private RepositoryService m_service;

    [SetUp]
    public async Task SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "difflens-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_service = new RepositoryService();
        await Git("init", "--initial-branch=main");
        await Git("config", "user.name", "DiffLens Test");
        await Git("config", "user.email", "difflens@example.invalid");
        await Git("config", "commit.gpgsign", "false");
        await Git("config", "core.hooksPath", Path.Combine(m_root, "no-hooks"));
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "original\n");
        await Git("add", "--", "sample.txt");
        await Git("commit", "-m", "Initial");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(m_root, recursive: true);

    private Task<string> Git(params string[] args) => m_service.RunAsync(m_root, args);

    [Test]
    public async Task BranchStatusDistinguishesLocalKnowledgeFromFetchingTheServer()
    {
        var initial = await m_service.ReadBranchStatusAsync(m_root);
        Assert.That(initial.Badge, Is.EqualTo("No upstream"));
        Assert.That(initial.LastFetched, Is.Null);
        var remoteRoot = Path.Combine(m_root, "remote.git");
        await Git("init", "--bare", remoteRoot);
        await Git("remote", "add", "origin", remoteRoot);
        await Git("push", "--set-upstream", "origin", "main");
        var synced = await m_service.ReadBranchStatusAsync(m_root);
        Assert.That(synced.Badge, Is.EqualTo("✓ Synced"));
        Assert.That(synced.Upstream, Is.EqualTo("origin/main"));
        Assert.That(synced.LastFetched, Is.Null, "Pushing must not be reported as fetching.");
        var baseTip = (await Git("rev-parse", "HEAD")).Trim();
        await Git("commit", "--allow-empty", "-m", "Incoming");
        var incoming = (await Git("rev-parse", "HEAD")).Trim();
        await Git("push", "origin", "HEAD:refs/heads/temporary");
        await m_service.RunAsync(remoteRoot, "update-ref", "refs/heads/main", incoming);
        await Git("reset", "--hard", baseTip);
        Assert.That((await m_service.ReadBranchStatusAsync(m_root)).Badge, Is.EqualTo("✓ Synced"), "Reading badges must not contact the server.");
        await Git("fetch", "origin");
        var behind = await m_service.ReadBranchStatusAsync(m_root);
        Assert.That(behind.Badge, Is.EqualTo("↓ 1 Behind"));
        Assert.That(behind.LastFetched, Is.Not.Null);
        var fetchTime = behind.LastFetched;
        await Git("commit", "--allow-empty", "-m", "Outgoing");
        var diverged = await m_service.ReadBranchStatusAsync(m_root);
        Assert.That(diverged.Badge, Is.EqualTo("↑ 1 ↓ 1"));
        Assert.That(diverged.LastFetched, Is.EqualTo(fetchTime));
        await Git("update-ref", "refs/remotes/origin/main", baseTip);
        Assert.That((await m_service.ReadBranchStatusAsync(m_root)).Badge, Is.EqualTo("↑ 1 Ahead"));
        await Git("switch", "--detach");
        Assert.That((await m_service.ReadBranchStatusAsync(m_root)).Badge, Is.EqualTo("Detached HEAD"));
    }

    [Test]
    public async Task DeleteLocalBranchProtectsCurrentAndUnmergedBranchesAndPreservesWorkingFiles()
    {
        var head = (await Git("rev-parse", "HEAD")).Trim();
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.DeleteBranchAsync(m_root, new RepositoryRef("main", "refs/heads/main", false), head));
        await Git("branch", "merged");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "unfinished work\n");
        await m_service.DeleteBranchAsync(m_root, new RepositoryRef("merged", "refs/heads/merged", false), head);
        Assert.That((await m_service.ReadAsync(m_root)).Branches.Any(b => b.Name == "merged"), Is.False);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "sample.txt")), Is.EqualTo("unfinished work\n"));
        await Git("restore", "--", "sample.txt");
        await Git("switch", "-c", "unmerged");
        await File.WriteAllTextAsync(Path.Combine(m_root, "feature.txt"), "feature\n");
        await Git("add", ".");
        await Git("commit", "-m", "Feature");
        var tip = (await Git("rev-parse", "HEAD")).Trim();
        await Git("switch", "main");
        var branch = new RepositoryRef("unmerged", "refs/heads/unmerged", false);
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.DeleteBranchAsync(m_root, branch, tip));
        var recovery = await m_service.DeleteBranchAsync(m_root, branch, tip, allowUnmerged: true);
        Assert.That((await Git("rev-parse", recovery)).Trim(), Is.EqualTo(tip));
    }

    [Test]
    public async Task DeleteLocalBranchRejectsMovedTipsAndOtherWorktrees()
    {
        var head = (await Git("rev-parse", "HEAD")).Trim();
        await Git("branch", "other");
        var branch = new RepositoryRef("other", "refs/heads/other", false);
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.DeleteBranchAsync(m_root, branch, new string('0', head.Length), true));
        var worktree = Path.Combine(m_root, "linked-worktree");
        await Git("worktree", "add", worktree, "other");
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => m_service.DeleteBranchAsync(m_root, branch, head, true));
            Assert.That((await Git("rev-parse", branch.FullName)).Trim(), Is.EqualTo(head));
        }
        finally { await Git("worktree", "remove", worktree); }
    }

    [Test]
    public async Task DeleteRemoteBranchUsesALeaseAndKeepsLocalBranches()
    {
        var remoteRoot = Path.Combine(m_root, "remote.git");
        await Git("init", "--bare", remoteRoot);
        await Git("remote", "add", "test-remote", remoteRoot);
        await Git("branch", "feature/topic");
        await Git("push", "test-remote", "feature/topic");
        var branch = new RepositoryRef("test-remote/feature/topic", "refs/remotes/test-remote/feature/topic", true);
        var tip = (await Git("rev-parse", branch.FullName)).Trim();
        // Simulate someone moving the server branch after the local remote-tracking snapshot.
        await Git("commit", "--allow-empty", "-m", "Remote advance");
        var advanced = (await Git("rev-parse", "HEAD")).Trim();
        await Git("push", "test-remote", "HEAD:refs/heads/elsewhere");
        await m_service.RunAsync(remoteRoot, "update-ref", "refs/heads/feature/topic", advanced);
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.DeleteBranchAsync(m_root, branch, tip));
        await Git("fetch", "test-remote");
        await m_service.DeleteBranchAsync(m_root, branch, advanced);
        Assert.That(await m_service.RunAsync(remoteRoot, "for-each-ref", "refs/heads/feature/topic"), Is.Empty);
        Assert.That((await Git("rev-parse", "refs/heads/feature/topic")).Trim(), Is.EqualTo(tip));
    }

    [Test]
    public async Task CommitUsesWorkingChangesWhenNothingIsStaged()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "updated\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "new.txt"), "new\n");
        await m_service.CommitWorkingOrStagedAsync(m_root, "Save working changes", null, false, await m_service.IndexTreeAsync(m_root));
        Assert.That(await Git("show", "HEAD:sample.txt"), Is.EqualTo("updated\n"));
        Assert.That(await Git("show", "HEAD:new.txt"), Is.EqualTo("new\n"));
    }

    [Test]
    public async Task CommitPrefersStagedChangesAndLeavesOtherEditsAlone()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "staged\n");
        await Git("add", "sample.txt");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "unstaged\n");
        await m_service.CommitWorkingOrStagedAsync(m_root, "Save staged changes", null, false, await m_service.IndexTreeAsync(m_root));
        Assert.That(await Git("show", "HEAD:sample.txt"), Is.EqualTo("staged\n"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "sample.txt")), Is.EqualTo("unstaged\n"));
    }

    [Test]
    public async Task StagingAndCommittingPreserveUnstagedChanges()
    {
        var path = Path.Combine(m_root, "sample.txt");
        await File.WriteAllTextAsync(path, "staged version\n");
        await Git("add", "--", "sample.txt");
        await File.WriteAllTextAsync(path, "later working version\n");
        var snapshot = await m_service.ReadAsync(m_root);
        var status = snapshot.Files.Single();
        Assert.Multiple(() =>
        {
            Assert.That(status.IsStaged, Is.True);
            Assert.That(status.IsUnstaged, Is.True);
        });
        await m_service.CommitAsync(m_root, "Feature: Save staged version", "Keep the working edit outside this commit.", false, await m_service.IndexTreeAsync(m_root));
        Assert.That((await Git("show", "HEAD:sample.txt")).Trim(), Is.EqualTo("staged version"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("later working version\n"));
        Assert.That((await m_service.ReadAsync(m_root)).Files.Single().IsStaged, Is.False);
    }

    [Test]
    public async Task CommitRefusesAnIndexThatChangedAfterReview()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "first\n");
        await Git("add", "--", "sample.txt");
        var expected = await m_service.IndexTreeAsync(m_root);
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "second\n");
        await Git("add", "--", "sample.txt");
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.CommitAsync(m_root, "Fix: Save change", null, false, expected));
        Assert.That((await Git("log", "-1", "--format=%s")).Trim(), Is.EqualTo("Initial"));
    }

    [Test]
    public async Task RenameWithSpacesAndNewlinesCanBeUnstagedWithoutChangingDisk()
    {
        const string name = "renamed with spaces\nand a newline.txt";
        await Git("mv", "--", "sample.txt", name);
        var entry = (await m_service.ReadAsync(m_root)).Files.Single();
        Assert.That(entry.Path, Is.EqualTo(name));
        Assert.That(entry.OriginalPath, Is.EqualTo("sample.txt"));
        await m_service.UnstageAsync(m_root, entry);
        Assert.That(File.Exists(Path.Combine(m_root, name)), Is.True);
        Assert.That((await m_service.ReadAsync(m_root)).Files.All(f => !f.IsStaged), Is.True);
    }

    [Test]
    public async Task RefComparisonUsesSelectedBranchWithoutWorkingEdits()
    {
        await m_service.CreateBranchAsync(m_root, "feature/TS-1234-SpeedUpApp", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "feature version\n");
        await Git("add", "--", "sample.txt");
        await Git("commit", "-m", "Feature change");
        await Git("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "unrelated working edit\n");
        var set = await new DiffLensFileLoader().LoadScopeAsync(m_root, "refs/heads/main", new DiffLensScope("ref:feature", "Feature", BranchTip: "refs/heads/feature/TS-1234-SpeedUpApp"));
        Assert.That(set.Entries.Single().File.WorkingText, Is.EqualTo("feature version\n"));
        Assert.That(set.Entries.Single().File.IsReadOnly, Is.True);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "sample.txt")), Is.EqualTo("unrelated working edit\n"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task RebaseRejectsMissingTargetWithoutChangingTheRepository(string target)
    {
        var head = await Git("rev-parse", "HEAD");
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.RebaseAsync(m_root, target));
        Assert.That(await Git("rev-parse", "HEAD"), Is.EqualTo(head));
        Assert.That(await Git("for-each-ref", "refs/difflens/recovery"), Is.Empty);
    }

    [Test]
    public async Task RebaseCreatesRecoveryRefAndRejectsDirtyWorkingTree()
    {
        await m_service.CreateBranchAsync(m_root, "feature/change", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "feature.txt"), "feature\n");
        await Git("add", "--", "feature.txt");
        await Git("commit", "-m", "Feature");
        var original = (await Git("rev-parse", "HEAD")).Trim();
        await Git("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "base.txt"), "base\n");
        await Git("add", "--", "base.txt");
        await Git("commit", "-m", "Base change");
        await Git("switch", "feature/change");
        await m_service.RebaseAsync(m_root, "refs/heads/main");
        Assert.That((await Git("for-each-ref", "--format=%(objectname)", "refs/difflens/recovery")).Trim(), Is.EqualTo(original));
        Assert.That(File.Exists(Path.Combine(m_root, "base.txt")), Is.True);
        await File.WriteAllTextAsync(Path.Combine(m_root, "dirty.txt"), "dirty\n");
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.RebaseAsync(m_root, "refs/heads/main"));
    }

    [Test]
    public async Task ConflictStateRemainsAvailableAfterFailedRebase()
    {
        await m_service.CreateBranchAsync(m_root, "feature/conflict", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "feature\n");
        await Git("add", "--", "sample.txt");
        await Git("commit", "-m", "Feature");
        await Git("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "main\n");
        await Git("add", "--", "sample.txt");
        await Git("commit", "-m", "Main");
        await Git("switch", "feature/conflict");
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.RebaseAsync(m_root, "main"));
        var state = await m_service.ReadAsync(m_root);
        Assert.That(state.Rebasing, Is.True);
        Assert.That(state.Files.Single().IsConflict, Is.True);
        await Git("rebase", "--abort");
        Assert.That((await m_service.ReadAsync(m_root)).Rebasing, Is.False);
    }

    [Test]
    public async Task UnbornRepositorySupportsLocalAndStagedComparisons()
    {
        var emptyRoot = Path.Combine(m_root, "empty-repository");
        Directory.CreateDirectory(emptyRoot);
        await m_service.RunAsync(emptyRoot, "init", "--initial-branch=main");
        await File.WriteAllTextAsync(Path.Combine(emptyRoot, "new.txt"), "first staged version\n");
        await m_service.RunAsync(emptyRoot, "add", "--", "new.txt");
        await File.WriteAllTextAsync(Path.Combine(emptyRoot, "new.txt"), "working version\n");
        var loader = new DiffLensFileLoader();
        var local = await loader.LoadScopeAsync(emptyRoot, "HEAD", new DiffLensScope("local", "Local"));
        var staged = await loader.LoadScopeAsync(emptyRoot, "HEAD", new DiffLensScope("staged", "Staged"));
        Assert.That(local.Entries.Single().File.WorkingText, Is.EqualTo("working version\n"));
        Assert.That(staged.Entries.Single().File.WorkingText, Is.EqualTo("first staged version\n"));
        await m_service.UnstageAsync(emptyRoot, (await m_service.ReadAsync(emptyRoot)).Files.Single());
        Assert.That(File.Exists(Path.Combine(emptyRoot, "new.txt")), Is.True);
        Assert.That((await m_service.ReadAsync(emptyRoot)).Files.Single().IsStaged, Is.False);
    }

    [Test]
    public async Task ExplicitPushLeaseRejectsRemoteChangesEvenAfterBackgroundFetch()
    {
        var remote = Path.Combine(m_root, "remote.git");
        var second = Path.Combine(m_root, "second-clone");
        await Git("init", "--bare", "--initial-branch=main", remote);
        await Git("remote", "add", "origin", remote);
        await Git("push", "--set-upstream", "origin", "main");
        var expected = (await m_service.ReadAsync(m_root)).UpstreamHead;
        await Git("clone", remote, second);
        await File.WriteAllTextAsync(Path.Combine(second, "sample.txt"), "someone else's edit\n");
        await m_service.RunAsync(second, "add", "--", "sample.txt");
        await m_service.RunAsync(second, "-c", "user.name=Second Test", "-c", "user.email=second@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Remote edit");
        await m_service.RunAsync(second, "push");
        await Git("fetch", "origin");
        Assert.ThrowsAsync<InvalidOperationException>(() => m_service.PushWithLeaseAsync(m_root, expected));
        Assert.That((await m_service.RunAsync(remote, "log", "-1", "--format=%s")).Trim(), Is.EqualTo("Remote edit"));
    }

    [Test]
    public async Task ImmutableComparisonKeysInvalidateWhenEitherBranchMoves()
    {
        await m_service.CreateBranchAsync(m_root, "feature/cache", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "feature.txt"), "feature\n");
        await Git("add", "--", "feature.txt");
        await Git("commit", "-m", "Feature");
        var loader = new DiffLensFileLoader();
        var scope = new DiffLensScope("ref:feature/cache", "Feature", BranchTip: "refs/heads/feature/cache");
        var original = await loader.GetImmutableCacheKeyAsync(m_root, "refs/heads/main", scope);
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.txt"), "unstaged edit\n");
        Assert.That(await loader.GetImmutableCacheKeyAsync(m_root, "refs/heads/main", scope), Is.EqualTo(original), "Working edits cannot alter an immutable branch comparison.");
        Assert.That(await loader.GetImmutableCacheKeyAsync(m_root, "main", new DiffLensScope("local", "Local")), Is.Null);
        await Git("add", "--", "sample.txt");
        await Git("commit", "-m", "Updated feature");
        var afterFeature = await loader.GetImmutableCacheKeyAsync(m_root, "refs/heads/main", scope);
        Assert.That(afterFeature, Is.Not.EqualTo(original));
        await Git("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "base.txt"), "base edit\n");
        await Git("add", "--", "base.txt");
        await Git("commit", "-m", "Updated base");
        await Git("switch", "feature/cache");
        Assert.That(await loader.GetImmutableCacheKeyAsync(m_root, "refs/heads/main", scope), Is.Not.EqualTo(afterFeature));
    }

    [Test]
    public async Task StagedComparisonsReusePreparedRowsUntilTheIndexOrHeadChanges()
    {
        var path = Path.Combine(m_root, "sample.txt");
        var scope = new DiffLensScope("staged", "Staged");
        var loader = new DiffLensFileLoader();
        var cache = new DiffLensComparisonCache();
        await File.WriteAllTextAsync(path, "staged version\n");
        await Git("add", "--", "sample.txt");
        var key = await loader.GetImmutableCacheKeyAsync(m_root, "main", scope);
        var set = await loader.LoadScopeAsync(m_root, "main", scope);
        var comparison = new DiffLensLoadedComparison(("main", [scope]), scope, set, set.Entries.Select(e => new DiffLensCell(e)).ToArray());
        cache.Store(key, comparison);
        await File.WriteAllTextAsync(path, "working edit that is not staged\n");
        var unchanged = await loader.GetImmutableCacheKeyAsync(m_root, "main", scope);
        Assert.That(unchanged, Is.EqualTo(key));
        Assert.That(cache.TryGet(unchanged, out var reused), Is.True);
        Assert.That(reused.Cells, Is.SameAs(comparison.Cells));
        Assert.That(reused.ChangeSet.Entries.Single().File.WorkingText, Is.EqualTo("staged version\n"));
        await Git("add", "--", "sample.txt");
        Assert.That(cache.TryGet(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), out _), Is.False, "Staging new content must invalidate the prepared view.");
        await Git("reset", "--quiet", "HEAD", "--", "sample.txt");
        Assert.That(cache.TryGet(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), out _), Is.False, "Unstaging must select a different comparison.");
        await File.WriteAllTextAsync(path, "staged version\n");
        await Git("add", "--", "sample.txt");
        Assert.That(cache.TryGet(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), out _), Is.True, "The identical index tree can reuse its old view.");
        await Git("commit", "-m", "Commit staged contents");
        Assert.That(cache.TryGet(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), out _), Is.False, "Advancing HEAD must invalidate the old comparison.");
    }

    [Test]
    public async Task StagedSnapshotRemainsPinnedWhenAnotherToolChangesTheIndex()
    {
        var path = Path.Combine(m_root, "sample.txt");
        var loader = new DiffLensFileLoader();
        var scope = new DiffLensScope("staged", "Staged");
        await File.WriteAllTextAsync(path, "first staged snapshot\n");
        await Git("add", "--", "sample.txt");
        var captured = await loader.CaptureStagedSnapshotAsync(m_root);
        var key = await loader.GetImmutableCacheKeyAsync(m_root, "main", scope, stagedSnapshot: captured);
        await File.WriteAllTextAsync(path, "second staged snapshot\n");
        await Git("add", "--", "sample.txt");
        var snapshot = await loader.LoadScopeAsync(m_root, "main", scope, stagedSnapshot: captured);
        Assert.That(snapshot.Entries.Single().File.WorkingText, Is.EqualTo("first staged snapshot\n"));
        Assert.That(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), Is.Not.EqualTo(key));
        var beforeAmend = await loader.GetImmutableCacheKeyAsync(m_root, "main", scope);
        await Git("commit", "--amend", "--only", "-m", "Amended baseline message");
        Assert.That(await loader.GetImmutableCacheKeyAsync(m_root, "main", scope), Is.Not.EqualTo(beforeAmend), "A message-only amend changes the baseline identity even when file contents stay the same.");
    }

    [Test]
    public async Task StagedBatchLoadingHandlesManyFilesWithoutReadingWorkingVersions()
    {
        for (var i = 0; i < 60; i++)
            await File.WriteAllTextAsync(Path.Combine(m_root, $"batch-{i:000}.txt"), $"baseline {i}\n");
        await Git("add", "--all");
        await Git("commit", "-m", "Many baseline files");
        for (var i = 0; i < 60; i++)
            await File.WriteAllTextAsync(Path.Combine(m_root, $"batch-{i:000}.txt"), $"staged {i}\n");
        await Git("add", "--all");
        for (var i = 0; i < 60; i++)
            File.Delete(Path.Combine(m_root, $"batch-{i:000}.txt"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var staged = await new DiffLensFileLoader().LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        TestContext.Progress.WriteLine($"Loaded {staged.Entries.Count} staged files in {watch.ElapsedMilliseconds} ms.");
        Assert.That(staged.Entries.Count, Is.EqualTo(60));
        for (var i = 0; i < 60; i++)
        {
            var file = staged.Entries.Single(e => e.Path == $"batch-{i:000}.txt").File;
            Assert.That(file.Baseline, Is.EqualTo($"baseline {i}\n"));
            Assert.That(file.WorkingText, Is.EqualTo($"staged {i}\n"));
        }
    }

    [Test]
    public async Task ComparisonLoadingHonorsCancellationAndBoundsHistory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => new DiffLensFileLoader(cancellation.Token).LoadScopeAsync(m_root, "main", new DiffLensScope("local", "Local")));
        await Git("commit", "--allow-empty", "-m", "Another commit");
        var catalog = await new DiffLensFileLoader().GetScopesAsync(m_root, "refs/heads/main", maxCommits: 1);
        Assert.That(catalog.Scopes.Count(s => s.Commit != null), Is.EqualTo(1));
    }

    [Test]
    public void StatusParserKeepsBothAddedConflictsOutOfStagedCount()
    {
        var status = RepositoryService.ParseStatus("AA conflict.txt\0?? untracked file.txt\0");
        Assert.That(status[0].IsConflict, Is.True);
        Assert.That(status[0].IsStaged, Is.False);
        Assert.That(status[1].IsUnstaged, Is.True);
    }
}

/// <summary>Checks deterministic commit prefixes and Jira branch detection.</summary>
/// <remarks>These rules stay outside the AI prompt so the application enforces the convention.</remarks>
[TestFixture]
public sealed class WorkspaceCommitConventionTests
{
    [TestCase("feature/TS-1234-SpeedUpApp", "TS-1234")]
    [TestCase("fix/ABC2-9-crash", "ABC2-9")]
    public void FindsStoryIdInBranch(string branch, string expected)
        => Assert.That(CommitConvention.DetectJiraIds(branch), Is.EqualTo(new[] { expected }));

    [Test]
    public void AppliesJiraPrefixWithoutDuplicatingHomePrefix()
        => Assert.That(CommitConvention.Apply("Fix: Speed up startup", new RepositoryPreferences { Convention = "jira" }, "feature/TS-1234-SpeedUpApp"), Is.EqualTo("[TS-1234] Speed up startup"));

    [Test]
    public void AmbiguousOrMissingStoryIdsRequireAnOverride()
    {
        Assert.Throws<InvalidOperationException>(() => CommitConvention.Apply("Fix startup", new RepositoryPreferences { Convention = "jira" }, "feature/startup"));
        Assert.Throws<InvalidOperationException>(() => CommitConvention.Apply("Fix startup", new RepositoryPreferences { Convention = "jira" }, "feature/TS-1234-TS-5678"));
        Assert.That(CommitConvention.Apply("Fix startup", new RepositoryPreferences { Convention = "jira", JiraOverride = "TS-5678" }, "feature/TS-1234-TS-5678"), Is.EqualTo("[TS-5678] Fix startup"));
    }

    [TestCase("Fix")]
    [TestCase("Feature")]
    [TestCase("Other")]
    public void AppliesHomeCategory(string category)
        => Assert.That(CommitConvention.Apply("Update README", new RepositoryPreferences { Category = category }, "main"), Is.EqualTo(category + ": Update README"));
}

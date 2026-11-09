// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensTests
{
    private string m_root;
    private readonly GitCommandRunner m_git = new();

    [SetUp]
    public async Task SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "ReviewG33k-DiffLens-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        await Git("init", "-b", "main");
        await Git("config", "user.name", "DiffLens Tests");
        await Git("config", "user.email", "difflens@example.invalid");
        await Git("config", "core.autocrlf", "false");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "class Sample\n{\n    int Value = 1;\n}\n");
        await Git("add", ".");
        await Git("commit", "-m", "Baseline");
        await Git("switch", "-c", "feature/test");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var file in Directory.EnumerateFiles(m_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(m_root, true);
    }

    [Test]
    public async Task ComparisonScopesSeparateIndexWorkingTreeAndCommitSnapshotsAsync()
    {
        var path = Path.Combine(m_root, "sample.cs");
        var original = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, "staged version\n");
        await Git("add", "sample.cs");
        await File.WriteAllTextAsync(path, "working version\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "new.glsl"), "void main() {}\n");
        var loader = new DiffLensFileLoader();
        var catalog = await loader.GetScopesAsync(m_root, "main");
        Assert.That(catalog.Scopes.Select(s => s.Id), Does.Contain("staged").And.Contain("unstaged"));
        var staged = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        var stagedFile = staged.Entries.Single().File;
        Assert.That(stagedFile.Baseline, Is.EqualTo(original));
        Assert.That(stagedFile.WorkingText, Is.EqualTo("staged version\n"));
        Assert.That(stagedFile.CanEdit, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(() => stagedFile.SaveAsync("do not write"));
        var unstaged = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope("unstaged", "Unstaged"));
        var workingFile = unstaged.Entries.Single(e => e.Path == "sample.cs").File;
        Assert.That(workingFile.Baseline, Is.EqualTo("staged version\n"));
        Assert.That(workingFile.WorkingText, Is.EqualTo("working version\n"));
        Assert.That(workingFile.CanEdit, Is.True);
        Assert.That(unstaged.Entries.Select(e => e.Path), Does.Contain("new.glsl"));
        await Git("commit", "-m", "Staged snapshot");
        var sha = (await m_git.RunAsync(m_root, "rev-parse", "HEAD")).StandardOutput.Trim();
        var commit = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope(sha, "Commit", sha));
        Assert.That(commit.Entries.Single().File.WorkingText, Is.EqualTo("staged version\n"));
        Assert.That(commit.Entries.Single().File.CanEdit, Is.False);
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("working version\n"));
        catalog = await loader.GetScopesAsync(m_root, "main");
        Assert.That(catalog.Scopes.Where(s => s.Commit != null).Select(s => s.Commit), Is.EqualTo(new[] { sha }));
        Assert.That(catalog.Scopes.Select(s => s.Id), Does.Not.Contain("staged").And.Not.Contain("unstaged"));
        catalog = await loader.GetScopesAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        Assert.That(catalog.Scopes.Select(s => s.Id), Does.Contain("staged"));
        Assert.That((await loader.LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"))).Entries, Is.Empty);
    }

    [Test]
    public async Task CommitScopeHandlesRenamesDeletionsAndRootCommitsAsync()
    {
        var loader = new DiffLensFileLoader();
        var rootSha = (await m_git.RunAsync(m_root, "rev-parse", "HEAD")).StandardOutput.Trim();
        var rootCommit = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope(rootSha, "Root", rootSha));
        Assert.That(rootCommit.Entries.Single().File.Baseline, Is.Empty);
        Assert.That(rootCommit.Entries.Single().File.WorkingText, Does.Contain("class Sample"));
        await Git("mv", "sample.cs", "renamed.cpp");
        await Git("commit", "-m", "Rename");
        var renameSha = (await m_git.RunAsync(m_root, "rev-parse", "HEAD")).StandardOutput.Trim();
        var rename = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope(renameSha, "Rename", renameSha));
        Assert.That(rename.Entries.Single().Path, Is.EqualTo("renamed.cpp"));
        Assert.That(rename.Entries.Single().File.Baseline, Is.EqualTo(rename.Entries.Single().File.WorkingText));
        await Git("rm", "renamed.cpp");
        var deletion = await loader.LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        Assert.That(deletion.Entries.Single().File.WorkingText, Is.Empty);
        Assert.That(deletion.Entries.Single().File.IsReadOnly, Is.True);
        Assert.That(deletion.Entries.Single().File.Baseline, Does.Contain("class Sample"));
    }

    [Test]
    public async Task MergedPullRequestComparisonUsesFirstParentInsteadOfTargetBranch()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "// Merged PR change\n");
        await Git("add", ".");
        await Git("commit", "-m", "Merge PR");
        var loader = new DiffLensFileLoader();
        var catalog = await loader.GetScopesAsync(m_root, "HEAD^");
        var set = await loader.LoadScopeAsync(m_root, catalog.Target, new DiffLensScope("branch", "PR"));
        Assert.That(set.Entries.Single().File.Baseline, Does.Contain("class Sample"));
        Assert.That(set.Entries.Single().File.WorkingText, Is.EqualTo("// Merged PR change\n"));
    }

    [Test]
    public async Task CleanRepositoryHidesLocalScopesButKeepsAnExplicitSelectionAsync()
    {
        var loader = new DiffLensFileLoader();
        var catalog = await loader.GetScopesAsync(m_root, "main");
        Assert.That(catalog.Scopes.Select(s => s.Id), Is.EqualTo(new[] { "branch" }));
        catalog = await loader.GetScopesAsync(m_root, "main", new DiffLensScope("local", "All local changes"));
        Assert.That(catalog.Scopes.Select(s => s.Id), Does.Contain("local"));
    }

    private sealed class LoadProgress(Action<DiffLensLoadProgress> report) : IProgress<DiffLensLoadProgress>
    {
        public void Report(DiffLensLoadProgress value) => report(value);
    }

    [Test]
    public async Task LoadingReportsFileCountsAndCanBeCancelledBetweenFilesAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "first.txt"), "first\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "second.txt"), "second\n");
        var updates = new List<DiffLensLoadProgress>();
        await new DiffLensFileLoader(progress: new LoadProgress(updates.Add)).LoadSetAsync(m_root, "HEAD");
        Assert.That(updates.Any(p => p.Total == 2 && p.Completed == 2), Is.True);
        using var cancellation = new CancellationTokenSource();
        var progress = new LoadProgress(p =>
        {
            if (p.Stage == "Loading files" && p.Completed == 1)
                cancellation.Cancel();
        });
        Assert.ThrowsAsync<OperationCanceledException>(() => new DiffLensFileLoader(cancellation.Token, progress).LoadSetAsync(m_root, "HEAD"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "second.txt")), Is.EqualTo("second\n"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelStopsAnActiveGitProcessAsync(bool binary)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Git executes aliases through its shell on Windows as well as Unix.
        var args = new[] { "-c", "alias.wait=!sleep 30", "wait" };
        if (binary)
            Assert.ThrowsAsync<OperationCanceledException>(() => m_git.RunBytesAsync(m_root, cancellation.Token, args));
        else
            Assert.ThrowsAsync<OperationCanceledException>(() => m_git.RunAsync(m_root, cancellation.Token, args));
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That((await m_git.RunAsync(m_root, "status", "--porcelain")).IsSuccess, Is.True);
    }

    [TestCase("same")]
    [TestCase("working")]
    [TestCase("baseline")]
    [TestCase("deleted")]
    public void ReviewedFilesStayCollapsedOnlyWhileTheirFullDiffIsUnchanged(string change)
    {
        var text = string.Join("\n", Enumerable.Range(1, 6000).Select(i => $"line {i}"));
        DiffLensCell Cell(string baseline, byte[] bytes) => new(new DiffLensEntry("large.txt",
            new DiffLensFile(Path.Combine(m_root, "large.txt"), "large.txt", "HEAD", baseline, bytes), null));
        var previous = Cell("original", Encoding.UTF8.GetBytes(text));
        previous.IsReviewed = true;
        previous.Detail = 3;
        var next = Cell(change == "baseline" ? "new baseline" : "original",
            change == "deleted" ? null : Encoding.UTF8.GetBytes(change == "working" ? text + "\nnew last line" : text));
        next.RestoreReviewState(previous);
        Assert.That(next.IsReviewed, Is.EqualTo(change == "same"));
        Assert.That(next.Detail, Is.EqualTo(3));
        Assert.That(next.Height == DiffLensCell.HeaderHeight, Is.EqualTo(change == "same"));
    }

    [Test]
    public void ReviewingFilesShrinksFolderBoundsAndEditsReopenThem()
    {
        var file = new DiffLensFile(Path.Combine(m_root, "sample.cs"), "sample.cs", "HEAD", "old", Encoding.UTF8.GetBytes("new"));
        var cell = new DiffLensCell(new DiffLensEntry("folder/sample.cs", file, null));
        var expanded = DiffLensLayout.Build("repo", [cell]).Bounds.Height;
        cell.IsReviewed = true;
        Assert.That(DiffLensLayout.Build("repo", [cell]).Bounds.Height, Is.LessThan(expanded));
        cell.Update("old", "new");
        Assert.That(cell.IsReviewed, Is.True);
        cell.Update("old", "edited");
        Assert.That(cell.IsReviewed, Is.False);
        Assert.That(DiffLensLayout.Build("repo", [cell]).Bounds.Height, Is.EqualTo(expanded));
    }

    [Test]
    public void LargePreviewsAreCappedWithoutTruncatingTheEditableSnapshot()
    {
        var text = string.Join("\n", Enumerable.Range(1, 6000).Select(i => $"line {i}"));
        var file = new DiffLensFile(Path.Combine(m_root, "large.txt"), "large.txt", "HEAD", "", Encoding.UTF8.GetBytes(text));
        var cell = new DiffLensCell(new DiffLensEntry("large.txt", file, null));
        foreach (var detail in Enumerable.Range(0, 4))
        {
            var rows = cell.Rows(detail);
            Assert.That(rows.Count, Is.EqualTo(5001));
            Assert.That(rows[^1].Notice, Does.Contain("5,000"));
            Assert.That(rows[^2].NewLine, Is.EqualTo(5000));
        }
        Assert.That(file.WorkingText, Is.EqualTo(text));
        Assert.That(cell.FindingRow(5500, 0), Is.EqualTo(-1));
    }

    [TestCase((int)CommandLineReviewMode.Tree)]
    [TestCase((int)CommandLineReviewMode.Committed)]
    [TestCase((int)CommandLineReviewMode.Uncommitted)]
    public void ReviewCanvasRequiresAGitRepository(int modeValue)
    {
        var directory = Path.Combine(m_root, "ordinary-directory");
        Directory.CreateDirectory(directory);
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DiffLensFileLoader().LoadReviewSetAsync(directory, "HEAD", (CommandLineReviewMode)modeValue));
    }

    [Test]
    public async Task LoadSetIncludesDeletedUntrackedAndUnsupportedFilesAgainstOneBase()
    {
        File.Delete(Path.Combine(m_root, "sample.cs"));
        Directory.CreateDirectory(Path.Combine(m_root, "src"));
        await File.WriteAllTextAsync(Path.Combine(m_root, "src", "added.cs"), "new file\n");
        await File.WriteAllBytesAsync(Path.Combine(m_root, "binary.dat"), new byte[] { 0, 1, 2, 0 });
        var set = await new DiffLensFileLoader().LoadSetAsync(m_root, "main");
        Assert.That(set.Entries.Select(entry => entry.Path), Is.EquivalentTo(new[] { "sample.cs", "src/added.cs", "binary.dat" }));
        Assert.That(set.Entries.Single(entry => entry.Path == "binary.dat").Error, Is.Not.Null);
        Assert.That(set.Entries.Single(entry => entry.Path == "sample.cs").File.CanEdit, Is.False);
        Assert.That(set.Entries.Where(entry => entry.File != null).All(entry => entry.File.BaseRevision == set.BaseRevision), Is.True);
    }

    [Test]
    public async Task HeadComparisonExcludesCommittedBranchChangesAndLoadsOnlyLocalChanges()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "committed feature\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "staged.cs"), "baseline staged\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "working.cs"), "baseline working\n");
        await Git("add", ".");
        await Git("commit", "-m", "Feature changes");
        await File.WriteAllTextAsync(Path.Combine(m_root, "staged.cs"), "staged edit\n");
        await Git("add", "staged.cs");
        await File.WriteAllTextAsync(Path.Combine(m_root, "working.cs"), "unstaged edit\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "untracked.cs"), "new file\n");

        var set = await new DiffLensFileLoader().LoadSetAsync(m_root, "HEAD");

        Assert.That(set.Entries.Select(entry => entry.Path),
            Is.EquivalentTo(new[] { "staged.cs", "working.cs", "untracked.cs" }));
        Assert.That(set.Entries.Single(entry => entry.Path == "staged.cs").File.Baseline, Is.EqualTo("baseline staged\n"));
        Assert.That(set.Entries.Single(entry => entry.Path == "working.cs").File.Baseline, Is.EqualTo("baseline working\n"));
    }

    [Test]
    public async Task HeadComparisonOnCleanFeatureBranchShowsNoFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "committed feature\n");
        await Git("commit", "-am", "Feature change");

        var set = await new DiffLensFileLoader().LoadSetAsync(m_root, "HEAD");

        Assert.That(set.Entries, Is.Empty);
    }

    [Test]
    public async Task HeadComparisonUsesCommittedFeatureContentAsBaselineForLocalEdits()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "committed feature\n");
        await Git("commit", "-am", "Feature change");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "local edit\n");

        var file = await new DiffLensFileLoader().LoadAsync(m_root, "HEAD", "sample.cs");

        Assert.That(file.Baseline, Is.EqualTo("committed feature\n"));
        Assert.That(file.WorkingText, Is.EqualTo("local edit\n"));
    }

    // Hey Codex - Do this...
    // TODO [async-method-name-suffix]: Async method `ReviewCanvasUsesGitScopeWithoutScannerFileFilters` should end with `Async`.
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ReviewCanvasUsesGitScopeWithoutScannerFileFilters(int modeValue) // can edit!
    {
        var mode = (CommandLineReviewMode)modeValue;
        await File.WriteAllTextAsync(Path.Combine(m_root, "committed.cs"), "committed feature\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "deleted.cs"), "to delete\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "whitespace.cs"), "line\n");
        await Git("add", ".");
        await Git("commit", "-m", "Feature files");
        File.Delete(Path.Combine(m_root, "deleted.cs"));
        await File.WriteAllTextAsync(Path.Combine(m_root, "whitespace.cs"), "line   \n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "local.cs"), "local change\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "notes.txt"), "not reviewed\n");
        Directory.CreateDirectory(Path.Combine(m_root, "obj"));
        await File.WriteAllTextAsync(Path.Combine(m_root, "obj", "generated.cs"), "generated\n");
        var actual = await new DiffLensFileLoader().LoadReviewSetAsync(m_root, "main", mode);
        var expected = new List<string> { "whitespace.cs", "local.cs", "notes.txt", "obj/generated.cs" };
        if (mode == CommandLineReviewMode.Uncommitted)
            expected.Add("deleted.cs");
        else
            expected.Add("committed.cs");
        if (mode == CommandLineReviewMode.Tree)
            expected.Add("sample.cs");
        Assert.That(actual.Entries.Select(entry => entry.Path), Is.EquivalentTo(expected));
        Assert.That(actual.Entries.All(entry => entry.Error == null), Is.True);
    }

    [TestCase((int)CommandLineReviewMode.Uncommitted)]
    [TestCase((int)CommandLineReviewMode.Committed)]
    [TestCase((int)CommandLineReviewMode.Tree)]
    public async Task ReviewCanvasIncludesCppSourcesAndHeadersAsync(int modeValue)
    {
        var mode = (CommandLineReviewMode)modeValue;
        var nativePaths = new[] { "native.cpp", "native.cc", "native.cxx", "native.h", "native.hpp", "native.hxx" };
        foreach (var path in nativePaths)
            await File.WriteAllTextAsync(Path.Combine(m_root, path), "int value = 1;\n");
        await Git("add", ".");
        await Git("commit", "-m", "Native source");
        foreach (var path in nativePaths)
            await File.WriteAllTextAsync(Path.Combine(m_root, path), "int value = 2;\n");
        await Git("add", "native.cpp");
        await File.WriteAllTextAsync(Path.Combine(m_root, "new.cpp"), "int added;\n");
        Directory.CreateDirectory(Path.Combine(m_root, "obj"));
        await File.WriteAllTextAsync(Path.Combine(m_root, "obj", "generated.cpp"), "generated\n");

        var actual = await new DiffLensFileLoader().LoadReviewSetAsync(m_root, "main", mode);

        foreach (var path in nativePaths.Append("new.cpp"))
        {
            var entry = actual.Entries.Single(item => item.Path == path);
            Assert.That(entry.Error, Is.Null);
            Assert.That(entry.File.CanEdit, Is.True);
        }
        var modified = actual.Entries.Single(item => item.Path == "native.cpp").File;
        Assert.That(modified.WorkingText, Is.EqualTo("int value = 2;\n"));
        Assert.That(modified.Baseline, Is.EqualTo(mode == CommandLineReviewMode.Committed ? string.Empty : "int value = 1;\n"));
        Assert.That(actual.Entries.Select(item => item.Path), Does.Contain("obj/generated.cpp"));
        var scanner = await new GitWorkingTreeChangedFileSource(m_git, m_root).LoadAsync();
        Assert.That(scanner.Files.Select(file => file.Path), Does.Not.Contain("native.cpp"));
    }

    [TestCase((int)CommandLineReviewMode.Committed)]
    [TestCase((int)CommandLineReviewMode.Uncommitted)]
    [TestCase((int)CommandLineReviewMode.Tree)]
    public async Task ReviewCanvasShowsArbitraryTextAndBinaryPlaceholdersAsync(int modeValue)
    {
        var files = new Dictionary<string, string>
        {
            ["README.md"] = "documentation\n", ["settings.json"] = "{ }\n",
            ["shader.glsl"] = "void main() {}\n", ["Makefile"] = "all: build\n",
            ["tiny.unknown"] = "x", ["empty.unknown"] = "", ["unicode.unknown"] = "日本語のテキスト\n"
        };
        foreach (var (path, text) in files)
            await File.WriteAllTextAsync(Path.Combine(m_root, path), text);
        await File.WriteAllTextAsync(Path.Combine(m_root, "utf16.unknown"), "Unicode text\n", new UnicodeEncoding(false, true));
        await File.WriteAllBytesAsync(Path.Combine(m_root, "binary.dat"), new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(Path.Combine(m_root, "disguised.txt"), new byte[] { 0, 1, 2, 3 });
        await File.WriteAllTextAsync(Path.Combine(m_root, ".gitignore"), "ignored.data\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "ignored.data"), "ignored\n");

        var set = await new DiffLensFileLoader().LoadReviewSetAsync(m_root, "main", (CommandLineReviewMode)modeValue);

        foreach (var (path, text) in files)
        {
            var entry = set.Entries.Single(e => e.Path == path);
            Assert.That(entry.Error, Is.Null, path);
            Assert.That(entry.File.WorkingText, Is.EqualTo(text), path);
        }
        Assert.That(set.Entries.Single(e => e.Path == "utf16.unknown").File.WorkingText, Is.EqualTo("Unicode text\n"));
        foreach (var path in new[] { "binary.dat", "disguised.txt" })
        {
            var entry = set.Entries.Single(e => e.Path == path);
            Assert.That(entry.File, Is.Null);
            Assert.That(entry.Error, Does.Contain("Binary"));
        }
        Assert.That(set.Entries.Select(e => e.Path), Does.Not.Contain("ignored.data"));
    }

    [Test]
    public async Task ReviewCanvasPreservesDeletedAndRenamedTextAndDeletedBinaryAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "old.md"), "rename me\n");
        await File.WriteAllTextAsync(Path.Combine(m_root, "deleted.txt"), "removed text\n");
        await File.WriteAllBytesAsync(Path.Combine(m_root, "deleted.bin"), new byte[] { 1, 2, 3 });
        await Git("add", ".");
        await Git("commit", "-m", "Baseline documents");
        await Git("mv", "old.md", "new.md");
        File.Delete(Path.Combine(m_root, "deleted.txt"));
        File.Delete(Path.Combine(m_root, "deleted.bin"));
        var set = await new DiffLensFileLoader().LoadReviewSetAsync(m_root, "HEAD", CommandLineReviewMode.Uncommitted);
        var renamed = set.Entries.Single(e => e.Path == "new.md").File;
        Assert.That(renamed.Baseline, Is.EqualTo("rename me\n"));
        Assert.That(set.Entries.Single(e => e.Path == "deleted.txt").File.CanEdit, Is.False);
        Assert.That(set.Entries.Single(e => e.Path == "deleted.bin").Error, Does.Contain("Binary"));
    }

    [Test]
    public async Task GivenBranchAndLocalEditsUsesMergeBaseAndCurrentDisk()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "committed feature\n");
        await Git("commit", "-am", "Feature");
        await Git("switch", "main");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "later main\n");
        await Git("commit", "-am", "Advance main");
        await Git("switch", "feature/test");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "staged\n");
        await Git("add", ".");
        await File.WriteAllTextAsync(Path.Combine(m_root, "sample.cs"), "current disk\n");
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "sample.cs");
        Assert.Multiple(() =>
        {
            Assert.That(file.Baseline, Does.Contain("Value = 1"));
            Assert.That(file.WorkingText, Is.EqualTo("current disk\n"));
        });
    }

    [Test]
    public async Task GivenRenameLoadsOriginalBaselinePath()
    {
        await Git("mv", "sample.cs", "renamed file.cs");
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "renamed file.cs");
        Assert.That(file.Baseline, Is.EqualTo(file.WorkingText));
    }

    [Test]
    public async Task GivenDeletedFileRetainsBaselineAndDisablesEditing()
    {
        File.Delete(Path.Combine(m_root, "sample.cs"));
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "sample.cs");
        Assert.Multiple(() =>
        {
            Assert.That(file.Baseline, Does.Contain("Value = 1"));
            Assert.That(file.WorkingText, Is.Empty);
            Assert.That(file.CanEdit, Is.False);
        });
    }

    [Test]
    public async Task GivenUntrackedFileUsesEmptyBaseline()
    {
        await File.WriteAllTextAsync(Path.Combine(m_root, "new.cs"), "new file\n");
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "new.cs");
        Assert.That(file.Baseline, Is.Empty);
        Assert.That(file.WorkingText, Is.EqualTo("new file\n"));
    }

    [Test]
    public void GivenOutsidePathRefusesToOpenIt() =>
        Assert.ThrowsAsync<InvalidOperationException>(async () => await new DiffLensFileLoader().LoadAsync(m_root, "main", "../outside.cs"));

    [Test]
    public async Task GivenExternalEditSaveRefusesAndRetainsExternalContent()
    {
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "sample.cs");
        await File.WriteAllTextAsync(file.FullPath, "agent edit");
        Assert.ThrowsAsync<IOException>(async () => await file.SaveAsync("user edit"));
        Assert.That(await File.ReadAllTextAsync(file.FullPath), Is.EqualTo("agent edit"));
    }

    [Test]
    public async Task GivenUtf16FileSavePreservesEncodingAndCrLfWithoutStaging()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var path = Path.Combine(m_root, "new.cs");
        await File.WriteAllTextAsync(path, "old\r\n", encoding);
        var file = await new DiffLensFileLoader().LoadAsync(m_root, "main", "new.cs");
        await file.SaveAsync("// TODO: test\r\nold\r\n");
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("// TODO: test\r\nold\r\n")).ToArray()));
        var staged = await m_git.RunAsync(m_root, "diff", "--cached", "--name-only");
        Assert.That(staged.StandardOutput, Is.Empty);
    }

    [Test]
    public void GivenWhitespaceChangeItIsNotHidden()
    {
        var rows = DiffLensRows.Build("value = 1;", "value  = 1;", 0, true);
        Assert.That(rows.Any(row => row.Changed), Is.True);
    }

    [Test]
    public void GivenContextExpansionSourceLineIdentityIsPreserved()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line {i}"));
        var newText = oldText.Replace("line 15", "changed line");
        var compact = DiffLensRows.Build(oldText, newText, 0, false);
        var expanded = DiffLensRows.Build(oldText, newText, 4, true);
        Assert.Multiple(() =>
        {
            Assert.That(compact.Any(row => row.NewLine == 15 && row.Right.Text == "changed line"), Is.True);
            Assert.That(expanded.Any(row => row.NewLine == 15 && row.OldLine == 15), Is.True);
            Assert.That(expanded.Count, Is.GreaterThan(compact.Count));
        });
    }

    [Test]
    public async Task TreeSnapshotUsesBatchedBaselinesForUnchangedModifiedRenamedAndNewFiles()
    {
        var encoding = new UnicodeEncoding(false, true, true);
        var unicodePath = Path.Combine(m_root, "a file.cs");
        await File.WriteAllTextAsync(unicodePath, "old Unicode value\r\n", encoding);
        await File.WriteAllTextAsync(Path.Combine(m_root, "empty.cs"), string.Empty);
        await Git("add", ".");
        await Git("commit", "-m", "Additional baseline files");
        await Git("mv", "sample.cs", "renamed.cs");
        await File.WriteAllTextAsync(unicodePath, "new Unicode value\r\n", encoding);
        await File.WriteAllTextAsync(Path.Combine(m_root, "new.cs"), "new file\n");

        var set = await new DiffLensFileLoader().LoadReviewSetAsync(m_root, "HEAD", CommandLineReviewMode.Tree);

        Assert.That(set.Entries.All(entry => entry.Error == null), Is.True);
        Assert.That(set.Entries.Single(entry => entry.Path == "renamed.cs").File.Baseline, Does.Contain("Value = 1"));
        Assert.That(set.Entries.Single(entry => entry.Path == "empty.cs").File.WorkingText, Is.Empty);
        Assert.That(set.Entries.Single(entry => entry.Path == "new.cs").File.Baseline, Is.Empty);
        var file = set.Entries.Single(entry => entry.Path == "a file.cs").File;
        Assert.That(file.Baseline, Is.EqualTo("old Unicode value\r\n"));
        Assert.That(file.WorkingText, Is.EqualTo("new Unicode value\r\n"));
        await file.SaveAsync("edited\r\n");
        Assert.That(await File.ReadAllBytesAsync(unicodePath), Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("edited\r\n")).ToArray()));
    }

    [Test]
    public async Task StagedBatchesPreserveRenamesUnicodeEmptyDeletionsAndBinaryCards()
    {
        var unicodePath = Path.Combine(m_root, "unicode.cs");
        var unicode = new UnicodeEncoding(false, true, true);
        await File.WriteAllTextAsync(unicodePath, "old Unicode line\r\n", unicode);
        await File.WriteAllTextAsync(Path.Combine(m_root, "deleted-empty.cs"), string.Empty);
        await Git("add", ".");
        await Git("commit", "-m", "Baseline edge cases");
        var renamed = OperatingSystem.IsWindows() ? "renamed with spaces.cs" : "renamed with spaces\nand a newline.cs";
        await Git("mv", "--", "sample.cs", renamed);
        await File.WriteAllTextAsync(unicodePath, "new Unicode line\r\n", unicode);
        await Git("rm", "--", "deleted-empty.cs");
        await File.WriteAllBytesAsync(Path.Combine(m_root, "new-binary.bin"), [0, 1, 255, 2]);
        await File.WriteAllTextAsync(Path.Combine(m_root, "added-empty.cs"), string.Empty);
        await Git("add", ".");
        await File.WriteAllTextAsync(unicodePath, "unstaged data that must not appear\n");
        var set = await new DiffLensFileLoader().LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        Assert.That(set.Entries.Single(e => e.Path == renamed).File.Baseline, Does.Contain("Value = 1"));
        Assert.That(set.Entries.Single(e => e.Path == "unicode.cs").File.WorkingText, Is.EqualTo("new Unicode line\r\n"));
        Assert.That(set.Entries.Single(e => e.Path == "unicode.cs").File.Baseline, Is.EqualTo("old Unicode line\r\n"));
        Assert.That(set.Entries.Single(e => e.Path == "added-empty.cs").File.WorkingText, Is.Empty);
        Assert.That(set.Entries.Single(e => e.Path == "new-binary.bin").Error, Does.Contain("Binary"));
        // Git can pair the two empty files as an exact rename. Unstage the addition
        // so the next comparison exercises a deleted empty blob independently.
        await Git("reset", "--quiet", "HEAD", "--", "added-empty.cs");
        var deletionSet = await new DiffLensFileLoader().LoadScopeAsync(m_root, "main", new DiffLensScope("staged", "Staged"));
        Assert.That(deletionSet.Entries.Single(e => e.Path == "deleted-empty.cs").File.IsReadOnly, Is.True);
        Assert.That(deletionSet.Entries.Single(e => e.Path == "deleted-empty.cs").Error, Is.Null);
    }

    [Test]
    public async Task GitBatchReaderPreservesBinaryAndEmptyBlobsAndSkipsAbsentPaths()
    {
        var binary = new byte[] { 0, 10, 13, 255, 1 };
        await File.WriteAllBytesAsync(Path.Combine(m_root, "binary blob.bin"), binary);
        await File.WriteAllTextAsync(Path.Combine(m_root, "empty.cs"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(m_root, "another empty.cs"), string.Empty);
        await Git("add", ".");
        await Git("commit", "-m", "Batch baseline blobs");

        var blobs = await GitBaselineBatchReader.ReadAsync(m_root, "HEAD",
            ["binary blob.bin", "empty.cs", "another empty.cs", "absent.cs"]);

        Assert.That(blobs.Keys, Is.EquivalentTo(new[] { "binary blob.bin", "empty.cs", "another empty.cs" }));
        Assert.That(blobs["binary blob.bin"], Is.EqualTo(binary));
        Assert.That(blobs["empty.cs"], Is.Empty);
        Assert.That(blobs["another empty.cs"], Is.Empty);
    }

    private async Task Git(params string[] args)
    {
        var result = await m_git.RunAsync(m_root, args);
        Assert.That(result.IsSuccess, Is.True, result.StandardError);
    }
}

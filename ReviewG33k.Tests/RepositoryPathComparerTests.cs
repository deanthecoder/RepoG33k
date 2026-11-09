using RepoG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class RepositoryPathComparerTests
{
    [Test]
    public void SavedAndDiscoveredPathsCollapseToOneEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "picker-repo");
        var paths = new[] { root, root + Path.DirectorySeparatorChar, Path.Combine(root, "."), root.Replace('\\', '/') };
        Assert.That(paths.Distinct(RepositoryPathComparer.Instance).Count(), Is.EqualTo(1));
        var statuses = new Dictionary<string, int>(RepositoryPathComparer.Instance) { [root] = 1 };
        Assert.That(statuses[paths[1]], Is.EqualTo(1));
    }

    [Test]
    public void CaseSensitivityMatchesPlatformAndNestedRepositoriesStayDistinct()
    {
        var root = Path.Combine(Path.GetTempPath(), "PickerRepo");
        Assert.That(RepositoryPathComparer.Instance.Equals(root, root.ToUpperInvariant()), Is.EqualTo(OperatingSystem.IsWindows()));
        Assert.That(RepositoryPathComparer.Instance.Equals(root, Path.Combine(root, "packages")), Is.False);
    }
}

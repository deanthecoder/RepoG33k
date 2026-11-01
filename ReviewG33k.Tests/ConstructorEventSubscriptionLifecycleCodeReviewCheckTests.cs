// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

public sealed class ConstructorEventSubscriptionLifecycleCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenConstructorSubscribesAndNoUnsubscribeOrDisposableReportsImportant()
    {
        const string source = """
            using System;

            public sealed class Publisher
            {
                public event EventHandler Changed;
            }

            public sealed class Sample
            {
                private readonly Publisher m_publisher;

                public Sample(Publisher publisher)
                {
                    m_publisher = publisher;
                    m_publisher.Changed += OnChanged;
                }

                private void OnChanged(object sender, EventArgs e)
                {
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 22));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("no unsubscribe and no `IDisposable`"));
    }

    [Test]
    public void AnalyzeWhenConstructorSubscribesButClassDoesNotImplementDisposableReportsImportant()
    {
        const string source = """
            using System;

            public sealed class Publisher
            {
                public event EventHandler Changed;
            }

            public sealed class Sample
            {
                private readonly Publisher m_publisher;

                public Sample(Publisher publisher)
                {
                    m_publisher = publisher;
                    m_publisher.Changed += OnChanged;
                }

                public void Detach()
                {
                    m_publisher.Changed -= OnChanged;
                }

                private void OnChanged(object sender, EventArgs e)
                {
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 26));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("no `IDisposable`"));
    }

    [Test]
    public void AnalyzeWhenConstructorSubscribesAndClassUnsubscribesInDisposeDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Publisher
            {
                public event EventHandler Changed;
            }

            public sealed class Sample : IDisposable
            {
                private readonly Publisher m_publisher;

                public Sample(Publisher publisher)
                {
                    m_publisher = publisher;
                    m_publisher.Changed += OnChanged;
                }

                public void Dispose()
                {
                    m_publisher.Changed -= OnChanged;
                }

                private void OnChanged(object sender, EventArgs e)
                {
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 27));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileIsCodeBehindDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Publisher
            {
                public event EventHandler Changed;
            }

            public sealed class Sample
            {
                private readonly Publisher m_publisher;

                public Sample(Publisher publisher)
                {
                    m_publisher = publisher;
                    m_publisher.Changed += OnChanged;
                }

                private void OnChanged(object sender, EventArgs e)
                {
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 22), path: "Views/MainWindow.axaml.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(
        string source,
        IEnumerable<int> addedLineNumbers,
        string status = "A",
        string path = "Packages/CSharp.Core/Sample.cs")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(addedLineNumbers ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new ConstructorEventSubscriptionLifecycleCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}

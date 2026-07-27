using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Guards NuGet/GitHub discoverability assets: package description/tags, README funnel,
/// and product-flow visuals that ship with PackageReadmeFile.
/// </summary>
public sealed class DiscoverabilityMetadataTests
{
    /// <summary>
    /// The newest released version documented in CHANGELOG.md — the first <c>## [x.y.z]</c> heading,
    /// skipping the <c>## [Unreleased]</c> section.
    /// </summary>
    private static string LatestReleasedChangelogVersion()
    {
        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot, "CHANGELOG.md"));
        var match = Regex.Match(changelog, @"^## \[(\d+\.\d+\.\d+)\]", RegexOptions.Multiline);
        Assert.True(match.Success, "CHANGELOG.md has no released version heading.");
        return match.Groups[1].Value;
    }

    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Analyzer_package_description_and_tags_include_high_intent_cancellation_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "CancelCop.Analyzer.Package",
                "CancelCop.Analyzer.Package.csproj"
            )
        );

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var readmeFile = Assert.Single(csproj.Descendants("PackageReadmeFile")).Value;
        var version = Assert.Single(csproj.Descendants("Version")).Value;

        Assert.Equal("README.md", readmeFile);

        // The package version is checked against the CHANGELOG rather than a hardcoded literal:
        // a literal asserts nothing about discoverability and fails on every release, which trains
        // maintainers to update the expectation reflexively. Tying it to the newest released
        // CHANGELOG heading pins the invariant that actually matters — a shipped package version is
        // always documented.
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.Equal(LatestReleasedChangelogVersion(), version);

        // The README ships inside the package, so its install snippets are the primary instructions
        // users follow. A stale version there sends everyone to the previous release.
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        Assert.Contains(
            $"<PackageReference Include=\"CancelCop.Analyzer\" Version=\"{version}\">",
            readme,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"Install-Package CancelCop.Analyzer -Version {version}",
            readme,
            StringComparison.Ordinal
        );
        Assert.Contains("CancellationToken", title, StringComparison.Ordinal);
        Assert.Contains("async", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Roslyn", title, StringComparison.OrdinalIgnoreCase);

        foreach (
            var term in new[]
            {
                "CancellationToken",
                "async/await",
                "Roslyn",
                "RequestAborted",
                "sync-over-async",
                "HttpClient",
                "EF Core",
                "Compile-time",
            }
        )
        {
            Assert.True(
                description.Contains(term, StringComparison.Ordinal),
                $"Analyzer Description must contain '{term}' for NuGet search discoverability."
            );
        }

        foreach (
            var tag in new[]
            {
                "cancellationtoken",
                "async-await",
                "sync-over-async",
                "deadlock",
                "roslyn-analyzer",
                "analyzers",
                "RequestAborted",
                "BackgroundService",
                "httpclient",
                "efcore",
                "aspnetcore",
                "codefix",
            }
        )
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Analyzer PackageTags must include '{tag}'."
            );
        }
    }

    [Fact]
    public void Readme_conversion_funnel_and_product_visuals_exist_with_resolvable_paths()
    {
        var readmePath = Path.Combine(RepositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        foreach (
            var section in new[]
            {
                "## The problem",
                "## What it catches",
                "## Install",
                "## See it work",
                "## 30-second path",
                "## Feature snapshot",
                "## Analyzer Rules",
            }
        )
        {
            Assert.Contains(section, readme, StringComparison.Ordinal);
        }

        Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);

        // Version-agnostic: the exact install version is asserted against the package version in
        // Analyzer_package_description_and_tags_include_high_intent_cancellation_terms. Repeating a
        // literal here only creates a second place to forget on every release.
        Assert.Matches(@"Version=""\d+\.\d+\.\d+""", readme);
        Assert.Contains("CC001", readme, StringComparison.Ordinal);
        Assert.Contains("CC029", readme, StringComparison.Ordinal);
        Assert.Contains("stays quiet", readme, StringComparison.OrdinalIgnoreCase);

        // NuGet.org requires absolute HTTPS image URLs in PackageReadmeFile content.
        const string rawBase =
            "https://raw.githubusercontent.com/georgepwall1991/CancelCop.Analyzer/main/";

        var visualAssets = new[]
        {
            "assets/flow-ide-diagnostics.svg",
            "assets/flow-before-after-fix.svg",
            "assets/flow-analyzer-ci-loop.svg",
        };

        foreach (var asset in visualAssets)
        {
            Assert.Contains(rawBase + asset, readme, StringComparison.Ordinal);
            var fullPath = Path.Combine(RepositoryRoot, asset);
            Assert.True(File.Exists(fullPath), $"Missing README visual: {asset}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Empty README visual: {asset}");
        }

        Assert.Contains(rawBase + "assets/cancelcop-icon.png", readme, StringComparison.Ordinal);

        var imageRefs = Regex
            .Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
            .Select(m => m.Groups[1].Value)
            .Concat(
                Regex.Matches(readme, @"<img[^>]+src=""([^""]+)""").Select(m => m.Groups[1].Value)
            )
            .Distinct(StringComparer.Ordinal);

        foreach (var imageRef in imageRefs)
        {
            Assert.True(
                imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"README image must use absolute HTTPS for NuGet rendering: {imageRef}"
            );
        }
    }

    [Fact]
    public void Package_project_packs_all_assets_for_nuget_readme_rendering()
    {
        var package = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "CancelCop.Analyzer.Package",
                "CancelCop.Analyzer.Package.csproj"
            )
        );

        Assert.Contains(
            package.Descendants("None"),
            n =>
                (n.Attribute("Include")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal
                )
                && string.Equals(
                    n.Attribute("Pack")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal
                )
        );
    }
}

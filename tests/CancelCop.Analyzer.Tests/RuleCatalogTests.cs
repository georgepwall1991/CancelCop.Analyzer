using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CancelCop.Analyzer.Tests;

/// <summary>
/// Trust drift guards: every descriptor a shipped analyzer registers must be documented in the
/// README rule table (with the correct severity and fix mark) and tracked in
/// AnalyzerReleases.Shipped.md. These tests fail when a rule is added, renamed, or re-severitied
/// without the public docs following.
/// </summary>
public class RuleCatalogTests
{
    private static readonly Assembly AnalyzerAssembly = typeof(MissingCancellationTokenAnalyzer).Assembly;
    private static readonly Assembly CodeFixAssembly = typeof(MissingCancellationTokenCodeFixProvider).Assembly;

    private static ImmutableArray<DiagnosticDescriptor> GetShippedDescriptors() =>
        AnalyzerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .SelectMany(a => a.SupportedDiagnostics)
            .ToImmutableArray();

    private static ImmutableHashSet<string> GetFixableDiagnosticIds() =>
        CodeFixAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
            .Select(t => (CodeFixProvider)Activator.CreateInstance(t)!)
            .SelectMany(p => p.FixableDiagnosticIds)
            .ToImmutableHashSet();

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CancelCop.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>README rule-table rows: | **CC001** | title | Warning | ✅ |</summary>
    private static Dictionary<string, (string Severity, string FixMark)> GetReadmeRuleRows()
    {
        var readme = File.ReadAllText(Path.Combine(GetRepoRoot(), "README.md"));
        var rows = new Dictionary<string, (string, string)>();

        foreach (Match match in Regex.Matches(
                     readme,
                     @"^\|\s*\*\*(CC\w+)\*\*\s*\|[^|]*\|\s*(\w+)\s*\|\s*(✅|❌)\s*\|",
                     RegexOptions.Multiline))
        {
            rows[match.Groups[1].Value] = (match.Groups[2].Value, match.Groups[3].Value);
        }

        return rows;
    }

    private static Dictionary<string, string> GetShippedReleaseRows()
    {
        var shipped = File.ReadAllText(Path.Combine(
            GetRepoRoot(), "src", "CancelCop.Analyzer", "AnalyzerReleases.Shipped.md"));
        var rows = new Dictionary<string, string>();

        foreach (Match match in Regex.Matches(
                     shipped,
                     @"^(CC\w+)\s*\|\s*\w+\s*\|\s*(\w+)\s*\|",
                     RegexOptions.Multiline))
        {
            rows[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return rows;
    }

    [Fact]
    public void ShippedAnalyzers_ExposeAtLeastTheNineKnownRules()
    {
        var ids = GetShippedDescriptors().Select(d => d.Id).ToImmutableHashSet();

        // Canary: if reflection-based discovery silently finds nothing, every other guard here
        // would vacuously pass.
        Assert.Superset(
            new HashSet<string> { "CC001", "CC002", "CC003", "CC004", "CC005A", "CC005B", "CC005C", "CC006", "CC009" },
            ids.ToHashSet());
    }

    [Fact]
    public void EveryShippedRule_HasAReadmeRuleTableRow_WithMatchingSeverity()
    {
        var rows = GetReadmeRuleRows();

        foreach (var descriptor in GetShippedDescriptors())
        {
            Assert.True(
                rows.ContainsKey(descriptor.Id),
                $"{descriptor.Id} is registered by an analyzer but has no row in the README rule table.");

            Assert.True(
                rows[descriptor.Id].Severity == descriptor.DefaultSeverity.ToString(),
                $"{descriptor.Id}: README severity '{rows[descriptor.Id].Severity}' does not match " +
                $"the shipped descriptor severity '{descriptor.DefaultSeverity}'.");
        }
    }

    [Fact]
    public void EveryShippedRule_IsTrackedInAnalyzerReleasesShipped_WithMatchingSeverity()
    {
        var rows = GetShippedReleaseRows();

        foreach (var descriptor in GetShippedDescriptors())
        {
            Assert.True(
                rows.ContainsKey(descriptor.Id),
                $"{descriptor.Id} is registered by an analyzer but is missing from AnalyzerReleases.Shipped.md.");

            Assert.True(
                rows[descriptor.Id] == descriptor.DefaultSeverity.ToString(),
                $"{descriptor.Id}: AnalyzerReleases.Shipped.md severity '{rows[descriptor.Id]}' does not " +
                $"match the shipped descriptor severity '{descriptor.DefaultSeverity}'.");
        }
    }

    [Fact]
    public void ReadmeFixColumn_MatchesExportedCodeFixProviders()
    {
        var fixableIds = GetFixableDiagnosticIds();
        var rows = GetReadmeRuleRows();

        foreach (var descriptor in GetShippedDescriptors())
        {
            if (!rows.TryGetValue(descriptor.Id, out var row))
                continue; // the README-presence test reports this case

            var expected = fixableIds.Contains(descriptor.Id) ? "✅" : "❌";
            Assert.True(
                row.FixMark == expected,
                $"{descriptor.Id}: README fix column shows '{row.FixMark}' but the code-fix " +
                $"assembly {(expected == "✅" ? "exports" : "does not export")} a provider for it.");
        }
    }

    [Fact]
    public void EveryExportedCodeFixProvider_TargetsAShippedRule()
    {
        var shippedIds = GetShippedDescriptors().Select(d => d.Id).ToImmutableHashSet();

        foreach (var fixableId in GetFixableDiagnosticIds())
        {
            Assert.True(
                shippedIds.Contains(fixableId),
                $"A code-fix provider targets '{fixableId}', which no shipped analyzer registers.");
        }
    }

    [Fact]
    public void EveryShippedRule_HasAHelpLink()
    {
        foreach (var descriptor in GetShippedDescriptors())
        {
            Assert.False(
                string.IsNullOrEmpty(descriptor.HelpLinkUri),
                $"{descriptor.Id} has no helpLinkUri; IDEs will not show a 'learn more' link for it.");
        }
    }
}

#!/usr/bin/env bash
set -euo pipefail

package_dir="${1:-artifacts/packages}"
analyzer_version="$(dotnet msbuild src/CancelCop.Analyzer.Package/CancelCop.Analyzer.Package.csproj -getProperty:Version -nologo | tr -d '[:space:]')"

analyzer_package="$package_dir/CancelCop.Analyzer.$analyzer_version.nupkg"

test -f "$analyzer_package"

cmp src/CancelCop.Analyzer/bin/Release/netstandard2.0/CancelCop.Analyzer.dll \
  <(unzip -p "$analyzer_package" analyzers/dotnet/cs/CancelCop.Analyzer.dll)
cmp src/CancelCop.Analyzer.CodeFixes/bin/Release/netstandard2.0/CancelCop.Analyzer.CodeFixes.dll \
  <(unzip -p "$analyzer_package" analyzers/dotnet/cs/CancelCop.Analyzer.CodeFixes.dll)
cmp README.md <(unzip -p "$analyzer_package" README.md)

# Product-flow visuals referenced by PackageReadmeFile must ship inside the package.
for asset in \
  assets/cancelcop-icon.png \
  assets/flow-ide-diagnostics.svg \
  assets/flow-before-after-fix.svg \
  assets/flow-analyzer-ci-loop.svg
do
  cmp "$asset" <(unzip -p "$analyzer_package" "$asset")
done

# Discoverability metadata: high-intent CancellationToken / async terms (NuGet search).
analyzer_nuspec="$(unzip -p "$analyzer_package" CancelCop.Analyzer.nuspec)"

for term in CancellationToken async/await RequestAborted sync-over-async HttpClient roslyn-analyzer; do
  printf '%s' "$analyzer_nuspec" | grep -Fq "$term" || {
    echo "Analyzer nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

echo "Verified package payload, README, assets, and discoverability metadata for $analyzer_version."

; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC002 | Usage | Warning | CancellationToken must be propagated to async calls
CC003 | Usage | Warning | EF Core queries must pass CancellationToken
CC004 | Usage | Warning | HttpClient methods must pass CancellationToken
CC005A | Usage | Warning | MediatR handlers must accept CancellationToken parameter
CC005B | Usage | Warning | Controller action methods must accept CancellationToken parameter
CC005C | Usage | Warning | Minimal API endpoint handlers must accept CancellationToken parameter
CC006 | Style | Info | CancellationToken should be the last parameter (convention)

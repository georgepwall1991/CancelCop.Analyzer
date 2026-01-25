; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC007 | Usage | Warning | Avoid CancellationToken.None when a token is available
CC008 | Usage | Warning | CancellationToken parameter is not used
CC010 | Usage | Warning | Avoid async void methods
CC011 | Usage | Warning | Avoid blocking on async code (.Wait(), .Result)
CC012 | Usage | Disabled | ConfigureAwait should be used (for library code)

### New Code Fixes

Rule ID | Notes
--------|-------
CC006 | Move CancellationToken to last parameter position
CC007 | Replace CancellationToken.None with available token
CC010 | Change async void to async Task
CC012 | Add ConfigureAwait(false) or ConfigureAwait(true)

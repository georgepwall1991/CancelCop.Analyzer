## Release 1.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC009 | Usage | Warning | Loops should check for cancellation

## Release 1.1.0

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

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC001 | Usage | Warning | Public async methods must have CancellationToken parameter

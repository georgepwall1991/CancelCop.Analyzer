## Release 1.10.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC015 | Usage | Warning | Avoid blocking on async code (.Result/.Wait()/.GetAwaiter().GetResult())

## Release 1.9.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC014 | Usage | Warning | CancellationTokenSource should be disposed

## Release 1.8.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC013 | Usage | Warning | Avoid Thread.Sleep in async code; use await Task.Delay

## Release 1.7.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC012 | Usage | Info | Avoid passing CancellationToken.None/default when a token is in scope

## Release 1.6.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC011 | Usage | Warning | Async-iterator CancellationToken should be [EnumeratorCancellation]

## Release 1.5.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC010 | Usage | Warning | await foreach should flow a CancellationToken via .WithCancellation

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

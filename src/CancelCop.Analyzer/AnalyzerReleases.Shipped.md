## Release 1.50.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC047 | Usage | Warning | Avoid blocking DbCommand.ExecuteNonQuery in async code

## Release 1.49.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC046 | Usage | Warning | Avoid blocking DbCommand.ExecuteReader in async code

## Release 1.48.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC045 | Usage | Warning | Avoid blocking DbConnection.Open in async code

## Release 1.47.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC044 | Usage | Warning | Avoid blocking Dns.GetHostEntry in async code

## Release 1.46.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC043 | Usage | Warning | Avoid blocking Dns.GetHostAddresses in async code

## Release 1.45.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC042 | Usage | Warning | Avoid blocking NamedPipeClientStream.Connect in async code

## Release 1.44.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC041 | Usage | Warning | Avoid blocking NamedPipeServerStream.WaitForConnection in async code

## Release 1.43.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC040 | Usage | Warning | Avoid blocking HttpListener.GetContext in async code

## Release 1.42.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC039 | Usage | Warning | Avoid blocking UdpClient.Receive in async code

## Release 1.41.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC038 | Usage | Warning | Avoid blocking TcpListener accept in async code

## Release 1.40.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC037 | Usage | Warning | Avoid blocking TcpClient.Connect in async code

## Release 1.38.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC036 | Usage | Warning | Avoid blocking socket calls in async code

## Release 1.37.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC035 | Usage | Info | Cancellation is silently swallowed by an empty catch

## Release 1.36.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC034 | Usage | Warning | ParallelOptions should set CancellationToken

## Release 1.34.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC033 | Usage | Warning | CancellationTokenSource field is never disposed

## Release 1.33.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC032 | Usage | Warning | Async call is not awaited in non-async code

## Release 1.32.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC031 | Usage | Warning | Avoid blocking synchronization primitives in async code

## Release 1.30.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC030 | Usage | Warning | Avoid blocking Process.WaitForExit() in async code; use WaitForExitAsync

## Release 1.28.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC029 | Usage | Warning | Timeout CancellationTokenSource should link the in-scope token

## Release 1.24.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC028 | Usage | Warning | Avoid blocking System.IO.File calls in async code; use the async counterpart

## Release 1.23.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC027 | Usage | Warning | Returned task uses a disposed using resource

## Release 1.22.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC026 | Usage | Warning | Avoid SemaphoreSlim.Wait() in async code; use await WaitAsync()

## Release 1.21.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC025 | Usage | Info | Prefer await using for IAsyncDisposable

## Release 1.20.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC024 | Usage | Warning | Avoid async lambdas converted to void-returning delegates

## Release 1.19.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC023 | Usage | Warning | Avoid async void (non-event-handler)

## Release 1.18.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC022 | Usage | Info | Prefer await CancelAsync() over Cancel() in async code

## Release 1.16.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC021 | Usage | Info | Method should observe HttpContext.RequestAborted

## Release 1.15.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC020 | Usage | Warning | gRPC method should observe ServerCallContext.CancellationToken

## Release 1.14.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC019 | Usage | Info | Broad catch swallows OperationCanceledException

## Release 1.13.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC018 | Usage | Warning | SignalR hub methods should accept a CancellationToken

## Release 1.12.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC017 | Usage | Warning | BackgroundService.ExecuteAsync should observe its stopping token

## Release 1.11.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CC016 | Usage | Info | CancellationToken parameter is accepted but never used

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

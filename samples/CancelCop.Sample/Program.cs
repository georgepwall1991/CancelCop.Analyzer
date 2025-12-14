// =============================================================================
// CancelCop.Sample - Demonstration of CancelCop Analyzer Rules
// =============================================================================
//
// This project demonstrates all CancelCop analyzer rules with examples of
// both violations and correct patterns. Build this project to see the
// analyzer warnings in action.
//
// SAMPLE FILES:
// -------------
// CC001_PublicAsyncMethods.cs  - Public async methods must have CancellationToken
// CC002_TokenPropagation.cs    - CancellationToken must be propagated to async calls
// CC003_EFCoreMethods.cs       - EF Core queries must pass CancellationToken
// CC004_HttpClientMethods.cs   - HttpClient methods must pass CancellationToken
// CC005_HandlerPatterns.cs     - MediatR/Controller handlers need CancellationToken
// CC006_ParameterPosition.cs   - CancellationToken should be the last parameter
// CC009_LoopCancellation.cs    - Loops should check for cancellation
//
// VIEWING WARNINGS:
// -----------------
// 1. Build the solution: dotnet build
// 2. Look for CC001, CC002, CC003, CC004, CC005, CC006, CC009 warnings
// 3. Each sample file contains detailed comments explaining why the rule exists
//
// =============================================================================

using CancelCop.Sample;

Console.WriteLine("===========================================");
Console.WriteLine("  CancelCop Analyzer Sample Project");
Console.WriteLine("===========================================");
Console.WriteLine();
Console.WriteLine("This project demonstrates CancelCop analyzer rules.");
Console.WriteLine("Build this project to see analyzer warnings in action.");
Console.WriteLine();
Console.WriteLine("Rules demonstrated:");
Console.WriteLine("  CC001 - Public async methods must have CancellationToken");
Console.WriteLine("  CC002 - CancellationToken must be propagated");
Console.WriteLine("  CC003 - EF Core methods must pass CancellationToken");
Console.WriteLine("  CC004 - HttpClient methods must pass CancellationToken");
Console.WriteLine("  CC005 - Handler patterns must accept CancellationToken");
Console.WriteLine("  CC006 - CancellationToken should be last parameter");
Console.WriteLine("  CC009 - Loops should check for cancellation");
Console.WriteLine();

// Demonstrate cancellation in action
using var cts = new CancellationTokenSource();
var token = cts.Token;

Console.WriteLine("Running sample operations...");
Console.WriteLine();

// CC001 - Good pattern: public method with token
var cc001 = new CC001_PublicAsyncMethods();
await cc001.FetchDataAsync(token);

// CC002 - Good pattern: token propagation
var cc002 = new CC002_TokenPropagation();
await cc002.DelayWithPropagationAsync(token);

// CC003 - Good pattern: EF Core with token
var cc003 = new CC003_EFCoreMethods();
_ = await cc003.GetAllProductsCorrectAsync(token);

// CC009 - Good pattern: loop with cancellation check
var cc009 = new CC009_LoopCancellation();
await cc009.ProcessItemsCorrectAsync(new List<string> { "item1", "item2" }, token);

Console.WriteLine();
Console.WriteLine("Sample operations completed successfully!");
Console.WriteLine();
Console.WriteLine("-------------------------------------------");
Console.WriteLine("Check the build output for analyzer warnings");
Console.WriteLine("on the VIOLATION examples in each sample file.");
Console.WriteLine("-------------------------------------------");

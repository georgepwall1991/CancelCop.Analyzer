// =============================================================================
// CC005A/B/C: Handler patterns must accept CancellationToken parameter
// =============================================================================
//
// WHY THIS MATTERS:
// Request handlers in web applications and CQRS patterns are entry points that
// should support cancellation:
//
// CC005A - MediatR Handlers:
//   - IRequestHandler<TRequest, TResponse> implementations
//   - Long-running business logic should be cancellable
//   - Allows graceful handling of client disconnects
//
// CC005B - ASP.NET Core Controller Actions:
//   - Controller methods with [HttpGet], [HttpPost], etc.
//   - When a user closes their browser, the request should cancel
//   - Prevents wasted server resources on abandoned requests
//
// CC005C - Minimal API Endpoints:
//   - MapGet, MapPost, MapPut, MapDelete, MapPatch handlers
//   - Lambda-based endpoints need cancellation support too
//
// THE RULE:
// - Handler methods should accept CancellationToken as a parameter
// - ASP.NET Core automatically passes the request's cancellation token
// - MediatR passes the token through the pipeline
//
// NOTE: This sample demonstrates the patterns. The actual diagnostics require
// ASP.NET Core or MediatR references to be detected.
//
// =============================================================================

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC005A/B/C: Handler patterns must accept CancellationToken.
/// </summary>
public static class CC005_HandlerPatterns
{
    // =========================================================================
    // CC005A: MediatR Handler Pattern
    // =========================================================================
    //
    // MediatR is a popular CQRS library. Handlers process commands and queries.
    //
    // VIOLATION:
    // ```csharp
    // public class GetUserHandler : IRequestHandler<GetUserQuery, User>
    // {
    //     // CC005A WARNING: Missing CancellationToken parameter
    //     public async Task<User> Handle(GetUserQuery request)
    //     {
    //         return await _repository.GetUserAsync(request.UserId);
    //     }
    // }
    // ```
    //
    // CORRECT:
    // ```csharp
    // public class GetUserHandler : IRequestHandler<GetUserQuery, User>
    // {
    //     public async Task<User> Handle(
    //         GetUserQuery request,
    //         CancellationToken cancellationToken)  // Added parameter
    //     {
    //         return await _repository.GetUserAsync(request.UserId, cancellationToken);
    //     }
    // }
    // ```

    // =========================================================================
    // CC005B: ASP.NET Core Controller Action Pattern
    // =========================================================================
    //
    // Controller actions are HTTP endpoints. ASP.NET Core can pass the request's
    // CancellationToken which triggers when the client disconnects.
    //
    // VIOLATION:
    // ```csharp
    // [ApiController]
    // [Route("api/[controller]")]
    // public class UsersController : ControllerBase
    // {
    //     [HttpGet("{id}")]
    //     // CC005B WARNING: Missing CancellationToken parameter
    //     public async Task<IActionResult> GetUser(int id)
    //     {
    //         var user = await _service.GetUserAsync(id);
    //         return Ok(user);
    //     }
    // }
    // ```
    //
    // CORRECT:
    // ```csharp
    // [ApiController]
    // [Route("api/[controller]")]
    // public class UsersController : ControllerBase
    // {
    //     [HttpGet("{id}")]
    //     public async Task<IActionResult> GetUser(
    //         int id,
    //         CancellationToken cancellationToken)  // ASP.NET Core injects this
    //     {
    //         var user = await _service.GetUserAsync(id, cancellationToken);
    //         return Ok(user);
    //     }
    // }
    // ```

    // =========================================================================
    // CC005C: Minimal API Endpoint Pattern
    // =========================================================================
    //
    // Minimal APIs use lambda expressions for endpoint handlers.
    //
    // VIOLATION:
    // ```csharp
    // app.MapGet("/users/{id}", async (int id, IUserService service) =>
    // {
    //     // CC005C WARNING: Lambda missing CancellationToken parameter
    //     var user = await service.GetUserAsync(id);
    //     return Results.Ok(user);
    // });
    // ```
    //
    // CORRECT:
    // ```csharp
    // app.MapGet("/users/{id}", async (
    //     int id,
    //     IUserService service,
    //     CancellationToken cancellationToken) =>  // Added parameter
    // {
    //     var user = await service.GetUserAsync(id, cancellationToken);
    //     return Results.Ok(user);
    // });
    // ```

    // =========================================================================
    // WHY CLIENT DISCONNECT MATTERS
    // =========================================================================
    //
    // When a user closes their browser or navigates away:
    // 1. The HTTP connection is terminated
    // 2. ASP.NET Core triggers the CancellationToken
    // 3. Your code can stop processing immediately
    //
    // Without cancellation support:
    // - Database queries continue to completion
    // - External API calls keep running
    // - Business logic processes to the end
    // - Results are computed but never sent
    // - Server resources wasted on dead requests
    //
    // With cancellation support:
    // - OperationCanceledException is thrown
    // - Resources are freed immediately
    // - Database connections released
    // - Thread pool threads returned
    // - Server can handle more concurrent requests

    /// <summary>
    /// Example demonstrating how cancellation flows through a handler.
    /// </summary>
    public static async Task DemonstrateHandlerCancellation(CancellationToken cancellationToken)
    {
        // Simulate a handler processing a request
        Console.WriteLine("Handler started processing...");

        try
        {
            // This simulates database or API calls
            await Task.Delay(5000, cancellationToken);
            Console.WriteLine("Handler completed successfully");
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - clean up and exit gracefully
            Console.WriteLine("Handler cancelled - client disconnected");
            throw; // Re-throw so ASP.NET Core knows the request was cancelled
        }
    }
}

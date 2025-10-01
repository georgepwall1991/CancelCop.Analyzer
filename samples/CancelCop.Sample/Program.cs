// CancelCop Sample - Demonstrates the analyzer detecting missing CancellationToken parameters

var service = new DataService();

// This will work fine
await service.FetchWithTokenAsync(CancellationToken.None);

// But this will trigger CC001 diagnostic (uncomment to see)
// await service.FetchWithoutTokenAsync();

Console.WriteLine("CancelCop Sample Complete");

public class DataService
{
    // Good: Has CancellationToken and propagates it
    public async Task FetchWithTokenAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine("Fetched with token");
    }

    // Bad: Missing CancellationToken - CC001 diagnostic
    public async Task FetchWithoutTokenAsync()
    {
        await Task.Delay(100);
        Console.WriteLine("Fetched without token");
    }

    // Bad: Has token but doesn't propagate - CC002 diagnostic
    public async Task ProcessWithoutPropagationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(50);  // Should pass cancellationToken
        await HelperAsync();    // Should pass cancellationToken
    }

    // Good: Protected method also needs token
    protected async Task ProcessDataAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
    }

    // Private methods are OK without token
    private async Task InternalOperationAsync()
    {
        await Task.Delay(10);
    }

    private async Task HelperAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
    }
}

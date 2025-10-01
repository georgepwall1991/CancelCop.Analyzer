// CancelCop Sample - Demonstrates the analyzer detecting missing CancellationToken parameters

using System.Net.Http;
using Microsoft.EntityFrameworkCore;

var service = new DataService();
var repo = new UserRepository();
var apiClient = new ApiClient();

// This will work fine - methods have tokens and propagate them
await service.FetchWithTokenAsync(CancellationToken.None);
await repo.GetUserByIdAsync(1, CancellationToken.None);
await apiClient.GetUserDataAsync("user1", CancellationToken.None);

Console.WriteLine("CancelCop Sample Complete");

public class DataService
{
    // ✅ Good: Has CancellationToken and propagates it
    public async Task FetchWithTokenAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine("Fetched with token");
    }

    // ❌ CC001: Public async method missing CancellationToken parameter
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

// EF Core Examples
public class UserRepository
{
    private readonly AppDbContext _context = new AppDbContext();

    // Good: Passes CancellationToken to EF Core methods
    public async Task<User?> GetUserByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    // Bad: Missing CancellationToken propagation - CC003 diagnostic
    public async Task<List<User>> GetAllUsersAsync(CancellationToken cancellationToken)
    {
        return await _context.Users.ToListAsync();  // Should pass cancellationToken
    }

    // Bad: Missing CancellationToken propagation - CC003 diagnostic
    public async Task SaveUserAsync(User user, CancellationToken cancellationToken)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();  // Should pass cancellationToken
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("SampleDb");
    }
}

// HttpClient Examples
public class ApiClient
{
    private readonly HttpClient _httpClient = new HttpClient();

    // ✅ Good: Has CancellationToken and propagates it to HttpClient
    public async Task<string> GetUserDataAsync(string userId, CancellationToken cancellationToken)
    {
        return await _httpClient.GetStringAsync($"https://api.example.com/users/{userId}", cancellationToken);
    }

    // ❌ CC004: Missing CancellationToken propagation to HttpClient
    public async Task<string> FetchDataWithoutTokenAsync(CancellationToken cancellationToken)
    {
        return await _httpClient.GetStringAsync("https://api.example.com/data");  // Should pass cancellationToken
    }

    // ❌ CC004: Missing CancellationToken propagation to PostAsync
    public async Task<HttpResponseMessage> PostDataWithoutTokenAsync(StringContent content, CancellationToken cancellationToken)
    {
        return await _httpClient.PostAsync("https://api.example.com/data", content);  // Should pass cancellationToken
    }

    // ✅ Good: Proper token propagation with POST
    public async Task<HttpResponseMessage> PostDataAsync(StringContent content, CancellationToken cancellationToken)
    {
        return await _httpClient.PostAsync("https://api.example.com/data", content, cancellationToken);
    }
}

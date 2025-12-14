// =============================================================================
// CC003: EF Core queries must pass CancellationToken
// =============================================================================
//
// WHY THIS MATTERS:
// Entity Framework Core database operations can be slow, especially:
// - Complex queries with multiple joins
// - Large result sets
// - Network latency to database server
// - Database server under load
//
// Without cancellation support:
// - User clicks away but query keeps running
// - Server shutdown blocked by pending queries
// - Connection pool exhaustion from abandoned queries
// - Wasted database server resources
//
// THE RULE:
// - EF Core async methods (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync, etc.)
//   should receive a CancellationToken when one is available
// - This allows queries to be cancelled mid-execution
//
// =============================================================================

using Microsoft.EntityFrameworkCore;

namespace CancelCop.Sample;

/// <summary>
/// Demonstrates CC003: EF Core queries must pass CancellationToken.
/// </summary>
public class CC003_EFCoreMethods
{
    private readonly SampleDbContext _context = new();

    // -------------------------------------------------------------------------
    // VIOLATIONS (CC003 will warn on these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CC003 WARNING: ToListAsync should receive CancellationToken.
    /// A query returning thousands of records could take seconds.
    /// </summary>
    public async Task<List<Product>> GetAllProductsAsync(CancellationToken cancellationToken)
    {
        // BAD: No cancellation token passed
        return await _context.Products.ToListAsync();
    }

    /// <summary>
    /// CC003 WARNING: FirstOrDefaultAsync should receive CancellationToken.
    /// </summary>
    public async Task<Product?> GetProductByIdAsync(int id, CancellationToken cancellationToken)
    {
        // BAD: Query could be slow if table is large and not indexed
        return await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>
    /// CC003 WARNING: SaveChangesAsync should receive CancellationToken.
    /// Database writes can be slow, especially with triggers or constraints.
    /// </summary>
    public async Task SaveProductAsync(Product product, CancellationToken cancellationToken)
    {
        _context.Products.Add(product);
        // BAD: Save could be blocked by locks or slow triggers
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// CC003 WARNING: AnyAsync should receive CancellationToken.
    /// Even simple existence checks can be slow on large tables.
    /// </summary>
    public async Task<bool> ProductExistsAsync(int id, CancellationToken cancellationToken)
    {
        // BAD: Full table scan if not indexed
        return await _context.Products.AnyAsync(p => p.Id == id);
    }

    /// <summary>
    /// CC003 WARNING: CountAsync should receive CancellationToken.
    /// </summary>
    public async Task<int> GetProductCountAsync(CancellationToken cancellationToken)
    {
        // BAD: COUNT(*) can be slow on large tables
        return await _context.Products.CountAsync();
    }

    // -------------------------------------------------------------------------
    // CORRECT PATTERNS (No warnings)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CORRECT: ToListAsync with CancellationToken.
    /// Query will be cancelled if token is triggered.
    /// </summary>
    public async Task<List<Product>> GetAllProductsCorrectAsync(CancellationToken cancellationToken)
    {
        return await _context.Products.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// CORRECT: FirstOrDefaultAsync with CancellationToken.
    /// </summary>
    public async Task<Product?> GetProductByIdCorrectAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <summary>
    /// CORRECT: SaveChangesAsync with CancellationToken.
    /// </summary>
    public async Task SaveProductCorrectAsync(Product product, CancellationToken cancellationToken)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// CORRECT: AnyAsync with CancellationToken.
    /// </summary>
    public async Task<bool> ProductExistsCorrectAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.Products.AnyAsync(p => p.Id == id, cancellationToken);
    }

    /// <summary>
    /// CORRECT: Complex query with cancellation support.
    /// This query could take significant time - cancellation is essential.
    /// </summary>
    public async Task<List<Product>> GetExpensiveProductsAsync(
        decimal minPrice,
        CancellationToken cancellationToken)
    {
        return await _context.Products
            .Where(p => p.Price > minPrice)
            .OrderByDescending(p => p.Price)
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}

// -------------------------------------------------------------------------
// SUPPORTING TYPES
// -------------------------------------------------------------------------

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class SampleDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("SampleDb");
    }
}

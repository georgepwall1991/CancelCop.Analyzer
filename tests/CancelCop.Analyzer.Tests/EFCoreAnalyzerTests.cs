using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EFCoreAnalyzerTests
{
    private static CSharpAnalyzerTest<EFCoreAnalyzer, DefaultVerifier> CreateTest(string testCode, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<EFCoreAnalyzer, DefaultVerifier>
        {
            TestCode = testCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.EntityFrameworkCore", "9.0.0"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        DbSet<User> users = null;
        return await users.{|#0:FirstOrDefaultAsync|}(u => u.Id == id);
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("FirstOrDefaultAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        DbSet<User> users = null;
        return await users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task ToListAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.{|#0:ToListAsync|}();
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ToListAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task ToListAsync_WithToken_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.ToListAsync(cancellationToken);
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task SingleOrDefaultAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.{|#0:SingleOrDefaultAsync|}(u => u.Id == id);
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SingleOrDefaultAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task AnyAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.{|#0:AnyAsync|}();
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("AnyAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task CountAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<int> GetUserCountAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.{|#0:CountAsync|}();
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CountAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutToken_WhenTokenAvailable_ShouldReportDiagnostic()
    {
        var test = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task SaveUserAsync(DbContext context, CancellationToken cancellationToken)
    {
        await context.{|#0:SaveChangesAsync|}();
    }
}";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SaveChangesAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task EFCoreMethod_NoTokenParameter_ShouldNotReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id)
    {
        IQueryable<User> query = null;
        return await query.FirstOrDefaultAsync(u => u.Id == id);
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(test).RunAsync();
    }
}

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
    public async Task EFCoreMethod_InLocalFunctionWithOwnToken_ShouldReportDiagnostic()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public void Setup()
    {
        async Task<int> CountUsersAsync(CancellationToken ct)
        {
            IQueryable<User> query = null;
            return await query.{|#0:CountAsync|}();
        }
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CountAsync", "ct");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task EFCoreMethod_InLambdaWithOwnToken_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public void Setup()
    {
        Func<CancellationToken, Task<int>> count = async ct =>
        {
            IQueryable<User> query = null;
            return await query.{|#0:CountAsync|}();
        };
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CountAsync", "ct");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task EFCoreMethod_InLambdaCapturingOuterToken_ShouldReportDiagnostic()
    {
        var test = @"
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public void Setup(CancellationToken cancellationToken)
    {
        Func<Task<int>> count = async () =>
        {
            IQueryable<User> query = null;
            return await query.{|#0:CountAsync|}();
        };
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("CountAsync", "cancellationToken");

        await CreateTest(test, expected).RunAsync();
    }

    [Fact]
    public async Task EFCoreMethod_InsideExpressionTree_ShouldNotReportDiagnostic()
    {
        // Real EF Core async methods take an optional CancellationToken, which an expression tree
        // may not call (CS0854) — so the stub overload below has no optional parameters, keeping
        // the tree compilable while still resolving into the Microsoft.EntityFrameworkCore namespace.
        var test = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public static class StubQueryableExtensions
    {
        public static Task<int> CountAsync<T>(this IQueryable<T> source) => null;
        public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken) => null;
    }
}

public class TestClass
{
    public void Setup(IQueryable<User> query, CancellationToken cancellationToken)
    {
        Expression<Func<Task<int>>> count = () => query.CountAsync();
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(test).RunAsync();
    }

    [Fact]
    public async Task EFCoreMethod_InLambda_NoTokenAnywhere_ShouldNotReportDiagnostic()
    {
        var test = @"
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public void Setup()
    {
        Func<Task<int>> count = async () =>
        {
            IQueryable<User> query = null;
            return await query.CountAsync();
        };
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(test).RunAsync();
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

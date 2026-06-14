using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CancelCop.Analyzer.Tests;

public class EFCoreCodeFixTests
{
    private static CSharpCodeFixTest<EFCoreAnalyzer, EFCoreCodeFixProvider, DefaultVerifier> CreateTest(
        string testCode,
        string fixedCode,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<EFCoreAnalyzer, EFCoreCodeFixProvider, DefaultVerifier>
        {
            TestCode = testCode,
            FixedCode = fixedCode,
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net90
                .AddPackages(ImmutableArray.Create(new PackageIdentity("Microsoft.EntityFrameworkCore", "9.0.0"))),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    [Fact]
    public async Task FixAll_TwoQueries_BothGetTokenArgument()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<int> CountUsersAsync(CancellationToken cancellationToken)
    {
        DbSet<User> users = null;
        var a = await users.{|#0:CountAsync|}();
        var b = await users.{|#1:CountAsync|}();
        return a + b;
    }
}

public class User { public int Id { get; set; } }";

        var fixedCode = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<int> CountUsersAsync(CancellationToken cancellationToken)
    {
        DbSet<User> users = null;
        var a = await users.CountAsync(cancellationToken);
        var b = await users.CountAsync(cancellationToken);
        return a + b;
    }
}

public class User { public int Id { get; set; } }";

        await CreateTest(
            test,
            fixedCode,
            new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(0).WithArguments("CountAsync", "cancellationToken"),
            new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithLocation(1).WithArguments("CountAsync", "cancellationToken")).RunAsync();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithoutToken_AddsTokenParameter()
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

        var fixedCode = @"
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

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("FirstOrDefaultAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task ToListAsync_WithoutToken_AddsTokenParameter()
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

        var fixedCode = @"
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

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ToListAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutToken_AddsTokenParameter()
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

        var fixedCode = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task SaveUserAsync(DbContext context, CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SaveChangesAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task AnyAsync_WithExistingArguments_AddsTokenAsLastArgument()
    {
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<bool> HasActiveUsersAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.{|#0:AnyAsync|}(u => u.IsActive);
    }
}

public class User
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
}";

        var fixedCode = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<bool> HasActiveUsersAsync(CancellationToken cancellationToken)
    {
        IQueryable<User> query = null;
        return await query.AnyAsync(u => u.IsActive, cancellationToken);
    }
}

public class User
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
}";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("AnyAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithNamedPredicateArgument_AddsNamedTokenArgument()
    {
        // The call already uses a named argument, so the appended token must be named as well
        // (a trailing positional argument would be CS8323 when the named one is out of position;
        // staying named is always legal).
        var test = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        IQueryable<User> users = null;
        return await users.{|#0:FirstOrDefaultAsync|}(predicate: u => u.Id == id);
    }
}

public class User { public int Id { get; set; } }";

        var fixedCode = @"
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TestClass
{
    public async Task<User> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        IQueryable<User> users = null;
        return await users.FirstOrDefaultAsync(predicate: u => u.Id == id, cancellationToken: cancellationToken);
    }
}

public class User { public int Id { get; set; } }";

        var expected = new DiagnosticResult("CC003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("FirstOrDefaultAsync", "cancellationToken");

        await CreateTest(test, fixedCode, expected).RunAsync();
    }
}

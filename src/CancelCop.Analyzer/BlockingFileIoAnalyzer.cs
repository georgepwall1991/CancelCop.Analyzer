using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CancelCop.Analyzer;

/// <summary>
/// Analyzer that detects a blocking synchronous <c>System.IO</c> call (<c>File</c> read/write/append
/// helpers, <c>StreamReader.ReadToEnd</c>/<c>ReadLine</c>, <c>StreamWriter.Write</c>/<c>WriteLine</c>/
/// <c>Flush</c>, or the <c>Stream</c> primitives <c>Read</c>/<c>Write</c>/<c>CopyTo</c>/<c>Flush</c>)
/// inside async code when a signature-compatible async counterpart (<c>&lt;name&gt;Async</c>) exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule ID:</b> CC028
/// </para>
/// <para>
/// <b>Why this matters:</b>
/// Synchronous <c>File</c> helpers such as <c>File.ReadAllText</c> block the calling thread for the
/// whole disk operation. Inside an <c>async</c> method that ties up a thread-pool thread and defeats
/// the point of being async. .NET exposes <c>ReadAllTextAsync</c> / <c>WriteAllTextAsync</c> / … which
/// take a <c>CancellationToken</c>, so the work can both yield the thread and be cancelled. This rounds
/// out the blocking-in-async family alongside CC013 (<c>Thread.Sleep</c>), CC015
/// (<c>Task.Wait</c>/<c>.Result</c>) and CC026 (<c>SemaphoreSlim.Wait</c>).
/// </para>
/// <para>
/// <b>What it detects:</b> a call to one of the well-known blocking <c>System.IO</c> methods
/// (<c>File</c> read/write/append helpers, <c>StreamReader.ReadToEnd</c>/<c>ReadLine</c>, or
/// <c>StreamWriter.Write</c>/<c>WriteLine</c>/<c>Flush</c>) that has a signature-compatible
/// <c>&lt;name&gt;Async</c> counterpart, made inside an <c>async</c> method, local function, lambda,
/// or anonymous method. Qualified calls and bare <c>File</c> calls imported with
/// <c>using static</c> are supported. Overloads without an async form (e.g.
/// <c>StreamWriter.Write(bool)</c>) are not flagged, so the rewrite always compiles.
/// </para>
/// <para>
/// The <c>Stream</c> primitives (<c>Read</c>, <c>Write</c>, <c>CopyTo</c>, <c>Flush</c>) are matched
/// by inheritance rather than by exact type name, so concrete framework streams
/// (<c>FileStream</c>, <c>NetworkStream</c>, <c>GZipStream</c>) and user-defined subclasses declared
/// outside <c>System.IO</c> are all covered. <c>MemoryStream</c> is excluded: it is backed by an
/// in-memory buffer, so the call never leaves the CPU and the async counterpart only wraps the same
/// synchronous work in a completed task.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task RunAsync(string path)
/// {
///     var text = File.ReadAllText(path);   // CC028 -> await File.ReadAllTextAsync(path)
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingFileIoAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer rule.
    /// </summary>
    public const string DiagnosticId = "CC028";

    /// <summary>
    /// Property key used to pass the in-scope token parameter name (if any) to the code fix provider.
    /// </summary>
    public const string TokenNameProperty = "TokenName";

    /// <summary>
    /// Property key set when the diagnostic is correct but no safe rewrite exists, so the code fix
    /// must not offer one. Used for calls whose named arguments do not line up with the async
    /// counterpart's parameter names.
    /// </summary>
    public const string NoFixProperty = "NoFix";

    /// <summary>
    /// The blocking <c>System.IO</c> methods (keyed by declaring type) that have a documented async
    /// counterpart of the form <c>&lt;name&gt;Async</c>.
    /// </summary>
    private static readonly ImmutableDictionary<
        string,
        ImmutableHashSet<string>
    > BlockingMethodsByType = ImmutableDictionary.CreateRange(
        new[]
        {
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "File",
                ImmutableHashSet.Create(
                    "ReadAllText",
                    "ReadAllBytes",
                    "ReadAllLines",
                    "WriteAllText",
                    "WriteAllBytes",
                    "WriteAllLines",
                    "AppendAllText",
                    "AppendAllLines"
                )
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "StreamReader",
                ImmutableHashSet.Create("ReadToEnd", "ReadLine")
            ),
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "StreamWriter",
                ImmutableHashSet.Create("Write", "WriteLine", "Flush")
            ),
        }
    );

    /// <summary>
    /// The blocking primitives on <c>System.IO.Stream</c>. These are matched by inheritance rather
    /// than by exact type name because the concrete streams that matter (<c>FileStream</c>,
    /// <c>NetworkStream</c>, <c>GZipStream</c>, user-defined subclasses) each override them while
    /// inheriting the async counterparts from <c>Stream</c>.
    /// </summary>
    private static readonly ImmutableHashSet<string> StreamBlockingMethods =
        ImmutableHashSet.Create("Read", "Write", "CopyTo", "Flush");

    private static readonly LocalizableString Title = "Avoid blocking I/O in async code";
    private static readonly LocalizableString MessageFormat =
        "Blocking '{0}' in async code; use '{0}Async'";
    private static readonly LocalizableString Description =
        "Synchronous System.IO calls block the thread in async code; use the async counterpart, which also accepts a CancellationToken.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: DiagnosticHelp.LinkUri
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            _ => null,
        };
        if (invokedName is null)
            return;

        var methodName = invokedName.Identifier.Text;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method
        )
            return;

        var containingType = method.ContainingType;
        if (containingType is null)
            return;

        if (
            !IsCuratedBlockingCall(containingType, methodName)
            && !IsBlockingStreamCall(context, invocation, containingType, methodName)
        )
            return;

        // Only flag when the framework in use actually offers a signature-compatible async counterpart,
        // so the suggested fix always compiles. A counterpart matches when its parameters equal the
        // blocking call's parameters, optionally followed by a single trailing CancellationToken. The
        // overloads vary by type and target framework (e.g. StreamWriter.Write(bool) has no async form),
        // so this signature check — not a name-only lookup — is what keeps the rewrite valid.
        if (
            !HasAsyncCounterpart(
                containingType,
                method,
                methodName + "Async",
                out var asyncCounterpart,
                out var asyncTakesToken
            )
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation,
            context.SemanticModel
        );

        // Only ask the fixer to flow the token when the matched async overload actually accepts one;
        // adding a token argument to a tokenless overload (e.g. StreamWriter.WriteAsync(string)) would
        // not compile.
        var flowToken = asyncTakesToken && tokenParameter != null;

        // The counterpart search finds a signature-compatible member anywhere in the hierarchy, but
        // the rewritten call is resolved by ordinary overload resolution from the receiver's type
        // down — and a derived member can hide the one that was matched. A subclass declaring
        // `new int ReadAsync(byte[], int, int)` is not awaitable, so `await stream.ReadAsync(b, 0, n)`
        // binds to it and fails with CS1061. Verify against the argument count the fix will actually
        // emit; with no token in scope that is the tokenless arity, which is what makes the shadowing
        // member applicable in the first place.
        var emittedArity = invocation.ArgumentList.Arguments.Count + (flowToken ? 1 : 0);
        if (!AsyncCallBindsToAwaitable(containingType, methodName + "Async", emittedArity))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        // The fix copies the original argument list verbatim, so any named argument has to name a
        // parameter that exists at the same position on the *async* overload. A subclass that renames
        // its override's parameters breaks that: `stream.Read(data: b, start: 0, …)` is a valid call,
        // but the inherited Stream.ReadAsync names them `buffer`/`offset`, so the rewrite would fail
        // with CS1739. The call is still genuinely blocking, so the diagnostic stands — only the fix
        // is withheld.
        if (!NamedArgumentsMatch(invocation, asyncCounterpart!))
            properties = properties.Add(NoFixProperty, "named-argument-mismatch");
        if (flowToken)
            properties = properties.Add(TokenNameProperty, tokenParameter!.Name);

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invokedName.GetLocation(), properties, methodName)
        );
    }

    /// <summary>
    /// Returns <c>true</c> for the curated exact-type helpers: the <c>System.IO</c> types whose
    /// blocking members are listed by name (<c>File</c>, <c>StreamReader</c>, <c>StreamWriter</c>).
    /// </summary>
    private static bool IsCuratedBlockingCall(INamedTypeSymbol containingType, string methodName) =>
        containingType.ContainingNamespace?.ToDisplayString() == "System.IO"
        && BlockingMethodsByType.TryGetValue(containingType.Name, out var blockingMethods)
        && blockingMethods.Contains(methodName);

    /// <summary>
    /// Returns <c>true</c> for a blocking primitive on a <c>System.IO.Stream</c>. Matched by
    /// inheritance so that concrete and user-defined streams — which override the sync members and
    /// inherit the async ones — are covered, and so a subclass declared outside <c>System.IO</c> is
    /// still recognised.
    /// </summary>
    /// <remarks>
    /// <c>MemoryStream</c> and its subclasses are excluded: they are backed by an in-memory buffer,
    /// so the "blocking" call never leaves the CPU and the async counterpart only wraps the same
    /// synchronous work in an already-completed task. Flagging it would be noise, not a finding.
    /// The exclusion tests the receiver's own type rather than the declaring type, because
    /// <c>MemoryStream</c> does not override every member (e.g. <c>Flush</c> resolves to
    /// <c>Stream.Flush</c>).
    /// </remarks>
    private static bool IsBlockingStreamCall(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol containingType,
        string methodName
    )
    {
        if (!StreamBlockingMethods.Contains(methodName) || !IsStream(containingType))
            return false;

        var receiverType = GetReceiverType(context, invocation) ?? containingType;
        return !DerivesFrom(receiverType, "MemoryStream");
    }

    /// <summary>
    /// Resolves the static type of the invocation's receiver, so the <c>MemoryStream</c> exclusion
    /// sees the type the caller actually holds. Returns <c>null</c> for unqualified calls.
    /// </summary>
    private static ITypeSymbol? GetReceiverType(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var receiver = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            // Null-conditional (`stream?.Read(...)`): the receiver lives on the enclosing
            // conditional access, not on the member binding.
            MemberBindingExpressionSyntax => invocation
                .Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault()
                ?.Expression,
            _ => null,
        };

        return receiver is null
            ? null
            : context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
    }

    /// <summary>Returns <c>true</c> when <paramref name="type"/> is or derives from <c>System.IO.Stream</c>.</summary>
    private static bool IsStream(ITypeSymbol type) => DerivesFrom(type, "Stream");

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> or any of its base types is the
    /// <c>System.IO</c> type named <paramref name="name"/>. Namespace-gated so a same-named
    /// user type is never mistaken for the framework one.
    /// </summary>
    private static bool DerivesFrom(ITypeSymbol? type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (
                current.Name == name
                && current.ContainingNamespace?.ToDisplayString() == "System.IO"
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> declares an overload named
    /// <paramref name="asyncName"/> whose parameters match the blocking call's parameters, optionally
    /// followed by a single trailing <c>CancellationToken</c>. A token-taking overload is preferred;
    /// <paramref name="takesToken"/> reports whether the chosen match accepts the token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lookup walks base types: a concrete stream overrides the blocking member but inherits the
    /// async counterpart from <c>Stream</c>, so <c>FileStream.GetMembers("ReadAsync")</c> alone finds
    /// nothing and the rule would silently never fire.
    /// </para>
    /// <para>
    /// A candidate must be <c>public</c> and return an awaitable (<c>Task</c>/<c>ValueTask</c>). A
    /// matching signature is not enough: a type can declare a member named <c>ReadAsync</c> that
    /// returns <c>int</c>, and the suggested <c>await</c> would not compile against it. Since the
    /// derived member also shadows the framework one at the call site, no async alternative exists
    /// and the diagnostic's premise is false — so the rule stays quiet rather than reporting.
    /// </para>
    /// </remarks>
    private static bool HasAsyncCounterpart(
        INamedTypeSymbol type,
        IMethodSymbol sync,
        string asyncName,
        out IMethodSymbol? match,
        out bool takesToken
    )
    {
        takesToken = false;
        match = null;

        for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(asyncName).OfType<IMethodSymbol>())
            {
                if (
                    candidate.DeclaredAccessibility != Accessibility.Public
                    || !CancellationTokenHelpers.IsAsyncReturnType(candidate.ReturnType)
                )
                    continue;

                if (
                    !ParametersMatch(
                        sync.Parameters,
                        candidate.Parameters,
                        out var candidateTakesToken
                    )
                )
                    continue;

                match ??= candidate;
                if (candidateTakesToken)
                {
                    match = candidate;
                    takesToken = true;
                    return true;
                }
            }
        }

        return match != null;
    }

    /// <summary>
    /// Returns <c>true</c> when a call to <paramref name="asyncName"/> with
    /// <paramref name="arity"/> arguments, resolved from <paramref name="receiverType"/>, binds to a
    /// public awaitable method — i.e. the <c>await</c> the fix inserts will compile.
    /// </summary>
    /// <remarks>
    /// Overload resolution considers the most-derived type that declares an applicable member and
    /// does not fall back to base types once one is found, so a shadowing member decides the binding
    /// even when a perfectly good counterpart exists further up. Applicability is by argument count
    /// against the required/total parameter range, which keeps optional parameters working:
    /// <c>File.ReadAllTextAsync(path)</c> binds to a two-parameter method whose token is optional.
    /// </remarks>
    private static bool AsyncCallBindsToAwaitable(
        INamedTypeSymbol receiverType,
        string asyncName,
        int arity
    )
    {
        for (INamedTypeSymbol? current = receiverType; current != null; current = current.BaseType)
        {
            var applicable = current
                .GetMembers(asyncName)
                .OfType<IMethodSymbol>()
                .Where(candidate =>
                    arity >= candidate.Parameters.Count(p => !p.IsOptional && !p.IsParams)
                    && (
                        arity <= candidate.Parameters.Length
                        || candidate.Parameters.Any(p => p.IsParams)
                    )
                )
                .ToList();

            if (applicable.Count == 0)
                continue;

            return applicable.Any(candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public
                && CancellationTokenHelpers.IsAsyncReturnType(candidate.ReturnType)
            );
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when every named argument in the call names a parameter that exists at
    /// the same position on <paramref name="asyncCounterpart"/>, so copying the argument list into
    /// the rewritten call still binds.
    /// </summary>
    /// <remarks>
    /// A subclass may rename its override's parameters while inheriting the async counterpart, in
    /// which case the names are valid on the blocking call but not on the async one.
    /// </remarks>
    private static bool NamedArgumentsMatch(
        InvocationExpressionSyntax invocation,
        IMethodSymbol asyncCounterpart
    )
    {
        var arguments = invocation.ArgumentList.Arguments;

        for (var i = 0; i < arguments.Count; i++)
        {
            var name = arguments[i].NameColon?.Name.Identifier.Text;
            if (name is null)
                continue;

            if (
                i >= asyncCounterpart.Parameters.Length
                || asyncCounterpart.Parameters[i].Name != name
            )
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="async"/> equals <paramref name="sync"/> (by parameter
    /// type, in order), or equals it followed by one trailing <c>CancellationToken</c>
    /// (<paramref name="takesToken"/> set accordingly).
    /// </summary>
    private static bool ParametersMatch(
        ImmutableArray<IParameterSymbol> sync,
        ImmutableArray<IParameterSymbol> async,
        out bool takesToken
    )
    {
        takesToken = false;

        if (
            async.Length == sync.Length + 1
            && CancellationTokenHelpers.IsCancellationToken(async[sync.Length].Type)
        )
            takesToken = true;
        else if (async.Length != sync.Length)
            return false;

        for (var i = 0; i < sync.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(sync[i].Type, async[i].Type))
                return false;
        }

        return true;
    }
}

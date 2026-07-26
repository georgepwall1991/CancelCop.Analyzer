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
    /// Property key carrying the async counterpart's own token parameter name, so a fix that writes
    /// a named token argument uses the name that overload actually declares. An override is free to
    /// rename it (<c>ReadAsync(…, CancellationToken stop)</c>), and a hardcoded
    /// <c>cancellationToken:</c> would then fail with CS1739.
    /// </summary>
    public const string TokenArgumentNameProperty = "TokenArgumentName";

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
            && !IsBlockingStreamCall(context, invocation, method, containingType, methodName)
        )
            return;

        if (!CancellationTokenHelpers.IsInAsyncFunction(invocation))
            return;

        var tokenParameter = CancellationTokenHelpers.FindEnclosingCancellationTokenParameter(
            invocation,
            context.SemanticModel
        );

        // Resolve the counterpart the way the *rewritten* call will be resolved: by overload
        // resolution starting at the receiver's own type. Using the declaring type instead would miss
        // members declared on a subclass — `stream.CopyTo(...)` on a subclass resolves to
        // Stream.CopyTo, so the declaring type is Stream and a subclass's own `CopyToAsync` is
        // invisible to the search even though it is what the rewrite would bind to.
        var searchRoot = GetReceiverType(context, invocation) as INamedTypeSymbol ?? containingType;
        var asyncName = methodName + "Async";

        // Prefer the token-taking form when a token is in scope; otherwise the tokenless one (which
        // includes an overload whose trailing token is optional, e.g. File.ReadAllTextAsync).
        IMethodSymbol? asyncCounterpart = null;
        var flowToken =
            tokenParameter != null
            && TryFindBoundCounterpart(searchRoot, method, asyncName, true, out asyncCounterpart);

        if (
            !flowToken
            && !TryFindBoundCounterpart(searchRoot, method, asyncName, false, out asyncCounterpart)
        )
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;

        // The fix copies the original argument list verbatim, so every named argument has to name a
        // parameter that exists on the *async* overload. A subclass that renames its override's
        // parameters breaks that: `stream.Read(data: b, start: 0, …)` is a valid call, but the
        // inherited Stream.ReadAsync names them `buffer`/`offset`, so the rewrite would fail with
        // CS1739. The call is still genuinely blocking, so the diagnostic stands — only the fix is
        // withheld.
        if (!NamedArgumentsMatch(invocation, asyncCounterpart!))
            properties = properties.Add(NoFixProperty, "named-argument-mismatch");
        if (flowToken)
        {
            properties = properties.Add(TokenNameProperty, tokenParameter!.Name);
            properties = properties.Add(
                TokenArgumentNameProperty,
                asyncCounterpart!.Parameters[asyncCounterpart.Parameters.Length - 1].Name
            );
        }

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
        IMethodSymbol method,
        INamedTypeSymbol containingType,
        string methodName
    )
    {
        if (!StreamBlockingMethods.Contains(methodName))
            return false;

        // Match the framework primitive, not merely a same-named member on a Stream subclass. A
        // subclass is free to declare its own `Write(string)` convenience overload; that is not a
        // known-blocking stream primitive and must not be flagged. Walking to the root definition
        // resolves overrides back to Stream, so FileStream.Read still qualifies.
        // Exact identity, not "derives from": a subclass's own Write(string) is declared on the
        // subclass, which does derive from Stream — only members whose original definition sits on
        // Stream itself are the known-blocking primitives.
        if (!IsExactlySystemIoType(RootDefinition(method).ContainingType, "Stream"))
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> <i>is</i> the <c>System.IO</c> type named
    /// <paramref name="name"/> — not merely derived from it.
    /// </summary>
    private static bool IsExactlySystemIoType(ITypeSymbol? type, string name) =>
        type?.Name == name && type.ContainingNamespace?.ToDisplayString() == "System.IO";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> or any of its base types is the
    /// <c>System.IO</c> type named <paramref name="name"/>. Namespace-gated so a same-named
    /// user type is never mistaken for the framework one.
    /// </summary>
    /// <summary>
    /// Walks an override chain back to the method that originally declared it, so a member inherited
    /// from <c>Stream</c> is recognised through any depth of subclassing.
    /// </summary>
    private static IMethodSymbol RootDefinition(IMethodSymbol method)
    {
        var current = method;
        while (current.OverriddenMethod != null)
            current = current.OverriddenMethod;
        return current;
    }

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
    /// Finds the async counterpart that the rewritten call would actually bind to, and returns
    /// <c>true</c> only when that member is usable — <c>public</c> and returning an awaitable.
    /// </summary>
    /// <param name="searchRoot">The receiver's type; resolution starts here, not at the declaring type.</param>
    /// <param name="sync">The blocking method being replaced; its parameters shape the emitted call.</param>
    /// <param name="asyncName">The counterpart name (<c>&lt;name&gt;Async</c>).</param>
    /// <param name="wantToken">
    /// <c>true</c> to require an overload that accepts a trailing <c>CancellationToken</c> (a token is
    /// in scope and will be flowed); <c>false</c> to accept a tokenless call, which also matches an
    /// overload whose trailing token is optional (e.g. <c>File.ReadAllTextAsync</c>).
    /// </param>
    /// <remarks>
    /// <para>
    /// The walk mirrors C# overload resolution closely enough for this rule's purpose: it goes from
    /// the most-derived type upwards and stops at the first member that the emitted arguments would
    /// select. Stopping is what catches shadowing — a subclass declaring
    /// <c>new int ReadAsync(byte[], int, int)</c> wins resolution over the inherited awaitable one,
    /// so <c>await stream.ReadAsync(b, 0, n)</c> fails with CS1061. When the winner is unusable the
    /// rule stays quiet: there is no async alternative to point at, so the diagnostic's premise is
    /// false.
    /// </para>
    /// <para>
    /// Candidacy is decided by parameter <i>types</i> (via <see cref="ParametersMatch"/>), not by
    /// argument count. A same-arity but type-incompatible member such as
    /// <c>int ReadAsync(string, int, int)</c> is not what a <c>byte[]</c> call binds to, so it must
    /// not mask the inherited overload that the call really would select.
    /// </para>
    /// <para>
    /// Walking base types also matters in the ordinary case: a concrete stream overrides the blocking
    /// member but inherits the async counterpart, so a same-type-only lookup finds nothing and the
    /// rule would never fire.
    /// </para>
    /// </remarks>
    private static bool TryFindBoundCounterpart(
        INamedTypeSymbol? searchRoot,
        IMethodSymbol sync,
        string asyncName,
        bool wantToken,
        out IMethodSymbol? match
    )
    {
        match = null;

        for (INamedTypeSymbol? current = searchRoot; current != null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(asyncName).OfType<IMethodSymbol>())
            {
                if (
                    !ParametersMatch(
                        sync.Parameters,
                        candidate.Parameters,
                        out var candidateTakesToken
                    )
                )
                    continue;

                if (wantToken)
                {
                    if (!candidateTakesToken)
                        continue;
                }
                else if (
                    candidateTakesToken
                    && !candidate.Parameters[candidate.Parameters.Length - 1].IsOptional
                )
                {
                    // The emitted call passes no token, so an overload whose token is required is
                    // not what it would bind to.
                    continue;
                }

                // First applicable member wins resolution; if it is unusable, no rewrite exists.
                // A static member cannot be invoked through an instance receiver (CS0176) — and a
                // hiding static member still captures the name, so it must end the search rather
                // than be skipped over in favour of the inherited instance one.
                match = candidate;
                return candidate.DeclaredAccessibility == Accessibility.Public
                    && candidate.IsStatic == sync.IsStatic
                    && CancellationTokenHelpers.IsAsyncReturnType(candidate.ReturnType);
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when every named argument in the call names a parameter declared by
    /// <paramref name="asyncCounterpart"/>, so copying the argument list into the rewritten call
    /// still binds.
    /// </summary>
    /// <remarks>
    /// A subclass may rename its override's parameters while inheriting the async counterpart, in
    /// which case the names are valid on the blocking call but not on the async one. Matching is by
    /// name alone, not by position: named arguments may legally be reordered
    /// (<c>File.WriteAllText(contents: text, path: path)</c>), and such a call rewrites safely as
    /// long as the counterpart declares the same names.
    /// </remarks>
    private static bool NamedArgumentsMatch(
        InvocationExpressionSyntax invocation,
        IMethodSymbol asyncCounterpart
    )
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var name = argument.NameColon?.Name.Identifier.Text;
            if (name is null)
                continue;

            if (!asyncCounterpart.Parameters.Any(p => p.Name == name))
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

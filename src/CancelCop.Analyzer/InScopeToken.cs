using Microsoft.CodeAnalysis;

namespace CancelCop.Analyzer;

/// <summary>
/// A cancellation token that is in scope at an analyzed location — either a
/// <c>CancellationToken</c> parameter or a proven framework property such as
/// <c>HttpContext.RequestAborted</c> / <c>ServerCallContext.CancellationToken</c>.
/// </summary>
public sealed class InScopeToken
{
    public InScopeToken(string expressionText, ISymbol tokenSymbol, IParameterSymbol? parameter)
    {
        ExpressionText = expressionText;
        TokenSymbol = tokenSymbol;
        Parameter = parameter;
    }

    /// <summary>
    /// Source text to insert in a code fix: a parameter name, or a member access such as
    /// <c>context.RequestAborted</c>.
    /// </summary>
    public string ExpressionText { get; }

    /// <summary>The same text, used in diagnostic messages.</summary>
    public string DisplayName => ExpressionText;

    /// <summary>
    /// The token itself: the parameter symbol, or the framework property symbol. Used for
    /// identity checks (CC009 cancellation-check matching).
    /// </summary>
    public ISymbol TokenSymbol { get; }

    /// <summary>
    /// The parameter when this token is a <c>CancellationToken</c> parameter; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public IParameterSymbol? Parameter { get; }

    public static InScopeToken FromParameter(IParameterSymbol parameter) =>
        new(parameter.Name, parameter, parameter);

    public static InScopeToken FromMember(IParameterSymbol receiver, IPropertySymbol property) =>
        new(receiver.Name + "." + property.Name, property, parameter: null);
}

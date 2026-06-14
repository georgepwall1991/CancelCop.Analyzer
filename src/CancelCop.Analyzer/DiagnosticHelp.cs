namespace CancelCop.Analyzer;

/// <summary>
/// Shared <c>helpLinkUri</c> for the diagnostic descriptors, so IDEs surface a clickable
/// "learn more" link on each diagnostic that opens the rule documentation.
/// </summary>
internal static class DiagnosticHelp
{
    /// <summary>The README "Analyzer Rules" table, which lists every rule with its description.</summary>
    public const string LinkUri = "https://github.com/georgepwall1991/CancelCop.Analyzer#analyzer-rules";
}

namespace Sage.Utilities
{
    /// <summary>A secondary source location or symbol that explains a diagnostic (e.g. the operation that moved an owner).</summary>
    /// <param name="Symbol">Related symbol name, when one exists.</param>
    /// <param name="Note">Short description of how the symbol/location relates to the diagnostic.</param>
    /// <param name="File">Source file of the related location, when known.</param>
    /// <param name="Line">1-based line of the related location, or 0 when unknown.</param>
    /// <param name="Column">1-based column of the related location, or 0 when unknown.</param>
    public sealed record DiagnosticRelated(string? Symbol, string? Note, string? File, int Line, int Column);

    /// <summary>One compiler diagnostic in the shape shared by human and machine-readable output.</summary>
    /// <param name="Code">Stable code such as <c>S140</c>; see <c>docs/diagnostics.md</c>.</param>
    /// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
    /// <param name="File">Source file name as printed in human output; null for failures that belong to no source file.</param>
    /// <param name="Line">1-based line, or 0 when the failure has no source location.</param>
    /// <param name="Column">1-based column, or 0 when the failure has no source location.</param>
    /// <param name="Message">The human message, identical to the text of the human-readable output.</param>
    /// <param name="Rule">The language rule the diagnostic enforces, from <see cref="DiagnosticCatalog"/>.</param>
    /// <param name="Related">Related symbols and locations.</param>
    /// <param name="Suggestion">An actionable correction, present only when it is reliable.</param>
    public sealed record Diagnostic(
        string Code,
        string Severity,
        string? File,
        int Line,
        int Column,
        string Message,
        string? Rule = null,
        IReadOnlyList<DiagnosticRelated>? Related = null,
        string? Suggestion = null);
}

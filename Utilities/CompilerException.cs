using Sage.Ast;
using Sage.Core;

namespace Sage.Utilities
{
    /// <summary>
    /// Represents a specialized exception for errors encountered during the compilation process.
    /// Stores detailed source location information (line, column) and unique error codes.
    /// </summary>
    public class CompilerException : Exception
    {
        /// <summary>
        /// Gets the <see cref="Token"/> that caused the error, if available. 
        /// Typically present during Lexical or Syntax analysis.
        /// </summary>
        public Token? OffendingToken { get; private set; }

        /// <summary>
        /// Gets the unique identification code for the error (e.g., "S101").
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Gets the 1-based line number in the source file where the error occurred.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the 1-based column position in the source file where the error occurred.
        /// </summary>
        public int Column { get; }

        /// <summary>Related symbols and locations that explain the error, such as the operation that invalidated an owner.</summary>
        public IReadOnlyList<DiagnosticRelated> Related => _related;

        /// <summary>An actionable correction that is specific to this occurrence; falls back to the catalogued hint when null.</summary>
        public string? Suggestion { get; private set; }

        private readonly List<DiagnosticRelated> _related = [];

        /// <summary>Attaches a related symbol or location to this error and returns it for fluent throwing.</summary>
        public CompilerException WithRelated(string? symbol, string? note, string? file, int line, int column)
        {
            _related.Add(new DiagnosticRelated(symbol, note, file, line, column));
            return this;
        }

        /// <summary>Attaches an occurrence-specific correction hint and returns this error for fluent throwing.</summary>
        public CompilerException WithSuggestion(string suggestion)
        {
            Suggestion = suggestion;
            return this;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompilerException"/> class using a source token.
        /// Used primarily by the Lexer and Parser.
        /// </summary>
        /// <param name="token">The token where the error was detected.</param>
        /// <param name="code">The unique error code.</param>
        /// <param name="message">A descriptive message explaining the error.</param>
        public CompilerException(Token? token, string code, string message) : base(message)
        {
            ErrorCode = code;
            OffendingToken = token;
            if (token != null)
            {
                Line = token.Line;
                Column = token.Column;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompilerException"/> class using an AST node.
        /// Used primarily by the Semantic Analyzer when location data is tied to the node structure.
        /// </summary>
        /// <param name="node">The AST node where the semantic error was detected.</param>
        /// <param name="code">The unique error code.</param>
        /// <param name="message">A descriptive message explaining the error.</param>
        public CompilerException(AstNode node, string code, string message) : base(message)
        {
            ErrorCode = code;
            Line = node.Line;
            Column = node.Column;
            OffendingToken = null;
        }

        /// <summary>
        /// Returns a formatted string representation of the compilation error,
        /// including the error code, message, and source location.
        /// </summary>
        /// <returns>A string formatted for compiler output.</returns>
        public override string ToString()
        {
            // Format for errors that have an associated token (Lexer/Parser)
            if (OffendingToken != null)
            {
                return $"error {ErrorCode}: {Message} at line {OffendingToken.Line}, column {OffendingToken.Column} ('{OffendingToken.Value}')";
            }

            // Format for errors derived from AST nodes (Semantic Analysis)
            if (Line > 0)
            {
                return $"error {ErrorCode}: {Message} at line {Line}, column {Column}";
            }

            // Fallback format
            return $"error {ErrorCode}: {Message}";
        }
    }
}
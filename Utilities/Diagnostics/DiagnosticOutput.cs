using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sage.Utilities
{
    /// <summary>Selectable diagnostic output format.</summary>
    public enum DiagnosticFormat { Human, Json }

    /// <summary>
    /// Opt-in machine-readable diagnostics. Human output stays the default and is produced by
    /// <see cref="CompilerLogger"/>; with <c>--diagnostics json</c> each diagnostic becomes one JSON object per
    /// line on standard output and informational success messages are suppressed.
    /// </summary>
    public static class DiagnosticOutput
    {
        private const string FlagName = "--diagnostics";
        private static readonly Dictionary<string, string> SourcePaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>The format selected for this process.</summary>
        public static DiagnosticFormat Format { get; set; } = DiagnosticFormat.Human;

        /// <summary>True when diagnostics are emitted as JSON lines.</summary>
        public static bool IsJson => Format == DiagnosticFormat.Json;

        /// <summary>
        /// Extracts <c>--diagnostics json|human</c> (or <c>--diagnostics=json|human</c>) from the arguments,
        /// applies it, and returns the remaining arguments untouched.
        /// </summary>
        /// <exception cref="ArgumentException">The format is missing or unknown.</exception>
        public static string[] ConfigureFromArgs(string[] args)
        {
            var remaining = new List<string>(args.Length);
            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                string? value = null;
                if (arg.Equals(FlagName, StringComparison.Ordinal))
                    value = index + 1 < args.Length ? args[++index] : "";
                else if (arg.StartsWith(FlagName + "=", StringComparison.Ordinal))
                    value = arg[(FlagName.Length + 1)..];

                if (value == null) { remaining.Add(arg); continue; }
                Format = value switch
                {
                    "json" => DiagnosticFormat.Json,
                    "human" => DiagnosticFormat.Human,
                    _ => throw new ArgumentException($"Unknown diagnostics format '{value}'. Use '--diagnostics json' or '--diagnostics human'.")
                };
            }
            return remaining.ToArray();
        }

        /// <summary>Remembers the full path of a source so JSON diagnostics can report it next to the file name.</summary>
        public static void RegisterSource(string path) => SourcePaths[Path.GetFileName(path)] = Path.GetFullPath(path);

        /// <summary>Builds a diagnostic enriched with the catalogued rule and suggestion.</summary>
        public static Diagnostic Create(string? file, int line, int column, string code, string message,
            IReadOnlyList<DiagnosticRelated>? related = null, string? suggestion = null) =>
            new(code, "error", file, line, column, message,
                DiagnosticCatalog.RuleFor(code), related is { Count: > 0 } ? related : null,
                suggestion ?? DiagnosticCatalog.SuggestionFor(code));

        /// <summary>Builds the diagnostic for any exception, mapping non-language failures to the file-less tool-failure code.</summary>
        public static Diagnostic FromException(Exception exception, string? currentFile)
        {
            if (exception is CompilerException compiler)
                return Create(currentFile ?? "main.sg", compiler.Line, compiler.Column, compiler.ErrorCode, compiler.Message,
                    compiler.Related, compiler.Suggestion);
            return Create(null, 0, 0, DiagnosticCatalog.ToolFailureCode, exception.Message.Trim());
        }

        /// <summary>Writes one diagnostic as a single JSON line to standard output.</summary>
        public static void Write(Diagnostic diagnostic) => Console.Out.WriteLine(ToJson(diagnostic));

        /// <summary>Serializes a diagnostic with a fixed property order and omitted empty fields.</summary>
        public static string ToJson(Diagnostic diagnostic)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                writer.WriteString("severity", diagnostic.Severity);
                if (diagnostic.File != null)
                {
                    writer.WriteString("file", diagnostic.File);
                    if (SourcePaths.TryGetValue(diagnostic.File, out var path)) writer.WriteString("path", path);
                }
                if (diagnostic.Line > 0)
                {
                    writer.WriteNumber("line", diagnostic.Line);
                    writer.WriteNumber("column", diagnostic.Column);
                }
                writer.WriteString("message", diagnostic.Message);
                if (diagnostic.Rule != null) writer.WriteString("rule", diagnostic.Rule);
                if (diagnostic.Related is { Count: > 0 })
                {
                    writer.WriteStartArray("related");
                    foreach (var related in diagnostic.Related)
                    {
                        writer.WriteStartObject();
                        if (related.Symbol != null) writer.WriteString("symbol", related.Symbol);
                        if (related.Note != null) writer.WriteString("note", related.Note);
                        if (related.File != null) writer.WriteString("file", related.File);
                        if (related.Line > 0)
                        {
                            writer.WriteNumber("line", related.Line);
                            writer.WriteNumber("column", related.Column);
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                if (diagnostic.Suggestion != null) writer.WriteString("suggestion", diagnostic.Suggestion);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}

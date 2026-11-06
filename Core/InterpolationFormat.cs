using System.Text.RegularExpressions;

namespace Sage.Core
{
    /// <summary>
    /// Maps one interpolated value to its checked C <c>printf</c> conversion.
    /// The analyzer and the code generator share this table so a program is accepted only when its
    /// format specifier is valid for the value's type, and the emitted conversion always matches
    /// the (explicitly cast) argument that is passed to it.
    /// </summary>
    public static class InterpolationFormat
    {
        private static readonly HashSet<string> UnsignedTypes =
            ["u8", "u16", "u32", "u64", "c_uchar", "c_ushort", "c_uint", "c_ulong", "c_ulonglong", "c_size_t", "c_uintptr"];

        // flags, width, precision, then one optional conversion letter; widths stay small so padding is bounded.
        private static readonly Regex SpecifierShape =
            new(@"^(?<flags>[-+ 0#]*)(?<width>\d{1,4})?(?:\.(?<precision>\d{1,4}))?(?<conversion>[a-zA-Z])?$", RegexOptions.Compiled);

        // Legacy shorthand for floating point: "2f" means two decimals.
        private static readonly Regex DecimalsShorthand = new(@"^(?<decimals>\d{1,4})f$", RegexOptions.Compiled);

        /// <summary>Whether a value of this type can be interpolated at all.</summary>
        public static bool IsSupported(string type) =>
            type is "str" or "b8" or "c_bool" || TypeSystem.IsNumeric(type);

        /// <summary>
        /// Resolves the C conversion (for example <c>%.2f</c>) and the cast applied to the argument.
        /// Returns false with a reason when the specifier does not fit the type.
        /// </summary>
        public static bool TryResolve(string type, string? specifier, out string conversion, out string cast, out string error)
        {
            conversion = cast = error = "";
            bool isString = type == "str";
            bool isFloat = TypeSystem.IsFloatingPoint(type);
            bool isUnsigned = UnsignedTypes.Contains(type);
            cast = isString ? "" : isFloat ? "(double)" : isUnsigned ? "(unsigned long long)" : "(long long)";
            string length = isString || isFloat ? "" : "ll";
            string defaultLetter = isString ? "s" : isFloat ? "f" : isUnsigned ? "u" : "d";

            if (string.IsNullOrEmpty(specifier))
            {
                conversion = $"%{length}{defaultLetter}";
                return true;
            }

            if (isFloat && DecimalsShorthand.Match(specifier) is { Success: true } shorthand)
            {
                conversion = $"%.{shorthand.Groups["decimals"].Value}f";
                return true;
            }

            var match = SpecifierShape.Match(specifier);
            if (!match.Success)
            {
                error = $"'{specifier}' is not a valid format specifier. Use flags, a width, a precision, and an optional conversion letter.";
                return false;
            }

            string flags = match.Groups["flags"].Value;
            string width = match.Groups["width"].Value;
            string precision = match.Groups["precision"].Success ? "." + match.Groups["precision"].Value : "";
            string letter = match.Groups["conversion"].Value;

            string[] allowed = isString ? ["s"] : isFloat ? ["f", "e", "E", "g", "G"]
                : isUnsigned ? ["u", "x", "X", "o"] : ["d"];
            if (letter.Length > 0 && !allowed.Contains(letter))
            {
                error = $"Conversion '{letter}' does not fit a value of type '{type}'. Allowed: {string.Join(", ", allowed)}.";
                return false;
            }

            string chosen = letter.Length > 0 ? letter : defaultLetter;
            bool alternateForm = chosen is "x" or "X" or "o" || isFloat;
            if (isString ? flags.Trim('-').Length > 0 : flags.Contains('#') && !alternateForm)
            {
                error = $"The flags '{flags}' do not apply to a value of type '{type}'.";
                return false;
            }

            conversion = $"%{flags}{width}{precision}{length}{chosen}";
            return true;
        }
    }

    /// <summary>Facts shared by the analyzer and generator about the <c>Text</c> an interpolation builds.</summary>
    internal static class InterpolationText
    {
        /// <summary>The std resource an interpolated string evaluates to.</summary>
        public const string TypeName = "Text";

        /// <summary>A literal part is fixed text in the format string; every other part is a value.</summary>
        public static bool IsSegment(Ast.AstNode part) => part is Ast.LiteralNode { TypeName: "str" };
    }
}

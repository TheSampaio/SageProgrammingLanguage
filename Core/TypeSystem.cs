namespace Sage.Core
{
    /// <summary>
    /// Centralized service for handling Sage type definitions, conversions, and compatibility rules.
    /// This is the single source of truth for how Sage types map onto C types, which keeps FFI
    /// and standard-library authoring predictable.
    /// </summary>
    public static class TypeSystem
    {
        private static readonly List<string> NumericHierarchy =
            ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "f32", "f64"];

        /// <summary>
        /// C interop aliases that behave as numeric scalars. They participate in numeric literal
        /// assignment (so byte-level FFI code like <c>buf[i] = 0</c> type-checks), though cross-type
        /// arithmetic promotion is intentionally limited to the fixed-width Sage hierarchy.
        /// </summary>
        private static readonly HashSet<string> CNumericTypes =
        [
            "c_char", "c_schar", "c_uchar", "c_short", "c_ushort", "c_int", "c_uint",
            "c_long", "c_ulong", "c_longlong", "c_ulonglong", "c_size_t", "c_ssize_t",
            "c_intptr", "c_uintptr", "c_float", "c_double"
        ];

        /// <summary>
        /// C interop aliases used when writing FFI bindings and standard-library modules.
        /// These map onto the *native* C types (whose width is platform-defined) instead of the
        /// fixed-width Sage primitives, so bindings line up with real C headers without guesswork.
        /// </summary>
        private static readonly Dictionary<string, string> CInteropTypes = new()
        {
            ["c_void"] = "void",
            ["c_bool"] = "bool",
            ["c_char"] = "char",
            ["c_schar"] = "signed char",
            ["c_uchar"] = "unsigned char",
            ["c_short"] = "short",
            ["c_ushort"] = "unsigned short",
            ["c_int"] = "int",
            ["c_uint"] = "unsigned int",
            ["c_long"] = "long",
            ["c_ulong"] = "unsigned long",
            ["c_longlong"] = "long long",
            ["c_ulonglong"] = "unsigned long long",
            ["c_float"] = "float",
            ["c_double"] = "double",
            ["c_size_t"] = "size_t",
            ["c_ssize_t"] = "ptrdiff_t",
            ["c_intptr"] = "intptr_t",
            ["c_uintptr"] = "uintptr_t",
        };

        /// <summary>
        /// Opaque C types registered through <c>type Name;</c> inside extern blocks (e.g. <c>FILE</c>).
        /// They carry no Sage-side layout; the name is emitted verbatim to C.
        /// </summary>
        private static readonly HashSet<string> OpaqueTypes = [];

        /// <summary>Registers an opaque C type (declared via <c>type Name;</c> in an extern block).</summary>
        public static void RegisterOpaqueType(string name) => OpaqueTypes.Add(name);

        /// <summary>True if the given name is a registered opaque C type.</summary>
        public static bool IsOpaqueType(string name) => OpaqueTypes.Contains(name);

        /// <summary>True if the given name is a C interop alias such as <c>c_int</c> or <c>c_size_t</c>.</summary>
        public static bool IsCInteropType(string name) => CInteropTypes.ContainsKey(name);

        /// <summary>Clears run-scoped registrations. Useful when the compiler processes several inputs in-process.</summary>
        public static void Reset() => OpaqueTypes.Clear();

        /// <summary>
        /// Converts a Sage type identifier to its corresponding C language type.
        /// Handles pointer recursion, C interop aliases, and special types like 'none' (void) or 'str' (char*).
        /// </summary>
        public static string ToCType(string sageType)
        {
            if (string.IsNullOrEmpty(sageType)) return "void";

            // Recursive pointer handling (e.g. i32*, none**, c_char*).
            if (sageType.EndsWith('*'))
                return $"{ToCType(sageType[..^1])}*";

            // Arrays decay to their element type here; callers that need the [N] form use the array helpers.
            if (IsArrayType(sageType))
                return ToCType(GetArrayBaseType(sageType));

            if (CInteropTypes.TryGetValue(sageType, out var cType))
                return cType;

            return sageType switch
            {
                "none" => "void",
                "str" => "char*",
                _ => sageType // Fixed-width primitives, structs and opaque types map directly.
            };
        }

        /// <summary>
        /// Determines if a type is a raw pointer (<c>T*</c>, including <c>none*</c>).
        /// Note: <c>str</c> is a first-class safe string type, not a raw pointer, and safe heap
        /// references (produced by <c>new</c>) are tracked separately via an AST flag.
        /// </summary>
        public static bool IsRawPointer(string? type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            if (IsArrayType(type)) return false;
            return type.EndsWith('*');
        }

        /// <summary>Determines if a type is a numeric scalar (Sage primitive or C interop alias).</summary>
        public static bool IsNumeric(string type) => NumericHierarchy.Contains(type) || CNumericTypes.Contains(type);

        /// <summary>Determines if a type is a floating-point scalar.</summary>
        public static bool IsFloatingPoint(string type) => type is "f32" or "f64" or "c_float" or "c_double";

        /// <summary>
        /// Determines the resulting type of a binary operation between two numeric types
        /// based on implicit promotion rules.
        /// </summary>
        public static string? GetDominantType(string typeA, string typeB)
        {
            int indexA = NumericHierarchy.IndexOf(typeA);
            int indexB = NumericHierarchy.IndexOf(typeB);

            if (indexA == -1 || indexB == -1) return null;
            return NumericHierarchy[Math.Max(indexA, indexB)];
        }

        /// <summary>
        /// Checks if a value of <paramref name="sourceType"/> can be implicitly assigned to <paramref name="targetType"/>.
        /// </summary>
        public static bool AreTypesCompatible(string targetType, string sourceType)
        {
            if (targetType == sourceType) return true;

            // Universal pointer compatibility: 'none*' (void*) implicitly converts to/from any pointer (including 'str').
            bool isTargetPointer = IsRawPointer(targetType) || targetType == "str";
            bool isSourcePointer = IsRawPointer(sourceType) || sourceType == "str";

            if ((isTargetPointer && sourceType == "none*") ||
                (targetType == "none*" && isSourcePointer))
            {
                return true;
            }

            // Implicit promotion (e.g., f32 -> f64)
            if (targetType == "f64" && sourceType == "f32") return true;

            return false;
        }

        /// <summary>
        /// Formats a parameter as a C declaration, handling the array form (<c>T name[N]</c>).
        /// Shared by the code and header generators to keep signatures consistent.
        /// </summary>
        public static string FormatCParameter(string name, string type)
        {
            if (IsArrayType(type))
                return $"{ToCType(GetArrayBaseType(type))} {name}[{GetArraySize(type)}]";
            return $"{ToCType(type)} {name}";
        }

        /// <summary>True if the type is a fixed-size array such as <c>i32[4]</c>.</summary>
        public static bool IsArrayType(string type) => type.Contains('[') && type.EndsWith(']');

        /// <summary>Returns the element type of an array type (e.g. <c>i32[4]</c> -> <c>i32</c>).</summary>
        public static string GetArrayBaseType(string type)
        {
            if (!IsArrayType(type)) return type;
            return type[..type.IndexOf('[')];
        }

        /// <summary>Returns the declared length of an array type, or 0 if it cannot be parsed.</summary>
        public static int GetArraySize(string type)
        {
            if (!IsArrayType(type)) return 0;
            int start = type.IndexOf('[') + 1;
            string sizeStr = type.Substring(start, type.IndexOf(']') - start);
            return int.TryParse(sizeStr, out int size) ? size : 0;
        }
    }
}

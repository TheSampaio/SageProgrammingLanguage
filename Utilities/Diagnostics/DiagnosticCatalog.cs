namespace Sage.Utilities
{
    /// <summary>
    /// Stable per-code metadata: the violated language rule and, where it is reliable, a correction hint.
    /// The same table is documented in <c>docs/diagnostics.md</c>; a regression test keeps both in sync.
    /// </summary>
    public static class DiagnosticCatalog
    {
        /// <summary>Code used for failures that are not language diagnostics (missing files, native toolchain errors).</summary>
        public const string ToolFailureCode = "S900";

        private static readonly Dictionary<string, (string Rule, string? Suggestion)> Entries = new(StringComparer.Ordinal)
        {
            ["S000"] = ("Source text may contain only recognized characters.", null),
            ["S001"] = ("The source must follow the Sage grammar; a required token is missing or misplaced.", null),
            ["S002"] = ("A module block may contain only functions and extern blocks.", null),
            ["S003"] = ("A type name is required here.", null),
            ["S004"] = ("An expression or statement cannot start with this token.", null),
            ["S005"] = ("Only variables, struct fields, array elements, and resource field paths can be assigned.", null),
            ["S006"] = ("An extern block may contain only func, type, and resource declarations.", null),
            ["S007"] = ("Every 'use' must name a module file in the project source directory or the standard library.",
                "Create the module file next to the importing file, or correct the module name in 'use'."),
            ["S010"] = ("String interpolation braces must be balanced.", null),
            ["S011"] = ("An interpolation must contain an expression.", null),
            ["S012"] = ("A variable declared in the standard form must have an initializer.", null),
            ["S013"] = ("A string literal must be terminated.", null),
            ["S101"] = ("An initializer must have the declared variable type.", null),
            ["S102"] = ("A name may be declared only once per scope.", null),
            ["S103"] = ("A constant cannot be reassigned or mutated.", null),
            ["S104"] = ("A condition must have type b8.", "Compare the value explicitly so the condition has type b8."),
            ["S105"] = ("An identifier or function must be declared before it is used.", null),
            ["S107"] = ("Logical operators require b8 operands.", null),
            ["S108"] = ("Arithmetic and assignment require compatible types.", null),
            ["S109"] = ("Member access requires a struct value.", null),
            ["S110"] = ("Member access must name a declared field.", null),
            ["S111"] = ("Only arrays and raw pointers can be indexed.", null),
            ["S112"] = ("An array index must be an integer.", null),
            ["S120"] = ("A raw pointer in a function signature requires an unsafe function.",
                "Declare the function 'unsafe func', or use a safe reference."),
            ["S121"] = ("A raw pointer variable requires an unsafe context.",
                "Wrap the code in an 'unsafe { }' block, or use a plain struct value or an owning std type."),
            ["S122"] = ("Casting to a raw pointer requires an unsafe context.", "Wrap the cast in an 'unsafe { }' block."),
            ["S123"] = ("Indexing a raw pointer requires an unsafe context.", "Wrap the access in an 'unsafe { }' block."),
            ["S130"] = ("'new' requires a struct type.", null),
            ["S131"] = ("A struct initializer may set only declared fields.", null),
            ["S132"] = ("'delete' applies only to a reference created with 'new'.", null),
            ["S133"] = ("'new' and 'delete' are manual memory management and require an unsafe context.",
                "Use a plain struct value, which is cleaned up automatically, or wrap the code in an 'unsafe { }' block."),
            ["S140"] = ("A moved owner cannot be used again.",
                "Use the owner before it is moved, or move it only on its last use."),
            ["S141"] = ("A deleted reference cannot be used again.", "Do not use the reference after 'delete'."),
            ["S142"] = ("An owner that may have been moved or deleted on another path cannot be used.",
                "Move or delete the owner on every path, or on none."),
            ["S143"] = ("Resource reassignment and field replacement are limited to standalone assignments between named local owners.", null),
            ["S144"] = ("A reference needs an owning or borrowing contract before it can be used in this position.", null),
            ["S145"] = ("An owner declared outside a loop cannot be moved or deleted inside it.",
                "Declare the owner inside the loop body."),
            ["S146"] = ("'new' must directly initialize a local owning binding.", null),
            ["S150"] = ("A declaration name must be unique in its scope.", null),
            ["S151"] = ("Only callable symbols can be called.", null),
            ["S152"] = ("Redeclared native aliases must have identical signatures and native origins.", null),
            ["S153"] = ("A callable is visible only in its declaring file and in files that directly 'use' it; native aliases are file-private.",
                "Add a 'use' for the declaring module, or declare the extern binding in this file."),
            ["S154"] = ("Parameter names must be unique within a function.", null),
            ["S156"] = ("A dot call requires an imported 'method func' for the receiver type; fields are not callable.", null),
            ["S157"] = ("A 'method func' must be a non-extern module function whose first parameter is a supported, non-consuming receiver.", null),
            ["S158"] = ("A method call must resolve to exactly one visible method.", "Qualify the call as 'module::name(value, ...)'."),
            ["S159"] = ("Only functions declared with 'method func' can be called with dot syntax.",
                "Call it as 'module::name(value, ...)' or declare it with 'method func'."),
            ["S160"] = ("A call must pass exactly the declared number of arguments.", null),
            ["S161"] = ("A call argument must have the declared parameter type.", null),
            ["S162"] = ("A return statement must be inside a function and return the declared type.", null),
            ["S163"] = ("A function with a return type must return a value on every path.",
                "Add a return statement on every path that reaches the end of the function."),
            ["S164"] = ("Unsafe and native functions can be called only from an unsafe context.",
                "Call it inside an 'unsafe { }' block or from an 'unsafe func'."),
            ["S170"] = ("A borrowed parameter requires an explicit matching borrow argument.",
                "Pass '&name' or '&mut name' of a named variable matching the parameter type."),
            ["S171"] = ("Mutable access requires a mutable binding that is not a shared borrow.", null),
            ["S172"] = ("Borrows of the same value in one call cannot conflict.", null),
            ["S173"] = ("Borrows cannot be returned, stored in structs, or crossed over a native ABI yet.", null),
            ["S174"] = ("A borrowed value cannot be deleted.", null),
            ["S175"] = ("Only a named variable can be borrowed.", null),
            ["S176"] = ("Only scalars, supported structs, and native resources can be borrowed.", null),
            ["S177"] = ("Calls and mutations cannot be evaluated alongside borrowed arguments.",
                "Evaluate the call or mutation into a local value before this call."),
            ["S178"] = ("A source value cannot be accessed in a way that conflicts with a live borrow.",
                "End the borrow's scope before accessing the source this way."),
            ["S180"] = ("An array or struct initializer needs a compatible declared destination.", null),
            ["S181"] = ("An array element or field value must have the declared type.", null),
            ["S182"] = ("An array literal cannot have more elements than the declared length.", null),
            ["S183"] = ("A struct field can be declared or initialized only once.", null),
            ["S185"] = ("A constant array index must be inside the array bounds.", null),
            ["S187"] = ("A fixed-array length must be positive and representable as i32.", null),
            ["S188"] = ("Fixed-array value operations are limited to supported copy contracts.", null),
            ["S189"] = ("An array field of a temporary value cannot be indexed or mutated directly.",
                "Bind the containing value to a local first."),
            ["S190"] = ("A native resource must declare one pointer to an opaque or native ABI type and a matching destructor.", null),
            ["S191"] = ("Native resources are supported only as local owners and explicit borrows.", null),
            ["S192"] = ("Resource adoption and handle exposure require unsafe and the exact native pointer type.", null),
            ["S193"] = ("An owning resource result must flow directly into a local owner.",
                "Bind the producer's result to a local before using it."),
            ["S194"] = ("A call cannot combine consumed resource arguments with conflicting borrows or evaluation hazards.", null),
            ["S195"] = ("Contextual null supports only == or != against a named resource owner or borrow.", null),
            ["S196"] = ("A struct containing native resources must be initialized from a literal, a named owner move, or a direct owning call.", null),
            ["S197"] = ("A resource array requires local zero initialization or a direct array literal.", null),
            ["S210"] = ("An 'extern cpp' binding must use a supported adapter shape and type mapping.", null),
            ["S211"] = ("A declared native source must exist and be a .c, .cpp, .cc, or .cxx file.", null),
            ["S212"] = ("Distinct declarations must not lower to the same C symbol.", "Rename one of the declarations."),
            ["S213"] = ("A native parameter cannot have type 'none'.", "Use 'none*' for an untyped pointer."),
            ["S900"] = ("The compiler or an external tool (file system, native toolchain) failed before producing a language diagnostic.", null),
        };

        /// <summary>All catalogued codes in ascending order.</summary>
        public static IEnumerable<string> Codes => Entries.Keys.OrderBy(code => code, StringComparer.Ordinal);

        /// <summary>Returns the rule for a code, or null for an unknown code.</summary>
        public static string? RuleFor(string code) => Entries.TryGetValue(code, out var entry) ? entry.Rule : null;

        /// <summary>Returns the reliable correction hint for a code, if one exists.</summary>
        public static string? SuggestionFor(string code) => Entries.TryGetValue(code, out var entry) ? entry.Suggestion : null;
    }
}

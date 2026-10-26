using Sage.Ast;

namespace Sage.Core
{
    /// <summary>
    /// Centralizes the rules that map Sage function declarations onto their C symbol names,
    /// so the code and header generators always agree on linkage names.
    /// </summary>
    internal static class CNaming
    {
        /// <summary>
        /// Resolves the C symbol name for a function:
        /// <list type="bullet">
        /// <item><c>main</c> is preserved verbatim.</item>
        /// <item>Extern (FFI) functions keep their original C name.</item>
        /// <item>Extern cpp functions use their generated adapter name.</item>
        /// <item>Module functions are namespaced as <c>module_function</c>.</item>
        /// </list>
        /// </summary>
        public static string ResolveFunctionName(FunctionDeclarationNode node)
        {
            if (node.Name.Equals("main", StringComparison.OrdinalIgnoreCase)) return "main";
            if (node.CppBinding != null) return CppAdapterFunction(node.ModuleOwner, node.Name);
            if (node.IsExtern) return node.Name;
            return string.IsNullOrEmpty(node.ModuleOwner) ? node.Name : $"{node.ModuleOwner}_{node.Name}";
        }

        // The alias length prefix keeps names unambiguous ("a_b"+"c" vs "a"+"b_c") without
        // the double underscores C++ reserves.

        /// <summary>The <c>extern "C"</c> adapter symbol for an extern cpp function.</summary>
        public static string CppAdapterFunction(string alias, string name) => $"sage_cpp_fn_{alias.Length}{alias}_{name}";

        /// <summary>The incomplete C struct tag that represents an extern cpp class handle.</summary>
        public static string CppHandleTag(string alias, string name) => $"sage_cpp_cls_{alias.Length}{alias}_{name}";
    }
}

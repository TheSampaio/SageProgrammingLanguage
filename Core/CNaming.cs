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
        /// <item>Module functions are namespaced as <c>module_function</c>.</item>
        /// </list>
        /// </summary>
        public static string ResolveFunctionName(FunctionDeclarationNode node)
        {
            if (node.Name.Equals("main", StringComparison.OrdinalIgnoreCase)) return "main";
            if (node.IsExtern) return node.Name;
            return string.IsNullOrEmpty(node.ModuleOwner) ? node.Name : $"{node.ModuleOwner}_{node.Name}";
        }
    }
}

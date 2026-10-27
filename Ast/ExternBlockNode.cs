using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an FFI block declaring external C functions and opaque types.
    /// Example: <c>extern stdio("stdio.h") { ... }</c> or, with a link library,
    /// <c>extern winsock("winsock2.h", "ws2_32") { ... }</c>.
    /// <c>extern cpp alias("header.hpp") { ... }</c> declares C++ targets reached through generated adapters.
    /// </summary>
    public class ExternBlockNode(string alias, string header, List<AstNode> declarations, string? library = null, bool isCpp = false,
        List<string>? sources = null) : AstNode
    {
        /// <summary>Project native sources (<c>source "x.cpp";</c>) compiled and linked with this block, relative to its .sg file.</summary>
        public List<string> Sources { get; } = sources ?? [];

        /// <summary>True for <c>extern cpp</c> blocks, whose header is only visible to the generated C++ adapters.</summary>
        public bool IsCpp { get; } = isCpp;

        public string Alias { get; } = alias;
        public string Header { get; } = header;
        public List<AstNode> Declarations { get; } = declarations;

        /// <summary>Optional native library to link against (e.g. "ws2_32" → gcc -lws2_32).</summary>
        public string? Library { get; } = library;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}
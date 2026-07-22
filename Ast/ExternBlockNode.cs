using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an FFI block declaring external C functions and opaque types.
    /// Example: <c>extern stdio("stdio.h") { ... }</c> or, with a link library,
    /// <c>extern winsock("winsock2.h", "ws2_32") { ... }</c>.
    /// </summary>
    public class ExternBlockNode(string alias, string header, List<AstNode> declarations, string? library = null) : AstNode
    {
        public string Alias { get; } = alias;
        public string Header { get; } = header;
        public List<AstNode> Declarations { get; } = declarations;

        /// <summary>Optional native library to link against (e.g. "ws2_32" → gcc -lws2_32).</summary>
        public string? Library { get; } = library;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}
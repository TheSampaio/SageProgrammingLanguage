using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an opaque C type declared inside an extern block via <c>type Name;</c>
    /// (e.g. <c>type FILE;</c>). It introduces a type name that Sage can reference (typically
    /// through pointers such as <c>FILE*</c>) without knowing its layout; the name is emitted
    /// verbatim to C, where the included header provides the real definition.
    /// </summary>
    /// <param name="name">The opaque type name.</param>
    public class ExternTypeNode(string name) : AstNode
    {
        /// <summary>Gets the opaque type name.</summary>
        public string Name { get; } = name;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

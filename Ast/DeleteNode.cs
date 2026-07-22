using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents a <c>delete expr;</c> statement that releases a safe heap reference
    /// previously produced by <c>new</c>. Transpiles to <c>free</c> in the C backend.
    /// </summary>
    /// <param name="target">The reference expression to release.</param>
    public class DeleteNode(AstNode target) : AstNode
    {
        /// <summary>Gets the reference being released.</summary>
        public AstNode Target { get; } = target;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

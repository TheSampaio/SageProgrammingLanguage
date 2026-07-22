using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an <c>unsafe { ... }</c> block. Inside it, raw pointer types and pointer
    /// operations are permitted and calls to unsafe functions are allowed. Everywhere else,
    /// Sage is memory-safe and forbids raw pointers.
    /// </summary>
    /// <param name="body">The block of statements executed in the unsafe context.</param>
    public class UnsafeBlockNode(BlockNode body) : AstNode
    {
        /// <summary>Gets the statements guarded by this unsafe context.</summary>
        public BlockNode Body { get; } = body;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

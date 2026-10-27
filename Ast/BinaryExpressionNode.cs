using Sage.Enums;
using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an expression involving a binary operator (e.g., +, -, *, /).
    /// Links two operand nodes through a specific <see cref="TokenType"/>.
    /// </summary>
    /// <param name="left">The left operand node.</param>
    /// <param name="operatorType">The operator token type.</param>
    /// <param name="right">The right operand node.</param>
    public class BinaryExpressionNode(AstNode left, TokenType operatorType, AstNode right) : AstNode
    {
        /// <summary>
        /// Gets the left-hand side operand of the expression.
        /// </summary>
        public AstNode Left { get; } = left;

        /// <summary>
        /// Gets the operator used in the binary expression.
        /// </summary>
        public TokenType Operator { get; } = operatorType;

        /// <summary>
        /// Gets the right-hand side operand of the expression.
        /// </summary>
        public AstNode Right { get; } = right;

        /// <summary>True after validating a named resource comparison against contextual null.</summary>
        public bool IsResourceNullComparison { get; set; }

        /// <summary>
        /// Dispatches the visitor to the appropriate visit method for this node.
        /// </summary>
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

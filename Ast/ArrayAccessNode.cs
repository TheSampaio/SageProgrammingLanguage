using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents accessing an element of an array via an index. Example: cars[0]
    /// </summary>
    public class ArrayAccessNode(AstNode array, AstNode index) : AstNode
    {
        public AstNode Array { get; } = array;
        public AstNode Index { get; } = index;

        /// <summary>Validated fixed-array length; null for unchecked native pointer indexing.</summary>
        public int? CheckedLength { get; set; }

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

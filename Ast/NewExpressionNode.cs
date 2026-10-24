using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents a transitional <c>new T { field = value, ... }</c> heap allocation.
    /// It allocates and initializes a value of type <see cref="TypeName"/> on the heap and yields a
    /// local owning reference (no pointer arithmetic), released automatically at scope exit.
    /// </summary>
    /// <param name="typeName">The Sage type being allocated (e.g. a struct name).</param>
    /// <param name="fields">The field initializers, keyed by field name (may be empty).</param>
    public class NewExpressionNode(string typeName, Dictionary<string, AstNode> fields) : AstNode
    {
        /// <summary>Gets the type being heap-allocated.</summary>
        public string TypeName { get; } = typeName;

        /// <summary>Gets the field initializers for the allocated value.</summary>
        public Dictionary<string, AstNode> Fields { get; } = fields;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

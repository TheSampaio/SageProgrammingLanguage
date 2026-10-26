using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an opaque C type declared inside an extern block via <c>type Name;</c>
    /// (e.g. <c>type FILE;</c>). It introduces a type name that Sage can reference (typically
    /// through pointers such as <c>FILE*</c>) without knowing its layout; the name is emitted
    /// verbatim to C, where the included header provides the real definition.
    /// Inside an <c>extern cpp</c> block, <c>class Name = ns::Type;</c> declares an opaque handle
    /// to a C++ class; <see cref="CppClass"/> then holds the qualified C++ type.
    /// </summary>
    /// <param name="name">The opaque type name.</param>
    /// <param name="cppClass">The qualified C++ class for <c>extern cpp</c> handles; otherwise null.</param>
    public class ExternTypeNode(string name, string? cppClass = null) : AstNode
    {
        /// <summary>Gets the opaque type name.</summary>
        public string Name { get; } = name;

        /// <summary>Gets the qualified C++ class this handle refers to, if any.</summary>
        public string? CppClass { get; } = cppClass;

        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

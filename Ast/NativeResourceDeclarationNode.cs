using Sage.Interfaces;

namespace Sage.Ast;

/// <summary>Explicit native representation and destruction contract; does not create a resource value.</summary>
public sealed class NativeResourceDeclarationNode(string name, string nativeType, string destructorName) : AstNode
{
    public string Name { get; } = name;
    public string NativeType { get; } = nativeType;
    public string DestructorName { get; } = destructorName;
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

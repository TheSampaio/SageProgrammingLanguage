using Sage.Interfaces;

namespace Sage.Ast;

/// <summary>Explicit temporary access to a variable, without transferring ownership.</summary>
public sealed class BorrowExpressionNode(AstNode target, bool isMutable) : AstNode
{
    public AstNode Target { get; } = target;
    public bool IsMutable { get; } = isMutable;
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

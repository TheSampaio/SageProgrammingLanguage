using Sage.Interfaces;

namespace Sage.Ast;

/// <summary>
/// An extension-method call <c>receiver.name(args)</c>. Semantic analysis resolves it statically to one
/// opted-in module function and records the equivalent qualified call in <see cref="Lowered"/>.
/// </summary>
public sealed class MethodCallNode(AstNode receiver, string name, List<AstNode> arguments) : AstNode
{
    /// <summary>The value before the dot; it becomes the first argument.</summary>
    public AstNode Receiver { get; } = receiver;

    /// <summary>The method name written after the dot.</summary>
    public string Name { get; } = name;

    /// <summary>The explicit arguments that follow the receiver.</summary>
    public List<AstNode> Arguments { get; } = arguments;

    /// <summary>The validated <c>Module::name(receiver, args)</c> call that code generation emits.</summary>
    public FunctionCallNode? Lowered { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents an expression that invokes a function by name with a set of arguments.
    /// This can include both local functions and namespaced calls (e.g., math::sum).
    /// </summary>
    /// <param name="name">The name of the function being called, including any namespace prefixes.</param>
    /// <param name="arguments">The list of expression nodes passed as arguments to the function.</param>
    public class FunctionCallNode(string name, List<AstNode> arguments) : AstNode
    {
        /// <summary>
        /// Gets the name of the function to be invoked.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Gets the list of argument expressions for the function call.
        /// </summary>
        public List<AstNode> Arguments { get; } = arguments;

        public bool IsExternCall { get; set; } = false;

        /// <summary>Exact callable origin selected by semantic analysis.</summary>
        public string? ResolvedName { get; set; }

        /// <summary>
        /// Dispatches the visitor to the appropriate visit method for this function call.
        /// </summary>
        /// <typeparam name="T">The return type of the visitor's operation.</typeparam>
        /// <param name="visitor">An implementation of <see cref="IAstVisitor{T}"/>.</param>
        /// <returns>The result of the visitor's operation on this node.</returns>
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}

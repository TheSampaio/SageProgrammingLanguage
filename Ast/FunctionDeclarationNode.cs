using Sage.Interfaces;

namespace Sage.Ast
{
    /// <summary>
    /// Represents a function definition within the Sage language, including its signature, 
    /// parameters, return type, and implementation block.
    /// </summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="returnType">The data type returned by the function (e.g., i32, none).</param>
    /// <param name="parameters">The list of formal parameters defined for the function.</param>
    /// <param name="body">The block node containing the function's implementation.</param>
    /// <param name="moduleOwner">The optional name of the module that contains this function.</param>
    public class FunctionDeclarationNode(
        string name,
        string returnType,
        List<ParameterNode> parameters,
        BlockNode body,
        string moduleOwner = "") : AstNode
    {
        /// <summary>
        /// Gets the identifier name of the function.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Gets the Sage type name returned by the function.
        /// </summary>
        public string ReturnType { get; } = returnType;

        /// <summary>
        /// Gets the list of parameters required by this function.
        /// </summary>
        public List<ParameterNode> Parameters { get; } = parameters;

        /// <summary>
        /// Gets the code block containing the function's statements.
        /// </summary>
        public BlockNode Body { get; } = body;

        /// <summary>
        /// Gets or sets the name of the module scope this function belongs to.
        /// This is primarily used for C code generation to handle namespacing.
        /// </summary>
        public string ModuleOwner { get; set; } = moduleOwner;

        /// <summary>
        /// Gets a value indicating whether the function is defined externally (e.g., C interop).
        /// </summary>
        public bool IsExtern { get; set; } = false;

        /// <summary>
        /// Gets a value indicating whether the function is marked <c>unsafe</c>.
        /// The body runs in an unsafe context (raw pointers allowed) and callers must invoke it
        /// from within an unsafe context.
        /// </summary>
        public bool IsUnsafe { get; set; } = false;

        /// <summary>
        /// Dispatches the visitor to the appropriate visit method for this function declaration.
        /// </summary>
        /// <typeparam name="T">The return type of the visitor's operation.</typeparam>
        /// <param name="visitor">An implementation of <see cref="IAstVisitor{T}"/>.</param>
        /// <returns>The result of the visitor's operation on this node.</returns>
        public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
    }
}
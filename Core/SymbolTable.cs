namespace Sage.Core
{
    /// <summary>
    /// Manages the mapping of identifiers to their metadata across nested scopes.
    /// Implements a stack-based scoping mechanism to support block-level visibility 
    /// and variable shadowing.
    /// </summary>
    public class SymbolTable
    {
        /// <summary>
        /// A stack of dictionaries, where each dictionary represents a unique lexical scope.
        /// The top of the stack is the current (deepest) scope.
        /// </summary>
        private readonly Stack<Dictionary<string, SymbolMetadata>> _scopes = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolTable"/> class with a global scope.
        /// </summary>
        public SymbolTable() { EnterScope(); }

        /// <summary>
        /// Pushes a new, empty scope onto the stack. Call this when entering a function or code block.
        /// </summary>
        public void EnterScope() => _scopes.Push([]);

        /// <summary>
        /// Removes the current scope from the stack. Call this when exiting a function or code block.
        /// Note: The global scope cannot be popped.
        /// </summary>
        public void ExitScope() { if (_scopes.Count > 1) _scopes.Pop(); }

        /// <summary>
        /// Registers a new symbol in the current scope.
        /// </summary>
        /// <param name="name">The identifier name.</param>
        /// <param name="type">The Sage data type associated with the symbol.</param>
        /// <param name="isFunction">True if the symbol represents a callable function.</param>
        /// <param name="isExtern">True if the symbol is defined externally (C interop).</param>
        /// <param name="isConstant">True if the symbol is an immutable constant.</param>
        /// <param name="isReference">True if the symbol holds a safe heap reference (from <c>new</c>).</param>
        public void Define(
            string name,
            string type,
            bool isFunction = false,
            bool isExtern = false,
            bool isConstant = false,
            bool isReference = false)
        {
            if (_scopes.Count > 0)
            {
                _scopes.Peek()[name] = new SymbolMetadata(type, isFunction, isExtern, isConstant, isReference);
            }
        }

        /// <summary>
        /// Searches for a symbol by name, starting from the innermost scope and moving outward to the global scope.
        /// </summary>
        /// <param name="name">The identifier name to look up.</param>
        /// <returns>The <see cref="SymbolMetadata"/> if found; otherwise, <c>null</c>.</returns>
        public SymbolMetadata? Resolve(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var metadata)) return metadata;
            }
            return null;
        }

        /// <summary>
        /// Checks if a symbol is already declared specifically within the current (topmost) scope.
        /// Useful for preventing duplicate declarations within the same block.
        /// </summary>
        /// <param name="name">The identifier name to check.</param>
        /// <returns><c>true</c> if the symbol exists in the current scope; otherwise, <c>false</c>.</returns>
        public bool IsDefinedInCurrentScope(string name) => _scopes.Count > 0 && _scopes.Peek().ContainsKey(name);
    }
}
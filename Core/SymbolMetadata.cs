namespace Sage.Core
{
    /// <summary>
    /// Encapsulates the essential metadata for an identifier stored in the <see cref="SymbolTable"/>.
    /// This includes its data type, classification, linkage, mutability, and whether it holds a safe heap reference.
    /// </summary>
    /// <param name="type">The Sage data type name (e.g., "i32", "f64").</param>
    /// <param name="isFunction">Indicates whether the symbol represents a callable function.</param>
    /// <param name="isExtern">Indicates whether the symbol is an external C function (C interop).</param>
    /// <param name="isConstant">Indicates whether the symbol is an immutable constant.</param>
    /// <param name="isReference">Indicates whether the symbol holds a safe heap reference (produced by <c>new</c>).</param>
    public class SymbolMetadata(
        string type,
        bool isFunction,
        bool isExtern,
        bool isConstant = false,
        bool isReference = false)
    {
        /// <summary>Gets the Sage data type associated with this symbol.</summary>
        public string Type { get; } = type;

        /// <summary>Gets a value indicating whether this symbol is a function (otherwise a variable/constant).</summary>
        public bool IsFunction { get; } = isFunction;

        /// <summary>Gets a value indicating whether this symbol refers to an external C implementation.</summary>
        public bool IsExtern { get; } = isExtern;

        /// <summary>Gets a value indicating whether this symbol is a constant and cannot be reassigned.</summary>
        public bool IsConstant { get; } = isConstant;

        /// <summary>Gets a value indicating whether this symbol holds a safe heap reference (from <c>new</c>).</summary>
        public bool IsReference { get; } = isReference;
    }
}

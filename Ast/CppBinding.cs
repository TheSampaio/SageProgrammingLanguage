namespace Sage.Ast
{
    /// <summary>How a declared Sage signature reaches its C++ target through a generated adapter.</summary>
    public enum CppBindingKind
    {
        /// <summary>A namespace-qualified free function or explicit template instance.</summary>
        Function,
        /// <summary>A member function invoked on the first (class handle) parameter.</summary>
        Method,
        /// <summary><c>new Class(args)</c>, returning an owning class handle.</summary>
        Constructor,
        /// <summary><c>delete handle</c>; null handles are ignored.</summary>
        Destructor
    }

    /// <summary>
    /// The explicit C++ target of a function declared in an <c>extern cpp</c> block
    /// (e.g. <c>= demo::add</c>, <c>= .increment</c>, <c>= new</c>, <c>= delete</c>).
    /// </summary>
    /// <param name="kind">The adapter shape to generate.</param>
    /// <param name="target">The qualified C++ function or member name; empty for constructors and destructors.</param>
    public sealed class CppBinding(CppBindingKind kind, string target = "")
    {
        public CppBindingKind Kind { get; } = kind;
        public string Target { get; } = target;
    }
}

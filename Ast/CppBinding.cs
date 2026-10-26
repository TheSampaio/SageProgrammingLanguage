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
        Destructor,
        /// <summary><c>= catch</c>: reports whether the latest recoverable call on this thread failed.</summary>
        FailureStatus,
        /// <summary><c>= catch.message</c>: the text of the latest recoverable failure on this thread.</summary>
        FailureMessage
    }

    /// <summary>
    /// The explicit C++ target of a function declared in an <c>extern cpp</c> block
    /// (e.g. <c>= demo::add</c>, <c>= .increment</c>, <c>= new</c>, <c>= delete</c>).
    /// </summary>
    /// <param name="kind">The adapter shape to generate.</param>
    /// <param name="target">The qualified C++ function or member name; empty for constructors, destructors, and failure queries.</param>
    /// <param name="recoverable">True when a trailing <c>catch</c> asks for exceptions to be reported through the failure queries instead of aborting.</param>
    public sealed class CppBinding(CppBindingKind kind, string target = "", bool recoverable = false)
    {
        public CppBindingKind Kind { get; } = kind;
        public string Target { get; } = target;

        /// <summary>True when an escaping exception (or a null handle) yields a zero result and a recorded failure instead of an abort.</summary>
        public bool Recoverable { get; } = recoverable;
    }
}

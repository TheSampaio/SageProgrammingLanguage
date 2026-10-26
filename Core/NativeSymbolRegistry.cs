using Sage.Ast;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Tracks the C symbols produced by Sage functions (flattened <c>module_function</c> names), declared C
    /// externs, and generated C++ adapters, so two different declarations that would link as one symbol are
    /// rejected (S212) before any C is generated. Only declared symbols are known: names defined inside
    /// project native sources or linked libraries cannot be seen without parsing them.
    /// </summary>
    internal sealed class NativeSymbolRegistry
    {
        // Several aliases may declare the same C function; that is one native symbol, not a collision.
        private readonly Dictionary<string, string> _sage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _native = new(StringComparer.Ordinal);

        /// <summary>Records the symbol of a Sage-defined function and rejects a clash with any other declaration.</summary>
        public void RegisterSage(FunctionDeclarationNode function, string sageName)
        {
            string? symbol = SymbolOf(function);
            if (symbol == null) return;
            if (_native.TryGetValue(symbol, out string? existing) || (_sage.TryGetValue(symbol, out existing) && existing != sageName))
                throw Collision(function, symbol, sageName, existing!);
            _sage[symbol] = sageName;
        }

        /// <summary>Records the symbol of an extern C function or C++ adapter and rejects a clash with a Sage function.</summary>
        public void RegisterNative(FunctionDeclarationNode function, string sageName)
        {
            string? symbol = SymbolOf(function);
            if (symbol == null) return;
            if (_sage.TryGetValue(symbol, out string? existing)) throw Collision(function, symbol, sageName, existing);
            _native.TryAdd(symbol, sageName);
        }

        // Every function named 'main' lowers to C 'main', so a module 'main' collides with the entry point.
        private static string? SymbolOf(FunctionDeclarationNode function) => CNaming.ResolveFunctionName(function);

        private static CompilerException Collision(FunctionDeclarationNode function, string symbol, string sageName, string existing) =>
            new(function, "S212", $"Native symbol collision: '{sageName}' and '{existing}' both link as C symbol '{symbol}'. Rename one declaration.");
    }
}

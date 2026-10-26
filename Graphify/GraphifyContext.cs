using Sage.Ast;
using Sage.Core;

namespace Sage.Graphify
{
    /// <summary>A function declaration located in a module, with the name calls use to resolve to it.</summary>
    internal sealed record DeclaredFunction(FunctionDeclarationNode Node, string Qualified, string? Namespace, ExternBlockNode? Extern);

    /// <summary>
    /// Project-wide declarations that per-module indexing needs: where callables and types are declared, and the
    /// struct table behind copy and resource capability inference.
    /// </summary>
    internal sealed class GraphifyContext
    {
        private readonly Dictionary<string, string> _functionOrigins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _typeOrigins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StructDeclarationNode> _structs = new(StringComparer.Ordinal);

        public GraphifyContext(IEnumerable<LoadedModule> modules)
        {
            foreach (var module in modules)
            {
                foreach (var function in EnumerateFunctions(module.Ast))
                    _functionOrigins.TryAdd(function.Qualified, module.Name);
                foreach (var declaration in EnumerateDeclarations(module.Ast))
                {
                    switch (declaration)
                    {
                        case StructDeclarationNode structure:
                            _structs.TryAdd(structure.Name, structure);
                            _typeOrigins.TryAdd(structure.Name, module.Name);
                            break;
                        case NativeResourceDeclarationNode resource: _typeOrigins.TryAdd(resource.Name, module.Name); break;
                        case ExternTypeNode opaque: _typeOrigins.TryAdd(opaque.Name, module.Name); break;
                    }
                }
            }
        }

        public IReadOnlyDictionary<string, StructDeclarationNode> Structs => _structs;

        /// <summary>The module declaring a callable (by the name calls resolve to), or null when unknown.</summary>
        public string? FunctionOrigin(string qualified) => _functionOrigins.GetValueOrDefault(qualified);

        /// <summary>The module declaring a struct, resource, or opaque type, or null when the name is not a declared type.</summary>
        public string? TypeOrigin(string name) => _typeOrigins.GetValueOrDefault(name);

        /// <summary>True when a struct directly or transitively owns native resources.</summary>
        public bool OwnsResources(string structName) => OwnsResources(structName, new HashSet<string>(StringComparer.Ordinal));

        private bool OwnsResources(string structName, HashSet<string> path)
        {
            if (!_structs.TryGetValue(structName, out var structure) || !path.Add(structName)) return false;
            bool owns = structure.Fields.Any(field => TypeSystem.IsNativeResource(field.Type) ||
                (!TypeSystem.IsArrayType(field.Type) && OwnsResources(field.Type, path)));
            path.Remove(structName);
            return owns;
        }

        /// <summary>Classifies how a parameter of the given type constrains its caller.</summary>
        public string ParameterContract(string type)
        {
            if (TypeSystem.IsBorrowType(type))
                return TypeSystem.GetBorrowKind(type) == BorrowKind.Mutable ? "borrow_mut" : "borrow";
            // Native resources and structs owning them move into the callee.
            if (TypeSystem.IsNativeResource(type) || OwnsResources(type)) return "consume";
            if (TypeSystem.IsRawPointer(type)) return "raw_pointer";
            return TypeCapabilities.IsCopyable(type, _structs) ? "copy" : "value";
        }

        /// <summary>Lists every function declared in a module, including functions of module blocks and extern blocks.</summary>
        public static IEnumerable<DeclaredFunction> EnumerateFunctions(ProgramNode ast)
        {
            foreach (var statement in ast.Statements)
            {
                switch (statement)
                {
                    case FunctionDeclarationNode function:
                        yield return new DeclaredFunction(function, function.Name, null, null);
                        break;
                    case ExternBlockNode external:
                        foreach (var native in ExternFunctions(external)) yield return native;
                        break;
                    case ModuleNode module:
                        foreach (var member in module.Members)
                        {
                            if (member is FunctionDeclarationNode function)
                                yield return new DeclaredFunction(function, $"{module.Name}::{function.Name}", module.Name, null);
                            else if (member is ExternBlockNode external)
                                foreach (var native in ExternFunctions(external)) yield return native;
                        }
                        break;
                }
            }
        }

        private static IEnumerable<DeclaredFunction> ExternFunctions(ExternBlockNode external) =>
            external.Declarations.OfType<FunctionDeclarationNode>()
                .Select(function => new DeclaredFunction(function, $"{external.Alias}::{function.Name}", null, external));

        /// <summary>Lists type declarations of a module, including those inside module and extern blocks.</summary>
        private static IEnumerable<AstNode> EnumerateDeclarations(ProgramNode ast)
        {
            foreach (var statement in ast.Statements)
            {
                switch (statement)
                {
                    case StructDeclarationNode structure: yield return structure; break;
                    case ExternBlockNode external:
                        foreach (var declaration in external.Declarations) yield return declaration;
                        break;
                    case ModuleNode module:
                        foreach (var external in module.Members.OfType<ExternBlockNode>())
                            foreach (var declaration in external.Declarations) yield return declaration;
                        break;
                }
            }
        }
    }
}

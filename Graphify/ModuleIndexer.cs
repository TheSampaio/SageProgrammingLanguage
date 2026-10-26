using System.Collections.Concurrent;
using System.Reflection;
using Sage.Ast;
using Sage.Core;

namespace Sage.Graphify
{
    /// <summary>
    /// Derives a module's semantic index from its analyzed AST. Call targets come from the origins the semantic
    /// analyzer resolved; parameter contracts come from the same type helpers the analyzer enforces.
    /// </summary>
    internal sealed class ModuleIndexer(GraphifyContext context)
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildProperties = new();

        private readonly HashSet<AstNode> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<(string, string, int, int, string?)> _referenceKeys = [];
        private readonly HashSet<string> _referencedModules = new(StringComparer.Ordinal);
        private readonly List<ReferenceEntry> _references = [];
        private readonly List<FunctionEntry> _functions = [];
        private readonly List<StructEntry> _structs = [];
        private readonly List<ResourceEntry> _resources = [];
        private readonly List<VariableEntry> _variables = [];
        private readonly List<ExternEntry> _externs = [];
        private readonly List<NamespaceEntry> _namespaces = [];
        private readonly List<UseEntry> _uses = [];
        private string? _function;
        private string _module = "";

        /// <summary>Modules (other than the indexed one) whose declarations this module's index depends on.</summary>
        public IReadOnlySet<string> ReferencedModules => _referencedModules;

        /// <summary>Builds the shard content for one module; the caller assigns the cache key and dependency hash.</summary>
        public ModuleShard Build(LoadedModule module, string relativeFile)
        {
            _module = module.Name;
            Walk(module.Ast, null, null);
            _referencedModules.Remove(module.Name);

            return new ModuleShard(
                Schema: 1,
                Module: module.Name,
                File: relativeFile,
                Origin: module.IsStd ? "std" : "project",
                Key: "",
                Deps: "",
                Uses: NullIfEmpty(_uses),
                Namespaces: NullIfEmpty(_namespaces),
                Externs: NullIfEmpty(_externs),
                Structs: NullIfEmpty(_structs),
                Resources: NullIfEmpty(_resources),
                Variables: NullIfEmpty(_variables),
                Functions: NullIfEmpty(_functions),
                References: NullIfEmpty(_references.OrderBy(r => r.Line).ThenBy(r => r.Column)
                    .ThenBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ToList()));
        }

        private static List<T>? NullIfEmpty<T>(List<T> list) => list.Count == 0 ? null : list;

        private void Walk(AstNode node, string? @namespace, ExternBlockNode? external)
        {
            if (!_visited.Add(node)) return;
            switch (node)
            {
                case UseNode use:
                    _uses.Add(new UseEntry(use.Module, use.Line, use.Column));
                    return;
                case ModuleNode module:
                    _namespaces.Add(new NamespaceEntry(module.Name, module.Line, module.Column));
                    foreach (var member in module.Members) Walk(member, module.Name, null);
                    return;
                case ExternBlockNode block:
                    IndexExternBlock(block);
                    foreach (var declaration in block.Declarations) Walk(declaration, null, block);
                    return;
                case FunctionDeclarationNode function:
                    IndexFunction(function, @namespace, external);
                    return;
                case StructDeclarationNode structure:
                    IndexStruct(structure);
                    return;
                case NativeResourceDeclarationNode resource:
                    IndexResource(resource, external);
                    return;
                case VariableDeclarationNode variable:
                    if (_function == null)
                        _variables.Add(new VariableEntry(variable.Name, variable.Type, variable.IsConstant, variable.Line, variable.Column));
                    AddTypeReferences(variable.Type, variable);
                    break;
                case FunctionCallNode call:
                    IndexCall(call);
                    break;
                case MemberAccessNode access:
                    IndexFieldAccess(access);
                    break;
                case CastExpressionNode cast:
                    AddTypeReferences(cast.TargetType, cast);
                    break;
                case NewExpressionNode allocation:
                    AddTypeReferences(allocation.TypeName, allocation);
                    break;
            }
            foreach (var child in Children(node)) Walk(child, @namespace, external);
        }

        private void IndexExternBlock(ExternBlockNode block)
        {
            var types = block.Declarations.OfType<ExternTypeNode>().Select(type => type.Name).ToList();
            _externs.Add(new ExternEntry(block.Alias, block.Header, block.Library, block.Line, block.Column, NullIfEmpty(types)));
        }

        private void IndexFunction(FunctionDeclarationNode function, string? @namespace, ExternBlockNode? external)
        {
            string qualified = external != null ? $"{external.Alias}::{function.Name}"
                : @namespace != null ? $"{@namespace}::{function.Name}" : function.Name;
            bool isExtern = external != null || function.IsExtern;
            string parameters = string.Join(", ", function.Parameters.Select(p => $"{p.Name}: {p.Type}"));
            string modifier = isExtern ? "extern " : function.IsUnsafe ? "unsafe " : "";
            string returnContract = function.ReturnType == "none" ? "none"
                : TypeSystem.IsNativeResource(function.ReturnType) ? "owner" : "value";

            _functions.Add(new FunctionEntry(
                function.Name, qualified, @namespace ?? external?.Alias, function.Line, function.Column,
                $"{modifier}func {function.Name}({parameters}): {function.ReturnType}",
                function.ReturnType, returnContract, function.IsUnsafe || isExtern, isExtern,
                external?.Header, external?.Library,
                NullIfEmpty(function.Parameters.Select(p => new ParameterEntry(p.Name, p.Type, context.ParameterContract(p.Type))).ToList())));

            AddTypeReferences(function.ReturnType, function, qualified);
            foreach (var parameter in function.Parameters) AddTypeReferences(parameter.Type, function, qualified);

            if (function.Body == null) return;
            string? previous = _function;
            _function = qualified;
            try { Walk(function.Body, @namespace, external); }
            finally { _function = previous; }
        }

        private void IndexStruct(StructDeclarationNode structure)
        {
            var fields = structure.Fields.Select(f => new FieldEntry(f.Name, f.Type, f.Line, f.Column)).ToList();
            _structs.Add(new StructEntry(structure.Name, structure.Line, structure.Column,
                TypeCapabilities.IsCopyable(structure.Name, context.Structs), context.OwnsResources(structure.Name), NullIfEmpty(fields)));
            foreach (var field in structure.Fields) AddTypeReferences(field.Type, field);
        }

        private void IndexResource(NativeResourceDeclarationNode resource, ExternBlockNode? external) =>
            _resources.Add(new ResourceEntry(resource.Name, resource.NativeType,
                external != null ? $"{external.Alias}::{resource.DestructorName}" : resource.DestructorName,
                external?.Header, external?.Library, resource.Line, resource.Column));

        private void IndexCall(FunctionCallNode call)
        {
            string name = call.ResolvedName ?? call.Name;
            string? origin = context.FunctionOrigin(name);
            if (origin != null) _referencedModules.Add(origin);
            AddReference(new ReferenceEntry(name, "call", call.Line, call.Column, _function, origin, call.IsExternCall));
        }

        private void IndexFieldAccess(MemberAccessNode access)
        {
            string? owner = StructNameOf(access.Object.VariableType);
            if (owner == null) return;
            if (context.TypeOrigin(owner) is { } origin) _referencedModules.Add(origin);
            AddReference(new ReferenceEntry($"{owner}.{access.PropertyName}", "field", access.Line, access.Column, _function, null, false));
        }

        private string? StructNameOf(string? type)
        {
            if (type == null) return null;
            string name = TypeBaseName(type);
            return context.Structs.ContainsKey(name) ? name : null;
        }

        /// <summary>Records references to declared structs, resources, and opaque types named by a type string.</summary>
        private void AddTypeReferences(string type, AstNode location, string? enclosing = null)
        {
            string name = TypeBaseName(type);
            if (context.TypeOrigin(name) is not { } origin) return;
            _referencedModules.Add(origin);
            AddReference(new ReferenceEntry(name, "type", location.Line, location.Column, enclosing ?? _function, origin, false));
        }

        private void AddReference(ReferenceEntry reference)
        {
            if (_referenceKeys.Add((reference.Name, reference.Kind, reference.Line, reference.Column, reference.In)))
                _references.Add(reference);
        }

        /// <summary>Strips borrow markers, pointer stars, and array lengths: <c>&amp;mut Point[4]*</c> becomes <c>Point</c>.</summary>
        internal static string TypeBaseName(string type)
        {
            string name = TypeSystem.GetBorrowedType(type);
            int bracket = name.IndexOf('[');
            if (bracket >= 0) name = name[..bracket];
            return name.TrimEnd('*');
        }

        /// <summary>
        /// Enumerates child nodes by shape rather than node type, so nodes added to the language later are traversed
        /// without changing the indexer.
        /// </summary>
        private static IEnumerable<AstNode> Children(AstNode node)
        {
            foreach (var property in ChildProperties.GetOrAdd(node.GetType(), DiscoverChildProperties))
            {
                switch (property.GetValue(node))
                {
                    case AstNode child: yield return child; break;
                    case IEnumerable<AstNode> list:
                        foreach (var item in list) if (item != null) yield return item;
                        break;
                    case IEnumerable<KeyValuePair<string, AstNode>> map:
                        foreach (var item in map) if (item.Value != null) yield return item.Value;
                        break;
                }
            }
        }

        private static PropertyInfo[] DiscoverChildProperties(Type type) => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.DeclaringType != typeof(AstNode) &&
                (typeof(AstNode).IsAssignableFrom(p.PropertyType) ||
                 typeof(IEnumerable<AstNode>).IsAssignableFrom(p.PropertyType) ||
                 typeof(IEnumerable<KeyValuePair<string, AstNode>>).IsAssignableFrom(p.PropertyType)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();
    }
}

using Sage.Ast;

namespace Sage.Core
{
    /// <summary>Infers the checked value-copy subset from type composition, without guessing native resource ownership.</summary>
    public static class TypeCapabilities
    {
        public static bool IsCopyable(string type, IReadOnlyDictionary<string, StructDeclarationNode> structures) =>
            IsCopyable(type, structures, new HashSet<string>(StringComparer.Ordinal));

        private static bool IsCopyable(string type, IReadOnlyDictionary<string, StructDeclarationNode> structures, HashSet<string> path)
        {
            if (TypeSystem.IsNumeric(type) || type is "b8" or "c_bool") return true;
            if (TypeSystem.IsArrayType(type) && !TypeSystem.IsBorrowType(type))
                return TypeSystem.GetArraySize(type) > 0 && IsCopyable(TypeSystem.GetArrayBaseType(type), structures, path);
            if (!structures.TryGetValue(type, out var structure) || !path.Add(type)) return false;
            bool copyable = structure.Fields.All(field => IsCopyable(field.Type, structures, path));
            path.Remove(type);
            return copyable;
        }
    }
}

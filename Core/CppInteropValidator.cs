using System.Text.RegularExpressions;
using Sage.Ast;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Validates the explicit contracts of an <c>extern cpp</c> block before any adapter is generated.
    /// Only shapes with a well-defined C ABI are accepted; everything else is rejected with S210
    /// instead of being guessed.
    /// </summary>
    internal static partial class CppInteropValidator
    {
        // Qualified names with an optional explicit template argument list. No characters that could
        // end a C++ expression or statement are accepted, so targets are safe to emit verbatim.
        [GeneratedRegex(@"^[A-Za-z_]\w*(::[A-Za-z_]\w*)*(<[A-Za-z0-9_:, *&<>]+>)?$")]
        private static partial Regex QualifiedName();

        /// <summary>Validates every class handle and function binding of a C++ block.</summary>
        public static void Validate(ExternBlockNode block)
        {
            var classes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in block.Declarations.OfType<ExternTypeNode>())
            {
                if (handle.CppClass == null || !QualifiedName().IsMatch(handle.CppClass))
                    throw new CompilerException(handle, "S210", $"C++ class handle '{handle.Name}' requires a qualified C++ type name.");
                if (!classes.Add(handle.Name))
                    throw new CompilerException(handle, "S150", $"C++ class handle '{handle.Name}' is already declared.");
            }
            foreach (var function in block.Declarations.OfType<FunctionDeclarationNode>())
                ValidateFunction(function, classes);
        }

        /// <summary>True when <paramref name="type"/> is a single pointer to a class handle of the block.</summary>
        public static bool IsHandle(string type, ICollection<string> classes, out string handle)
        {
            handle = type.EndsWith('*') ? type[..^1] : "";
            return classes.Contains(handle);
        }

        private static void ValidateFunction(FunctionDeclarationNode function, ICollection<string> classes)
        {
            var binding = function.CppBinding!;
            string bindingName = $"{function.ModuleOwner}::{function.Name}";
            foreach (var parameter in function.Parameters)
                if (parameter.Type == "none" || !IsAdapterType(parameter.Type, classes))
                    throw new CompilerException(function, "S210", $"Parameter '{parameter.Name}' of '{bindingName}' has no C++ adapter mapping: '{parameter.Type}'.");
            if (!IsAdapterType(function.ReturnType, classes))
                throw new CompilerException(function, "S210", $"Return type of '{bindingName}' has no C++ adapter mapping: '{function.ReturnType}'.");

            bool firstIsHandle = function.Parameters.Count > 0 && IsHandle(function.Parameters[0].Type, classes, out _);
            switch (binding.Kind)
            {
                case CppBindingKind.Function when !QualifiedName().IsMatch(binding.Target):
                    throw new CompilerException(function, "S210", $"'{bindingName}' requires a qualified C++ function name, got '{binding.Target}'.");
                case CppBindingKind.Constructor when !IsHandle(function.ReturnType, classes, out _):
                    throw new CompilerException(function, "S210", $"Constructor '{bindingName}' must return a class handle pointer declared in the same block.");
                case CppBindingKind.Destructor when function.Parameters.Count != 1 || !firstIsHandle || function.ReturnType != "none":
                    throw new CompilerException(function, "S210", $"Destructor '{bindingName}' must take one class handle pointer and return none.");
                case CppBindingKind.Method when !firstIsHandle:
                    throw new CompilerException(function, "S210", $"Member binding '{bindingName}' must take its class handle pointer as the first parameter.");
            }
        }

        /// <summary>
        /// Scalars, <c>str</c>, raw pointers to scalars, and single pointers to the block's class handles
        /// cross the adapter unchanged. Structs, arrays, borrows, and foreign handles have no adapter ABI yet.
        /// </summary>
        private static bool IsAdapterType(string type, ICollection<string> classes)
        {
            if (IsHandle(type, classes, out _)) return true;
            string pointee = type.TrimEnd('*');
            if (classes.Contains(pointee) || TypeSystem.IsBorrowType(type) || TypeSystem.IsArrayType(type)) return false;
            return pointee is "none" or "b8" or "str" || TypeSystem.IsNumeric(pointee) || TypeSystem.IsCInteropType(pointee);
        }
    }
}

using System.Text;
using Sage.Ast;

namespace Sage.Core
{
    /// <summary>
    /// Generates the C++ translation unit that exposes <c>extern cpp</c> declarations to the C backend.
    /// Each adapter has C linkage, converts class handles to their C++ types, and contains C++
    /// exceptions: none may unwind into C frames, so an escaping exception terminates the process
    /// with a diagnostic naming the Sage binding. A binding that opts in with <c>catch</c> instead records the failure
    /// in thread-local state, returns a zero value, and is queried through <c>= catch</c> / <c>= catch.message</c>.
    /// </summary>
    public sealed class CppAdapterGenerator
    {
        /// <summary>File name of the generated adapter translation unit inside <c>obj/</c>.</summary>
        public const string FileName = "sage_cpp_adapters.cpp";

        /// <summary>Generates adapters for the given blocks; identical bindings from several modules are emitted once.</summary>
        public static string Generate(IEnumerable<ExternBlockNode> blocks)
        {
            var cppBlocks = blocks.Where(block => block.IsCpp).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("/* --- Sage C++ interop adapters (generated) --- */");
            sb.AppendLine($"#include \"{HeaderGenerator.PreludeFileName}\"");
            sb.AppendLine("#include <cstddef>");
            sb.AppendLine("#include <cstdio>");
            sb.AppendLine("#include <cstdlib>");
            sb.AppendLine("#include <exception>");
            foreach (var header in cppBlocks.Select(block => HeaderGenerator.IncludeTarget(block.Header)).Distinct().Order(StringComparer.Ordinal))
                sb.AppendLine($"#include {header}");
            sb.AppendLine();
            sb.AppendLine("[[noreturn]] static void sage_cpp_fail(const char* binding, const char* reason) {");
            sb.AppendLine("    std::fprintf(stderr, \"Sage C++ interop: %s: %s\\n\", binding, reason);");
            sb.AppendLine("    std::abort();");
            sb.AppendLine("}");
            if (cppBlocks.SelectMany(block => block.Declarations.OfType<FunctionDeclarationNode>()).Any(UsesFailureState))
            {
                // Fixed storage: recording a failure must not allocate while handling an exception.
                sb.AppendLine("static thread_local bool sage_cpp_failed = false;");
                sb.AppendLine("static thread_local char sage_cpp_failure[256] = \"\";");
                sb.AppendLine("static void sage_cpp_reset() { sage_cpp_failed = false; sage_cpp_failure[0] = '\\0'; }");
                sb.AppendLine("static void sage_cpp_record(const char* binding, const char* reason) {");
                sb.AppendLine("    sage_cpp_failed = true;");
                sb.AppendLine("    std::snprintf(sage_cpp_failure, sizeof sage_cpp_failure, \"%s: %s\", binding, reason);");
                sb.AppendLine("}");
            }

            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var block in cppBlocks)
            {
                var classes = block.Declarations.OfType<ExternTypeNode>()
                    .ToDictionary(handle => handle.Name, handle => handle.CppClass!, StringComparer.Ordinal);
                foreach (var handle in classes.Keys)
                {
                    string tag = CNaming.CppHandleTag(block.Alias, handle);
                    if (emitted.Add(tag)) sb.AppendLine($"struct {tag};");
                }
                foreach (var function in block.Declarations.OfType<FunctionDeclarationNode>())
                {
                    string symbol = CNaming.ResolveFunctionName(function);
                    if (emitted.Add(symbol)) sb.Append(Adapter(block.Alias, function, symbol, classes));
                }
            }
            return sb.ToString();
        }

        private static bool UsesFailureState(FunctionDeclarationNode function) =>
            function.CppBinding!.Recoverable || function.CppBinding.Kind is CppBindingKind.FailureStatus or CppBindingKind.FailureMessage;

        private static string Adapter(string alias, FunctionDeclarationNode function, string symbol, Dictionary<string, string> classes)
        {
            var binding = function.CppBinding!;
            // Generated parameter names cannot collide with C++ keywords or the target's names.
            var parameters = function.Parameters.Select((parameter, index) => (Name: $"a{index}", parameter.Type)).ToList();
            string signature = string.Join(", ", parameters.Select(parameter => $"{AbiType(alias, parameter.Type, classes)} {parameter.Name}"));
            var arguments = parameters.Select(parameter => ToCpp(parameter.Name, parameter.Type, classes)).ToList();
            string bindingName = $"{alias}::{function.Name}";
            bool returnsValue = function.ReturnType is not ("none" or "c_void");

            string abiReturn = AbiType(alias, function.ReturnType, classes);
            string abiSignature = signature.Length == 0 ? "void" : signature;
            if (binding.Kind is CppBindingKind.FailureStatus or CppBindingKind.FailureMessage)
            {
                string state = binding.Kind == CppBindingKind.FailureStatus ? "sage_cpp_failed" : "sage_cpp_failure";
                return $"\nextern \"C\" {abiReturn} {symbol}(void) {{ return {state}; }}\n";
            }

            // A recoverable binding reports a null handle like an exception: zero result, failure recorded.
            string nullHandle = binding.Recoverable
                ? $"{{ sage_cpp_record(\"{bindingName}\", \"null class handle\"); return{(returnsValue ? " {}" : "")}; }}"
                : $"sage_cpp_fail(\"{bindingName}\", \"null class handle\");";
            var body = new StringBuilder();
            switch (binding.Kind)
            {
                case CppBindingKind.Function:
                    body.AppendLine(Return($"{binding.Target}({string.Join(", ", arguments)})"));
                    break;
                case CppBindingKind.Method:
                    body.AppendLine($"        if ({parameters[0].Name} == nullptr) {nullHandle}");
                    body.AppendLine(Return($"{arguments[0]}->{binding.Target}({string.Join(", ", arguments.Skip(1))})"));
                    break;
                case CppBindingKind.Constructor:
                    body.AppendLine(Return($"new {classes[function.ReturnType[..^1]]}({string.Join(", ", arguments)})"));
                    break;
                case CppBindingKind.Destructor:
                    body.AppendLine($"        delete {arguments[0]};");
                    break;
            }

            string reset = binding.Recoverable ? "    sage_cpp_reset();\n" : "";
            string onStd = binding.Recoverable ? $"sage_cpp_record(\"{bindingName}\", error.what());" : $"sage_cpp_fail(\"{bindingName}\", error.what());";
            string onAny = binding.Recoverable ? $"sage_cpp_record(\"{bindingName}\", \"unknown C++ exception\");" : $"sage_cpp_fail(\"{bindingName}\", \"unknown C++ exception\");";
            string recover = binding.Recoverable ? (returnsValue ? "\n        return {};" : "\n        return;") : "";

            return $$"""

                extern "C" {{abiReturn}} {{symbol}}({{abiSignature}}) {
                {{reset}}    try {
                {{body}}    } catch (const std::exception& error) {
                        {{onStd}}{{recover}}
                    } catch (...) {
                        {{onAny}}{{recover}}
                    }
                }

                """;

            string Return(string call) => returnsValue
                ? $"        return {FromCpp(call, alias, function.ReturnType, classes)};"
                : $"        {call};";
        }

        /// <summary>The C ABI type of an adapter value; class handles use their incomplete struct tag.</summary>
        private static string AbiType(string alias, string type, Dictionary<string, string> classes) =>
            IsHandle(type, classes, out string handle) ? $"{CNaming.CppHandleTag(alias, handle)}*" : TypeSystem.ToCType(type);

        private static string ToCpp(string name, string type, Dictionary<string, string> classes) =>
            IsHandle(type, classes, out string handle) ? $"reinterpret_cast<{classes[handle]}*>({name})" : name;

        private static string FromCpp(string expression, string alias, string type, Dictionary<string, string> classes) =>
            IsHandle(type, classes, out string handle) ? $"reinterpret_cast<{CNaming.CppHandleTag(alias, handle)}*>({expression})" : expression;

        private static bool IsHandle(string type, Dictionary<string, string> classes, out string handle) =>
            CppInteropValidator.IsHandle(type, classes.Keys, out handle);
    }
}

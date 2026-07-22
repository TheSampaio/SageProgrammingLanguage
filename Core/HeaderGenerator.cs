using System.Text;
using Sage.Ast;
using Sage.Interfaces;

namespace Sage.Core
{
    /// <summary>
    /// Traverses the Sage AST to generate C-compatible header files (.h),
    /// including type definitions, forward declarations, and required includes.
    /// </summary>
    public class HeaderGenerator : IAstVisitor<string>
    {
        private readonly HashSet<string> _requiredHeaders = [];

        /// <summary>
        /// Generates the full content of a C header file based on the provided AST.
        /// </summary>
        /// <param name="ast">The root program node.</param>
        /// <returns>A string containing the complete header file code.</returns>
        public string Generate(ProgramNode ast)
        {
            _requiredHeaders.Clear();

            // Collect all declarations and dependencies by visiting the AST
            string declarations = ast.Accept(this);

            var sb = new StringBuilder();
            sb.AppendLine("/* --- Sage Header File --- */");
            sb.AppendLine("#pragma once");

            // The shared runtime prelude carries the Sage type aliases and base C headers,
            // so each module header includes it once instead of re-emitting typedefs.
            sb.AppendLine($"#include \"{PreludeFileName}\"");

            // Emit required headers in a stable, deterministic order.
            foreach (var header in _requiredHeaders.OrderBy(h => h, StringComparer.Ordinal))
            {
                sb.AppendLine($"#include {header}");
            }

            sb.AppendLine();
            sb.AppendLine("/* --- Declarations --- */");
            sb.Append(declarations);

            return sb.ToString();
        }

        /// <summary>File name of the shared Sage runtime prelude header.</summary>
        public const string PreludeFileName = "sage.h";

        /// <summary>
        /// Produces the shared runtime prelude that every generated module header includes.
        /// It defines the Sage primitive type aliases exactly once and pulls in the base C headers.
        /// </summary>
        public static string GeneratePrelude()
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* --- Sage Runtime Prelude (generated) --- */");
            sb.AppendLine("#pragma once");
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdbool.h>");
            sb.AppendLine();
            sb.AppendLine("#ifndef SAGE_TYPES_DEFINED");
            sb.AppendLine("#define SAGE_TYPES_DEFINED");
            sb.AppendLine("typedef int8_t   i8;  typedef uint8_t  u8;");
            sb.AppendLine("typedef int16_t  i16; typedef uint16_t u16;");
            sb.AppendLine("typedef int32_t  i32; typedef uint32_t u32;");
            sb.AppendLine("typedef int64_t  i64; typedef uint64_t u64;");
            sb.AppendLine("typedef float    f32; typedef double   f64;");
            sb.AppendLine("typedef bool     b8;  typedef char* str;");
            sb.AppendLine("typedef void     none;");
            sb.AppendLine("#endif");
            return sb.ToString();
        }

        /// <summary>
        /// Normalizes and adds a header file to the inclusion list.
        /// </summary>
        private void AddHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return;

            string normalized = (header.StartsWith('<') || header.StartsWith('"'))
                ? header
                : $"<{header}>";

            _requiredHeaders.Add(normalized);
        }

        public string Visit(ProgramNode node)
        {
            var sb = new StringBuilder();
            foreach (var stmt in node.Statements)
            {
                string res = stmt.Accept(this);
                if (!string.IsNullOrEmpty(res)) sb.Append(res);
            }
            return sb.ToString();
        }

        public string Visit(ModuleNode node)
        {
            var sb = new StringBuilder();
            foreach (var member in node.Members)
            {
                if (member is FunctionDeclarationNode func) func.ModuleOwner = node.Name;
                string res = member.Accept(this);
                if (!string.IsNullOrEmpty(res)) sb.Append(res);
            }
            return sb.ToString();
        }

        public string Visit(FunctionDeclarationNode node)
        {
            // Visit body to discover dependencies within the function scope
            if (node.Body != null) node.Body.Accept(this);

            string cName = CNaming.ResolveFunctionName(node);

            bool isMain = node.Name.Equals("main", StringComparison.OrdinalIgnoreCase);
            string cReturnType = isMain ? "int" : TypeSystem.ToCType(node.ReturnType);

            string paramsStr = (isMain || node.Parameters.Count == 0)
                ? "void"
                : string.Join(", ", node.Parameters.Select(p => TypeSystem.FormatCParameter(p.Name, p.Type)));

            string prefix = node.IsExtern ? "extern " : "";
            return $"{prefix}{cReturnType} {cName}({paramsStr});\n";
        }

        public string Visit(StructDeclarationNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"typedef struct {node.Name} {{");
            foreach (var field in node.Fields)
            {
                sb.AppendLine($"    {TypeSystem.ToCType(field.Type)} {field.Name};");
            }
            sb.AppendLine($"}} {node.Name};\n");
            return sb.ToString();
        }

        public string Visit(ExternBlockNode node)
        {
            AddHeader(node.Header);
            foreach (var decl in node.Declarations) decl.Accept(this);
            return "";
        }

        // Opaque C types are provided by their extern header; nothing is emitted for them.
        public string Visit(ExternTypeNode node) => "";

        public string Visit(UnsafeBlockNode node) { node.Body.Accept(this); return ""; }

        // 'new' and 'delete' lower to malloc/free, which require stdlib.h.
        public string Visit(NewExpressionNode node)
        {
            AddHeader("stdlib.h");
            foreach (var value in node.Fields.Values) value.Accept(this);
            return "";
        }

        public string Visit(DeleteNode node)
        {
            AddHeader("stdlib.h");
            node.Target.Accept(this);
            return "";
        }

        public string Visit(UseNode node)
        {
            AddHeader($"\"{node.Module}.h\"");
            return "";
        }

        public string Visit(FunctionCallNode node)
        {
            foreach (var arg in node.Arguments) arg.Accept(this);
            return "";
        }

        public string Visit(InterpolatedStringNode node)
        {
            // String interpolation is transpiled to snprintf, requiring stdio.h
            AddHeader("stdio.h");
            foreach (var part in node.Parts) part.Accept(this);
            return "";
        }

        // --- AST Traversal Implementation ---
        public string Visit(BlockNode node) { foreach (var s in node.Statements) s.Accept(this); return ""; }
        public string Visit(VariableDeclarationNode node) { node.Initializer?.Accept(this); return ""; }
        public string Visit(AssignmentNode node) { node.Target.Accept(this); node.Expression.Accept(this); return ""; }
        public string Visit(ReturnNode node) { node.Expression.Accept(this); return ""; }
        public string Visit(ExpressionStatementNode node) { node.Expression.Accept(this); return ""; }
        public string Visit(IfNode node) { node.Condition.Accept(this); node.ThenBranch.Accept(this); node.ElseBranch?.Accept(this); return ""; }
        public string Visit(WhileNode node) { node.Condition.Accept(this); node.Body.Accept(this); return ""; }
        public string Visit(ForNode node) { node.Initializer?.Accept(this); node.Condition?.Accept(this); node.Increment?.Accept(this); node.Body.Accept(this); return ""; }
        public string Visit(BinaryExpressionNode node) { node.Left.Accept(this); node.Right.Accept(this); return ""; }
        public string Visit(UnaryExpressionNode node) { node.Operand.Accept(this); return ""; }
        public string Visit(CastExpressionNode node) { node.Expression.Accept(this); return ""; }
        public string Visit(MemberAccessNode node) { node.Object.Accept(this); return ""; }
        public string Visit(ArrayAccessNode node) { node.Array.Accept(this); node.Index.Accept(this); return ""; }
        public string Visit(StructInitializationNode node) { foreach (var f in node.Fields.Values) f.Accept(this); return ""; }
        public string Visit(ArrayInitializationNode node) { foreach (var e in node.Elements) e.Accept(this); return ""; }

        public string Visit(LiteralNode node) => "";
        public string Visit(IdentifierNode node) => "";
    }
}
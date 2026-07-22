using System.Text.Json;
using System.Text.Json.Serialization;
using Sage.Ast;
using Sage.Interfaces;

namespace Sage.Utilities
{
    /// <summary>
    /// Serializes the Abstract Syntax Tree (AST) into a human-readable JSON format.
    /// Implements the <see cref="IAstVisitor{T}"/> interface to traverse the tree.
    /// This tool is essential for debugging the Parser's output and verifying structural integrity.
    /// </summary>
    public class AstPrinter : IAstVisitor<object>
    {
        private readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Ensures TokenType enums are serialized as their name (e.g., "Plus") rather than integer values
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Converts the Abstract Syntax Tree, starting from the root node, into a formatted JSON string.
        /// </summary>
        /// <param name="node">The root <see cref="ProgramNode"/> of the AST.</param>
        /// <returns>A string containing the indented JSON representation of the program structure.</returns>
        public string Print(ProgramNode node)
        {
            return JsonSerializer.Serialize(node.Accept(this), _options);
        }

        /// <summary>Visits the program root and collects top-level statements.</summary>
        public object Visit(ProgramNode node) => new
        {
            Type = "Program",
            Statements = node.Statements.Select(s => s.Accept(this))
        };

        /// <summary>Visits a module and collects its internal functions.</summary>
        public object Visit(ModuleNode node) => new
        {
            Type = "Module",
            node.Name,
            Functions = node.Functions.Select(f => f.Accept(this))
        };

        /// <summary>Visits a function declaration including signature and body.</summary>
        public object Visit(FunctionDeclarationNode node) => new
        {
            Type = "FunctionDeclaration",
            node.Name,
            node.IsExtern,
            node.ReturnType,
            Parameters = node.Parameters.Select(p => new { p.Name, p.Type }),
            Body = node.Body?.Accept(this)
        };

        /// <summary>Visits a block of code and its inner statements.</summary>
        public object Visit(BlockNode node) => new
        {
            Type = "Block",
            Statements = node.Statements.Select(s => s.Accept(this))
        };

        /// <summary>Visits a variable or constant declaration.</summary>
        public object Visit(VariableDeclarationNode node) => new
        {
            Type = "VariableDeclaration",
            node.IsConstant,
            node.Name,
            DataType = node.Type,
            Initializer = node.Initializer?.Accept(this)
        };

        /// <summary>Visits an assignment operation.</summary>
        public object Visit(AssignmentNode node) => new
        {
            Type = "Assignment",
            Variable = node.Target.Accept(this),
            Value = node.Expression.Accept(this)
        };

        /// <summary>Visits a conditional if-else structure.</summary>
        public object Visit(IfNode node) => new
        {
            Type = "If",
            Condition = node.Condition.Accept(this),
            Then = node.ThenBranch.Accept(this),
            Else = node.ElseBranch?.Accept(this)
        };

        /// <summary>Visits a while loop structure.</summary>
        public object Visit(WhileNode node) => new
        {
            Type = "While",
            Condition = node.Condition.Accept(this),
            Body = node.Body.Accept(this)
        };

        /// <summary>Visits a for loop structure with its various optional components.</summary>
        public object Visit(ForNode node) => new
        {
            Type = "For",
            Initializer = node.Initializer?.Accept(this),
            Condition = node.Condition?.Accept(this),
            Increment = node.Increment?.Accept(this),
            Body = node.Body.Accept(this)
        };

        /// <summary>Visits a return statement.</summary>
        public object Visit(ReturnNode node) => new
        {
            Type = "Return",
            Value = node.Expression.Accept(this)
        };

        /// <summary>Wraps an expression used as a statement.</summary>
        public object Visit(ExpressionStatementNode node) => node.Expression.Accept(this);

        /// <summary>Visits an external FFI block declaration.</summary>
        public object Visit(ExternBlockNode node) => new
        {
            Type = "ExternBlock",
            node.Alias,
            node.Header,
            node.Library,
            Declarations = node.Declarations.Select(d => d.Accept(this))
        };

        /// <summary>Visits a binary operation.</summary>
        public object Visit(BinaryExpressionNode node) => new
        {
            Type = "BinaryExpression",
            Left = node.Left.Accept(this),
            node.Operator,
            Right = node.Right.Accept(this)
        };

        /// <summary>Visits a unary operation (prefix or postfix).</summary>
        public object Visit(UnaryExpressionNode node) => new
        {
            Type = "UnaryExpression",
            node.Operator,
            Operand = node.Operand.Accept(this),
            node.IsPostfix
        };

        /// <summary>Visits a type cast expression.</summary>
        public object Visit(CastExpressionNode node) => new
        {
            Type = "Cast",
            node.TargetType,
            Expression = node.Expression.Accept(this)
        };

        /// <summary>Visits a function call and its arguments.</summary>
        public object Visit(FunctionCallNode node) => new
        {
            Type = "FunctionCall",
            FunctionName = node.Name,
            Arguments = node.Arguments.Select(a => a.Accept(this))
        };

        /// <summary>Visits a constant literal value.</summary>
        public object Visit(LiteralNode node) => new
        {
            Type = "Literal",
            node.Value,
            DataType = node.TypeName
        };

        /// <summary>Visits a named identifier.</summary>
        public object Visit(IdentifierNode node) => new
        {
            Type = "Identifier",
            node.Name
        };

        /// <summary>Visits a 'use' directive for module imports.</summary>
        public object Visit(UseNode node) => new { Type = "Use", node.Module };

        /// <summary>Visits an unsafe block.</summary>
        public object Visit(UnsafeBlockNode node) => new
        {
            Type = "UnsafeBlock",
            Body = node.Body.Accept(this)
        };

        /// <summary>Visits a 'new' heap allocation expression.</summary>
        public object Visit(NewExpressionNode node) => new
        {
            Type = "New",
            node.TypeName,
            Fields = node.Fields.ToDictionary(k => k.Key, v => v.Value.Accept(this))
        };

        /// <summary>Visits a 'delete' statement.</summary>
        public object Visit(DeleteNode node) => new
        {
            Type = "Delete",
            Target = node.Target.Accept(this)
        };

        /// <summary>Visits an opaque extern type declaration.</summary>
        public object Visit(ExternTypeNode node) => new { Type = "ExternType", node.Name };

        /// <summary>Visits a string literal containing interpolation.</summary>
        public object Visit(InterpolatedStringNode node) => new { Type = "InterpolatedString" };

        /// <summary>Visits a struct declaration and its fields.</summary>
        public object Visit(StructDeclarationNode node) => new
        {
            Type = "StructDeclaration",
            node.Name,
            Fields = node.Fields.Select(f => f.Accept(this))
        };

        /// <summary>Visits a struct initialization literal.</summary>
        public object Visit(StructInitializationNode node) => new
        {
            Type = "StructInitialization",
            Fields = node.Fields.ToDictionary(k => k.Key, v => v.Value.Accept(this))
        };

        /// <summary>Visits a mamber access dot.</summary>
        public object Visit(MemberAccessNode node) => new
        {
            Type = "MemberAccess",
            Object = node.Object.Accept(this),
            node.PropertyName
        };

        /// <summary>Visits an array initialization literal.</summary>
        public object Visit(ArrayInitializationNode node) => new
        {
            Type = "ArrayInitialization",
            Elements = node.Elements.Select(e => e.Accept(this))
        };

        /// <summary>Visits an array element access.</summary>
        public object Visit(ArrayAccessNode node) => new
        {
            Type = "ArrayAccess",
            Array = node.Array.Accept(this),
            Index = node.Index.Accept(this)
        };
    }
}
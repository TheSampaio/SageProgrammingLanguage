using Sage.Ast;
using Sage.Enums;
using Sage.Interfaces;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Validates identifier existence, manages nested scopes, performs type checking, enforces
    /// constant immutability, and gates raw pointer usage behind <c>unsafe</c> contexts.
    /// </summary>
    public class SemanticAnalyzer(SymbolTable? sharedTable = null) : IAstVisitor<string>
    {
        private readonly SymbolTable _symbolTable = sharedTable ?? new SymbolTable();
        private readonly Dictionary<string, StructDeclarationNode> _structDeclarations = new();

        /// <summary>Depth of nested unsafe contexts (unsafe blocks, unsafe functions, extern signatures).</summary>
        private int _unsafeDepth;

        /// <summary>True when analysis is currently inside an unsafe context.</summary>
        private bool InUnsafe => _unsafeDepth > 0;

        public void Analyze(ProgramNode ast) => ast.Accept(this);

        /// <summary>
        /// Pre-registers module-level symbols to allow forward references and out-of-order calls.
        /// </summary>
        public void RegisterModuleSymbols(ProgramNode moduleAst)
        {
            foreach (var node in moduleAst.Statements.OfType<ModuleNode>())
            {
                RegisterExternBlocks(node);
                foreach (var member in node.Members.OfType<FunctionDeclarationNode>())
                {
                    member.ModuleOwner = node.Name;
                    RegisterFunction(member, node.Name);
                }
            }
        }

        private void RegisterFunction(FunctionDeclarationNode func, string moduleName = "")
        {
            // 1. Define the bare name (e.g., "print_line") for use inside the module itself.
            _symbolTable.Define(func.Name, func.ReturnType, isFunction: true, isExtern: func.IsExtern);

            // 2. Define the fully qualified name (e.g., "console::print_line") for external use.
            if (!string.IsNullOrEmpty(moduleName))
            {
                _symbolTable.Define($"{moduleName}::{func.Name}", func.ReturnType, isFunction: true, isExtern: func.IsExtern);
            }
        }

        /// <summary>
        /// Registers every extern binding and opaque type of a module up front, so a module's
        /// functions can reference its FFI declarations regardless of textual order.
        /// </summary>
        private void RegisterExternBlocks(ModuleNode module)
        {
            foreach (var ext in module.Members.OfType<ExternBlockNode>())
                RegisterExternBlock(ext);
        }

        private void RegisterExternBlock(ExternBlockNode node)
        {
            foreach (var decl in node.Declarations)
            {
                switch (decl)
                {
                    case ExternTypeNode opaque:
                        TypeSystem.RegisterOpaqueType(opaque.Name);
                        break;
                    case FunctionDeclarationNode func:
                        _symbolTable.Define($"{node.Alias}::{func.Name}", func.ReturnType, isFunction: true, isExtern: true);
                        break;
                }
            }
        }

        public string Visit(ProgramNode node)
        {
            // Register top-level extern blocks and structs first so declarations can appear in any order.
            foreach (var ext in node.Statements.OfType<ExternBlockNode>()) RegisterExternBlock(ext);
            foreach (var stmt in node.Statements) stmt.Accept(this);
            return "none";
        }

        public string Visit(ModuleNode node)
        {
            // Internal registration to support recursion and forward references within the module.
            RegisterExternBlocks(node);
            foreach (var member in node.Members.OfType<FunctionDeclarationNode>())
            {
                member.ModuleOwner = node.Name;
                RegisterFunction(member, node.Name);
            }

            foreach (var member in node.Members) member.Accept(this);
            return "none";
        }

        public string Visit(FunctionDeclarationNode node)
        {
            if (!_symbolTable.IsDefinedInCurrentScope(node.Name))
                RegisterFunction(node, node.ModuleOwner);

            // An unsafe or extern function establishes an unsafe context for its signature and body.
            bool unsafeContext = node.IsUnsafe || node.IsExtern;
            if (unsafeContext) _unsafeDepth++;

            // Raw pointers may only appear in a signature that is unsafe or extern.
            if (!InUnsafe)
            {
                if (TypeSystem.IsRawPointer(node.ReturnType))
                    throw new CompilerException(node, "S120",
                        $"Function '{node.Name}' returns raw pointer '{node.ReturnType}'. Declare it 'unsafe func' or use a safe reference.");

                foreach (var param in node.Parameters)
                {
                    if (TypeSystem.IsRawPointer(param.Type))
                        throw new CompilerException(node, "S120",
                            $"Parameter '{param.Name}' of '{node.Name}' is raw pointer '{param.Type}'. Declare the function 'unsafe func'.");
                }
            }

            _symbolTable.EnterScope();

            foreach (var param in node.Parameters)
                _symbolTable.Define(param.Name, param.Type);

            if (!node.IsExtern && node.Body != null)
                node.Body.Accept(this);

            _symbolTable.ExitScope();

            if (unsafeContext) _unsafeDepth--;
            return node.ReturnType;
        }

        public string Visit(UnsafeBlockNode node)
        {
            _unsafeDepth++;
            node.Body.Accept(this);
            _unsafeDepth--;
            return "none";
        }

        public string Visit(VariableDeclarationNode node)
        {
            if (_symbolTable.IsDefinedInCurrentScope(node.Name))
                throw new CompilerException(node, "S102", $"Variable '{node.Name}' is already defined.");

            if (TypeSystem.IsRawPointer(node.Type) && !InUnsafe)
                throw new CompilerException(node, "S121",
                    $"Raw pointer type '{node.Type}' can only be used inside an 'unsafe' block. Use 'new' for safe heap allocation.");

            if (node.Initializer != null)
            {
                string initType = node.Initializer.Accept(this);

                if (initType != "struct_initializer" && initType != "array_initializer")
                {
                    if (!TypeSystem.AreTypesCompatible(node.Type, initType) &&
                        !IsAutoPromotableLiteral(node.Type, node.Initializer))
                    {
                        throw new CompilerException(node, "S101", $"Type Mismatch: Cannot assign {initType} to {node.Name} (expected {node.Type}).");
                    }
                }

                if (node.Initializer is LiteralNode literal)
                    literal.TypeName = node.Type;

                node.Initializer.VariableType = node.Type;

                // A variable initialized from 'new' becomes a safe heap reference.
                node.IsReference = node.Initializer.IsReference;
            }

            _symbolTable.Define(node.Name, node.Type, isConstant: node.IsConstant, isReference: node.IsReference);
            return node.Type;
        }

        public string Visit(BinaryExpressionNode node)
        {
            string lhs = node.Left.Accept(this);
            string rhs = node.Right.Accept(this);

            if (IsComparisonOp(node.Operator)) return "b8";

            if (IsLogicalOp(node.Operator))
            {
                if (lhs != "b8" || rhs != "b8")
                    throw new CompilerException(node, "S107", "Logical operators require b8.");
                return "b8";
            }

            string? dominantType = TypeSystem.GetDominantType(lhs, rhs);
            return dominantType ?? throw new CompilerException(node, "S108", $"Invalid arithmetic: {lhs} and {rhs}");
        }

        public string Visit(FunctionCallNode node)
        {
            var symbol = _symbolTable.Resolve(node.Name)
                ?? throw new CompilerException(node, "S105", $"Function '{node.Name}' not found.");

            node.IsExternCall = symbol.IsExtern;

            foreach (var arg in node.Arguments) arg.Accept(this);

            return symbol.Type;
        }

        // --- Helpers ---

        private static bool IsComparisonOp(TokenType op) =>
            op is TokenType.EqualEqual or TokenType.NotEqual or TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual;

        private static bool IsLogicalOp(TokenType op) =>
            op is TokenType.AmpersandAmpersand or TokenType.PipePipe;

        // Syntactic Sugar: Allows assigning numeric literals to corresponding numeric types automatically.
        private bool IsAutoPromotableLiteral(string targetType, AstNode initializer)
        {
            if (initializer is not LiteralNode literal) return false;
            string literalType = literal.Accept(this);

            if (TypeSystem.IsNumeric(targetType) && TypeSystem.IsNumeric(literalType))
            {
                if (!TypeSystem.IsFloatingPoint(targetType) && TypeSystem.IsFloatingPoint(literalType))
                    return false;

                return true;
            }

            return false;
        }

        // --- Standard Visitor Implementations ---

        public string Visit(IdentifierNode node)
        {
            var symbol = _symbolTable.Resolve(node.Name)
                ?? throw new CompilerException(node, "S105", $"Identifier '{node.Name}' not declared.");
            node.VariableType = symbol.Type;
            node.IsReference = symbol.IsReference;
            return symbol.Type;
        }

        public string Visit(BlockNode node)
        {
            _symbolTable.EnterScope();
            foreach (var stmt in node.Statements) stmt.Accept(this);
            _symbolTable.ExitScope();
            return "none";
        }

        public string Visit(AssignmentNode node)
        {
            if (node.Target is IdentifierNode idTarget)
            {
                var targetSymbol = _symbolTable.Resolve(idTarget.Name);
                if (targetSymbol is { IsConstant: true })
                    throw new CompilerException(node, "S103", $"Cannot assign to constant '{idTarget.Name}'.");
            }

            string targetType = node.Target.Accept(this);
            string exprType = node.Expression.Accept(this);

            if (exprType != "struct_initializer" && exprType != "array_initializer")
            {
                if (!TypeSystem.AreTypesCompatible(targetType, exprType) && !IsAutoPromotableLiteral(targetType, node.Expression))
                    throw new CompilerException(node, "S108", $"Cannot assign {exprType} to {targetType}.");
            }

            node.Expression.VariableType = targetType;
            node.VariableType = targetType;
            node.IsReference = node.Target.IsReference;

            return targetType;
        }

        public string Visit(ExternBlockNode node)
        {
            // Idempotent: also covers top-level extern blocks defined outside any module.
            RegisterExternBlock(node);
            return "none";
        }

        public string Visit(ExternTypeNode node)
        {
            TypeSystem.RegisterOpaqueType(node.Name);
            return "none";
        }

        public string Visit(IfNode node) { CheckCondition(node.Condition); node.ThenBranch.Accept(this); node.ElseBranch?.Accept(this); return "none"; }
        public string Visit(WhileNode node) { CheckCondition(node.Condition); node.Body.Accept(this); return "none"; }

        public string Visit(ForNode node)
        {
            _symbolTable.EnterScope();
            node.Initializer?.Accept(this);
            if (node.Condition != null) CheckCondition(node.Condition);
            node.Increment?.Accept(this);
            node.Body.Accept(this);
            _symbolTable.ExitScope();
            return "none";
        }

        private void CheckCondition(AstNode condition)
        {
            if (condition.Accept(this) != "b8")
                throw new CompilerException(condition, "S104", "Condition must be b8.");
        }

        public string Visit(LiteralNode node) => node.TypeName;
        public string Visit(ReturnNode node) => node.Expression.Accept(this);
        public string Visit(ExpressionStatementNode node) => node.Expression.Accept(this);
        public string Visit(UnaryExpressionNode node) => node.Operand.Accept(this);

        public string Visit(CastExpressionNode node)
        {
            node.Expression.Accept(this);

            if (TypeSystem.IsRawPointer(node.TargetType) && !InUnsafe)
                throw new CompilerException(node, "S122",
                    $"Casting to raw pointer '{node.TargetType}' requires an 'unsafe' block.");

            return node.TargetType;
        }

        public string Visit(UseNode node) => "none";

        public string Visit(InterpolatedStringNode node)
        {
            foreach (var part in node.Parts)
                part.VariableType = part.Accept(this);
            return "str";
        }

        public string Visit(StructDeclarationNode node)
        {
            _symbolTable.Define(node.Name, "struct");
            _structDeclarations[node.Name] = node;
            return "none";
        }

        public string Visit(StructInitializationNode node)
        {
            foreach (var value in node.Fields.Values) value.Accept(this);
            return "struct_initializer";
        }

        public string Visit(NewExpressionNode node)
        {
            if (!_structDeclarations.TryGetValue(node.TypeName, out var structDecl))
                throw new CompilerException(node, "S130",
                    $"'new' requires a known struct type, but '{node.TypeName}' is not a struct.");

            foreach (var (fieldName, valueExpr) in node.Fields)
            {
                var field = structDecl.Fields.FirstOrDefault(f => f.Name == fieldName)
                    ?? throw new CompilerException(node, "S131",
                        $"Struct '{node.TypeName}' does not contain field '{fieldName}'.");

                valueExpr.Accept(this);
                if (valueExpr is LiteralNode lit) lit.TypeName = field.Type;
                valueExpr.VariableType = field.Type;
            }

            // 'new' yields a memory-safe heap reference whose surface type is the struct itself.
            node.IsReference = true;
            node.VariableType = node.TypeName;
            return node.TypeName;
        }

        public string Visit(DeleteNode node)
        {
            node.Target.Accept(this);
            if (!node.Target.IsReference)
                throw new CompilerException(node, "S132",
                    "'delete' expects a reference created with 'new'. For raw pointers use memory::release inside 'unsafe'.");
            return "none";
        }

        public string Visit(MemberAccessNode node)
        {
            string objType = node.Object.Accept(this);

            if (!_structDeclarations.TryGetValue(objType, out var structDecl))
                throw new CompilerException(node, "S109", $"Cannot access member. '{objType}' is not a struct.");

            var field = structDecl.Fields.FirstOrDefault(f => f.Name == node.PropertyName);
            if (field == null)
                throw new CompilerException(node, "S110", $"Struct '{objType}' does not contain field '{node.PropertyName}'.");

            node.VariableType = field.Type;
            // Accessing a field of a reference yields a plain value; the reference-ness stops here.
            node.IsReference = false;
            return field.Type;
        }

        public string Visit(ArrayInitializationNode node)
        {
            foreach (var el in node.Elements) el.Accept(this);
            return "array_initializer";
        }

        public string Visit(ArrayAccessNode node)
        {
            string arrayType = node.Array.Accept(this);

            bool isArray = TypeSystem.IsArrayType(arrayType);
            bool isPointer = TypeSystem.IsRawPointer(arrayType);

            if (!isArray && !isPointer)
                throw new CompilerException(node, "S111", $"Cannot index into non-array and non-pointer type '{arrayType}'.");

            if (isPointer && !InUnsafe)
                throw new CompilerException(node, "S123",
                    "Indexing a raw pointer dereferences memory and requires an 'unsafe' block.");

            string indexType = node.Index.Accept(this);
            if (!TypeSystem.IsNumeric(indexType) || TypeSystem.IsFloatingPoint(indexType))
                throw new CompilerException(node, "S112", $"Array index must be an integer, got '{indexType}'.");

            node.VariableType = isArray
                ? TypeSystem.GetArrayBaseType(arrayType)
                : arrayType[..^1];

            return node.VariableType;
        }
    }
}

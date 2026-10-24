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
        private readonly Dictionary<string, FunctionDeclarationNode> _functionDeclarations = new(StringComparer.Ordinal);
        private string _currentModule = "";
        private string? _currentReturnType;

        /// <summary>Depth of nested unsafe contexts (unsafe blocks, unsafe functions, extern signatures).</summary>
        private int _unsafeDepth;
        private int _loopDepth;
        private int _functionDepth;
        private int _borrowArgumentDepth;

        /// <summary>True when analysis is currently inside an unsafe context.</summary>
        private bool InUnsafe => _unsafeDepth > 0;

        public void Analyze(ProgramNode ast) => ast.Accept(this);

        /// <summary>
        /// Pre-registers module-level symbols to allow forward references and out-of-order calls.
        /// </summary>
        public void RegisterModuleSymbols(ProgramNode moduleAst)
        {
            foreach (var structure in moduleAst.Statements.OfType<StructDeclarationNode>()) RegisterStructDefinition(structure);
            foreach (var function in moduleAst.Statements.OfType<FunctionDeclarationNode>()) RegisterFunction(function);
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
            if (TypeSystem.IsBorrowType(func.ReturnType))
                throw new CompilerException(func, "S173", "Borrowed return values require lifetime contracts that are not implemented yet.");
            string name = string.IsNullOrEmpty(moduleName) ? func.Name : $"{moduleName}::{func.Name}";
            if (_functionDeclarations.TryGetValue(name, out var existing))
            {
                if (!ReferenceEquals(existing, func))
                    throw new CompilerException(func, "S150", $"Callable '{name}' is already declared.");
                return;
            }
            if (_symbolTable.Resolve(name) != null)
                throw new CompilerException(func, "S150", $"Callable '{name}' conflicts with an existing declaration.");
            _functionDeclarations.Add(name, func);
            _symbolTable.Define(name, func.ReturnType, isFunction: true, isExtern: func.IsExtern,
                signature: new CallableSignature(func.ReturnType, func.Parameters.Select(parameter => parameter.Type), func.IsUnsafe));
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
                        if (TypeSystem.IsBorrowType(func.ReturnType) || func.Parameters.Any(parameter => TypeSystem.IsBorrowType(parameter.Type)))
                            throw new CompilerException(func, "S173", "Native ABI declarations cannot use Sage borrowing contracts.");
                        if (_functionDeclarations.ContainsKey($"{node.Alias}::{func.Name}"))
                            throw new CompilerException(func, "S150",
                                $"Native alias '{node.Alias}::{func.Name}' conflicts with a Sage callable.");
                        string name = $"{node.Alias}::{func.Name}";
                        var signature = new CallableSignature(func.ReturnType, func.Parameters.Select(parameter => parameter.Type),
                            func.IsUnsafe, node.Header, node.Library);
                        var existing = _symbolTable.Resolve(name);
                        if (existing != null)
                        {
                            if (existing.Signature == null || !existing.Signature.Matches(signature))
                                throw new CompilerException(func, "S152", $"Native alias '{name}' has conflicting signatures or native origins.");
                            break;
                        }
                        _symbolTable.Define(name, func.ReturnType, isFunction: true, isExtern: true, signature: signature);
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

            string previousModule = _currentModule;
            _currentModule = node.Name;
            try { foreach (var member in node.Members) member.Accept(this); }
            finally { _currentModule = previousModule; }
            return "none";
        }

        public string Visit(FunctionDeclarationNode node)
        {
            RegisterFunction(node, node.ModuleOwner);
            var duplicateParameter = node.Parameters.GroupBy(parameter => parameter.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicateParameter != null)
                throw new CompilerException(node, "S154", $"Parameter '{duplicateParameter.Key}' is declared more than once in '{node.Name}'.");
            foreach (var parameter in node.Parameters.Where(parameter => TypeSystem.IsBorrowType(parameter.Type)))
                if (!IsBorrowableValue(TypeSystem.GetBorrowedType(parameter.Type)))
                    throw new CompilerException(node, "S176",
                        $"Borrowed parameter '{parameter.Name}' requires a scalar or a struct composed of borrowable values; '{parameter.Type}' is not supported yet.");

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
            string? previousReturnType = _currentReturnType;
            _currentReturnType = node.ReturnType;
            try
            {
                foreach (var param in node.Parameters)
                    _symbolTable.Define(param.Name, TypeSystem.GetBorrowedType(param.Type), borrowing: TypeSystem.GetBorrowKind(param.Type));

                if (!node.IsExtern && node.Body != null)
                {
                    _functionDepth++;
                    try { node.Body.Accept(this); }
                    finally { _functionDepth--; }
                    if (node.ReturnType != "none" && CanCompleteNormally(node.Body))
                        throw new CompilerException(node, "S163", $"Function '{node.Name}' can reach its end without returning '{node.ReturnType}'.");
                }
            }
            finally
            {
                _currentReturnType = previousReturnType;
                _symbolTable.ExitScope();
                if (unsafeContext) _unsafeDepth--;
            }
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
            if (TypeSystem.IsBorrowType(node.Type))
                throw new CompilerException(node, "S173", "Borrowed bindings are currently supported only as function parameters.");
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

            if (node.Initializer is IdentifierNode source && node.IsReference)
                ConsumeReference(source, node, OwnershipKind.Moved);

            _symbolTable.Define(node.Name, node.Type, isConstant: node.IsConstant,
                isReference: node.IsReference, declarationLoopDepth: _loopDepth);
            return node.Type;
        }

        public string Visit(BinaryExpressionNode node)
        {
            string lhs = node.Left.Accept(this);
            string rhs = node.Right.Accept(this);
            RequireValueContract(node.Left);
            RequireValueContract(node.Right);

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
            string name = node.Name;
            var symbol = _symbolTable.ResolveLocal(name);
            if (symbol == null)
            {
                if (!name.Contains("::") && !string.IsNullOrEmpty(_currentModule) &&
                    _symbolTable.Resolve($"{_currentModule}::{name}") != null)
                    name = $"{_currentModule}::{name}";
                symbol = _symbolTable.Resolve(name);
            }
            symbol = symbol
                ?? throw new CompilerException(node, "S105", $"Function '{node.Name}' not found.");
            if (!symbol.IsFunction)
                throw new CompilerException(node, "S151", $"Symbol '{node.Name}' is not callable.");

            node.ResolvedName = name;
            node.IsExternCall = symbol.IsExtern;
            var signature = symbol.Signature ?? throw new InvalidOperationException($"Callable '{name}' has no signature.");
            if (signature.IsUnsafe && !InUnsafe)
                throw new CompilerException(node, "S164", $"Calling unsafe function '{name}' requires an unsafe context.");
            if (node.Arguments.Count != signature.ParameterTypes.Count)
                throw new CompilerException(node, "S160",
                    $"Function '{name}' expects {signature.ParameterTypes.Count} arguments, but received {node.Arguments.Count}.");

            bool hasBorrowedParameters = signature.ParameterTypes.Any(TypeSystem.IsBorrowType);
            var borrowed = new Dictionary<SymbolMetadata, (BorrowKind Kind, BorrowExpressionNode Expression)>();
            for (int index = 0; index < node.Arguments.Count; index++)
            {
                var arg = node.Arguments[index];
                string parameterType = signature.ParameterTypes[index];
                if (TypeSystem.IsBorrowType(parameterType))
                {
                    if (arg is not BorrowExpressionNode borrow)
                        throw new CompilerException(arg, "S170", $"Argument {index + 1} of '{name}' requires an explicit '{parameterType}' borrow.");
                    _borrowArgumentDepth++;
                    string borrowedType;
                    try { borrowedType = borrow.Accept(this); }
                    finally { _borrowArgumentDepth--; }
                    if (borrowedType != parameterType)
                        throw new CompilerException(borrow, "S170", $"Argument {index + 1} of '{name}' expects '{parameterType}', not '{borrowedType}'.");
                    var source = _symbolTable.Resolve(((IdentifierNode)borrow.Target).Name)!;
                    var kind = TypeSystem.GetBorrowKind(borrowedType);
                    if (borrowed.TryGetValue(source, out var previous) && (kind == BorrowKind.Mutable || previous.Kind == BorrowKind.Mutable))
                        throw new CompilerException(borrow, "S172",
                            $"This borrow conflicts with the borrow at ({previous.Expression.Line},{previous.Expression.Column}) in the same call.");
                    borrowed[source] = (kind, borrow);
                    continue;
                }
                if (arg is BorrowExpressionNode)
                    throw new CompilerException(arg, "S170", $"Argument {index + 1} of '{name}' expects a value, not a borrow.");
                if (hasBorrowedParameters && !IsPureBorrowCallArgument(arg))
                    throw new CompilerException(arg, "S177", "Evaluate calls and mutations into local values before passing them alongside borrowed arguments.");
                string type = arg.Accept(this);
                RequireValueContract(arg);
                if (!symbol.IsExtern)
                    ValidateValueType(arg, signature.ParameterTypes[index], type, "S161",
                        $"Argument {index + 1} of '{name}'");
            }

            return symbol.Type;
        }

        public string Visit(BorrowExpressionNode node)
        {
            if (_borrowArgumentDepth == 0)
                throw new CompilerException(node, "S173", "A borrow must be a direct argument to a declared borrowed parameter.");
            if (node.Target is not IdentifierNode identifier)
                throw new CompilerException(node, "S175", "Borrow a named variable; temporary values and field-specific borrows are not supported yet.");
            string type = identifier.Accept(this);
            var source = _symbolTable.ResolveLocal(identifier.Name);
            if (source == null || source.IsFunction || !IsBorrowableValue(type))
                throw new CompilerException(node, "S176", "Only local variables or parameters with supported value types can be borrowed.");
            if (node.IsMutable && (source.IsConstant || source.Borrowing == BorrowKind.Shared))
                throw new CompilerException(node, "S171", $"Cannot mutably borrow '{identifier.Name}' through shared or constant access.");
            node.VariableType = (node.IsMutable ? "&mut " : "&") + type;
            return node.VariableType;
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

        private void ValidateValueType(AstNode expression, string targetType, string sourceType, string code, string description)
        {
            bool structInitializer = sourceType == "struct_initializer" && _structDeclarations.ContainsKey(targetType);
            if (!structInitializer && !TypeSystem.AreTypesCompatible(targetType, sourceType) &&
                !IsAutoPromotableLiteral(targetType, expression))
                throw new CompilerException(expression, code, $"{description} requires '{targetType}', but received '{sourceType}'.");
            expression.VariableType = targetType;
            if (expression is LiteralNode literal) literal.TypeName = targetType;
        }

        // --- Standard Visitor Implementations ---

        public string Visit(IdentifierNode node)
        {
            var symbol = _symbolTable.Resolve(node.Name)
                ?? throw new CompilerException(node, "S105", $"Identifier '{node.Name}' not declared.");
            if (symbol.IsReference && symbol.Ownership.Kind != OwnershipKind.Available)
            {
                var state = symbol.Ownership;
                string code = state.Kind switch
                {
                    OwnershipKind.Moved => "S140",
                    OwnershipKind.Released => "S141",
                    _ => "S142"
                };
                string reason = state.Kind switch
                {
                    OwnershipKind.Moved => "was moved",
                    OwnershipKind.Released => "was deleted",
                    _ => "may have been moved or deleted on another path"
                };
                throw new CompilerException(node, code,
                    $"Reference '{node.Name}' {reason} at ({state.Line},{state.Column}) and cannot be used here.");
            }
            node.VariableType = symbol.Type;
            node.IsReference = symbol.IsReference;
            node.IsBorrowedValue = symbol.Borrowing != BorrowKind.None;
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
            EnsureMutablePlace(node.Target, node);
            if (node.Target is IdentifierNode idTarget)
            {
                var targetSymbol = _symbolTable.Resolve(idTarget.Name);
                if (targetSymbol is { IsConstant: true })
                    throw new CompilerException(node, "S103", $"Cannot assign to constant '{idTarget.Name}'.");
            }

            string targetType = node.Target.Accept(this);
            if (node.Target.IsReference)
                throw new CompilerException(node, "S143",
                    "Reassigning an owning reference is not supported yet. Create a new binding to transfer ownership.");
            string exprType = node.Expression.Accept(this);
            RequireValueContract(node.Expression);

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

        public string Visit(IfNode node)
        {
            CheckCondition(node.Condition);
            var before = _symbolTable.CaptureOwnership();
            node.ThenBranch.Accept(this);
            var thenState = _symbolTable.CaptureOwnership();
            SymbolTable.RestoreOwnership(before);
            node.ElseBranch?.Accept(this);
            var elseState = _symbolTable.CaptureOwnership();

            bool thenContinues = CanCompleteNormally(node.ThenBranch);
            bool elseContinues = node.ElseBranch == null || CanCompleteNormally(node.ElseBranch);
            foreach (var symbol in before.Keys)
            {
                var left = thenState[symbol];
                var right = elseState[symbol];
                symbol.Ownership = !thenContinues ? right : !elseContinues ? left
                    : left.Kind == right.Kind ? left
                    : new OwnershipState(OwnershipKind.MaybeUnavailable,
                        left.Kind != OwnershipKind.Available ? left.Line : right.Line,
                        left.Kind != OwnershipKind.Available ? left.Column : right.Column);
            }
            return "none";
        }

        public string Visit(WhileNode node)
        {
            CheckCondition(node.Condition);
            _loopDepth++;
            try { node.Body.Accept(this); }
            finally { _loopDepth--; }
            return "none";
        }

        public string Visit(ForNode node)
        {
            _symbolTable.EnterScope();
            node.Initializer?.Accept(this);
            if (node.Condition != null) CheckCondition(node.Condition);
            _loopDepth++;
            try
            {
                node.Body.Accept(this);
                node.Increment?.Accept(this);
            }
            finally { _loopDepth--; }
            _symbolTable.ExitScope();
            return "none";
        }

        private void CheckCondition(AstNode condition)
        {
            if (condition.Accept(this) != "b8")
                throw new CompilerException(condition, "S104", "Condition must be b8.");
        }

        public string Visit(LiteralNode node) => node.TypeName;
        public string Visit(ReturnNode node)
        {
            string type = node.Expression.Accept(this);
            RequireValueContract(node.Expression);
            if (_currentReturnType == null)
                throw new CompilerException(node, "S162", "A return statement must be inside a function.");
            ValidateValueType(node.Expression, _currentReturnType, type, "S162", "Return expression");
            return type;
        }
        public string Visit(ExpressionStatementNode node)
        {
            string type = node.Expression.Accept(this);
            if (node.Expression is NewExpressionNode)
                throw new CompilerException(node, "S146", "An allocated reference must be bound to a local variable.");
            return type;
        }

        public string Visit(UnaryExpressionNode node)
        {
            if (node.Operator == TokenType.PlusPlus) EnsureMutablePlace(node.Operand, node);
            string type = node.Operand.Accept(this);
            RequireValueContract(node.Operand);
            return type;
        }

        public string Visit(CastExpressionNode node)
        {
            node.Expression.Accept(this);
            if (!TypeSystem.IsRawPointer(node.TargetType)) RequireValueContract(node.Expression);

            if (TypeSystem.IsRawPointer(node.TargetType) && !InUnsafe)
                throw new CompilerException(node, "S122",
                    $"Casting to raw pointer '{node.TargetType}' requires an 'unsafe' block.");

            return node.TargetType;
        }

        public string Visit(UseNode node) => "none";

        public string Visit(InterpolatedStringNode node)
        {
            foreach (var part in node.Parts)
            {
                part.VariableType = part.Accept(this);
                RequireValueContract(part);
            }
            return "str";
        }

        public string Visit(StructDeclarationNode node)
        {
            if (node.Fields.Any(field => TypeSystem.IsBorrowType(field.Type)))
                throw new CompilerException(node, "S173", "Struct fields cannot store borrowed references yet.");
            RegisterStructDefinition(node);
            _symbolTable.Define(node.Name, "struct");
            return "none";
        }

        public string Visit(StructInitializationNode node)
        {
            foreach (var value in node.Fields.Values)
            {
                value.Accept(this);
                RequireValueContract(value);
            }
            return "struct_initializer";
        }

        public string Visit(NewExpressionNode node)
        {
            if (_functionDepth == 0)
                throw new CompilerException(node, "S146", "Heap references must be created inside a function scope.");
            if (!_structDeclarations.TryGetValue(node.TypeName, out var structDecl))
                throw new CompilerException(node, "S130",
                    $"'new' requires a known struct type, but '{node.TypeName}' is not a struct.");

            foreach (var (fieldName, valueExpr) in node.Fields)
            {
                var field = structDecl.Fields.FirstOrDefault(f => f.Name == fieldName)
                    ?? throw new CompilerException(node, "S131",
                        $"Struct '{node.TypeName}' does not contain field '{fieldName}'.");

                valueExpr.Accept(this);
                RequireValueContract(valueExpr);
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
            if (node.Target is IdentifierNode borrowed && _symbolTable.Resolve(borrowed.Name) is { Borrowing: BorrowKind.Shared or BorrowKind.Mutable })
                throw new CompilerException(node, "S174", $"Borrowed value '{borrowed.Name}' cannot be deleted.");
            node.Target.Accept(this);
            if (!node.Target.IsReference)
                throw new CompilerException(node, "S132",
                    "'delete' expects a reference created with 'new'. For raw pointers use memory::release inside 'unsafe'.");
            if (node.Target is IdentifierNode target)
                ConsumeReference(target, node, OwnershipKind.Released);
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
            foreach (var el in node.Elements)
            {
                el.Accept(this);
                RequireValueContract(el);
            }
            return "array_initializer";
        }

        private void ConsumeReference(IdentifierNode source, AstNode operation, OwnershipKind kind)
        {
            var symbol = _symbolTable.Resolve(source.Name)!;
            if (_loopDepth > symbol.DeclarationLoopDepth)
                throw new CompilerException(operation, "S145",
                    $"Reference '{source.Name}' is declared outside this loop and cannot be moved or deleted on a potentially repeated path.");
            symbol.Ownership = new OwnershipState(kind, operation.Line, operation.Column);
        }

        private void RegisterStructDefinition(StructDeclarationNode node)
        {
            if (_structDeclarations.TryGetValue(node.Name, out var existing) && !ReferenceEquals(existing, node))
                throw new CompilerException(node, "S150", $"Struct '{node.Name}' is already declared.");
            _structDeclarations[node.Name] = node;
        }

        private bool IsBorrowableValue(string type, HashSet<string>? path = null)
        {
            if (TypeSystem.IsNumeric(type) || type is "b8" or "c_bool") return true;
            if (!_structDeclarations.TryGetValue(type, out var structure)) return false;
            path ??= new HashSet<string>(StringComparer.Ordinal);
            if (!path.Add(type)) return false;
            bool supported = structure.Fields.All(field => IsBorrowableValue(field.Type, path));
            path.Remove(type);
            return supported;
        }

        private void EnsureMutablePlace(AstNode target, AstNode operation)
        {
            AstNode root = target;
            while (root is MemberAccessNode or ArrayAccessNode)
                root = root is MemberAccessNode member ? member.Object : ((ArrayAccessNode)root).Array;
            if (root is not IdentifierNode identifier) return;
            var symbol = _symbolTable.Resolve(identifier.Name);
            if (symbol?.Borrowing == BorrowKind.Shared)
                throw new CompilerException(operation, "S171", $"Cannot mutate '{identifier.Name}' through a shared borrow.");
            if (symbol?.IsConstant == true)
                throw new CompilerException(operation, "S103", $"Cannot mutate constant '{identifier.Name}'.");
        }

        private static bool IsPureBorrowCallArgument(AstNode node) => node switch
        {
            LiteralNode or IdentifierNode => true,
            MemberAccessNode member => IsPureBorrowCallArgument(member.Object),
            ArrayAccessNode array => IsPureBorrowCallArgument(array.Array) && IsPureBorrowCallArgument(array.Index),
            BinaryExpressionNode binary => IsPureBorrowCallArgument(binary.Left) && IsPureBorrowCallArgument(binary.Right),
            UnaryExpressionNode unary when unary.Operator != TokenType.PlusPlus => IsPureBorrowCallArgument(unary.Operand),
            CastExpressionNode cast => IsPureBorrowCallArgument(cast.Expression),
            StructInitializationNode structure => structure.Fields.Values.All(IsPureBorrowCallArgument),
            _ => false
        };

        private static void RequireValueContract(AstNode expression)
        {
            if (expression.IsReference)
                throw new CompilerException(expression, "S144",
                    "This operation has no owning or borrowing reference contract yet. Pass a field value or keep the reference in a local binding.");
        }

        private static bool CanCompleteNormally(AstNode node) => node switch
        {
            ReturnNode => false,
            BlockNode block => block.Statements.All(CanCompleteNormally),
            UnsafeBlockNode block => CanCompleteNormally(block.Body),
            IfNode conditional when conditional.ElseBranch != null =>
                CanCompleteNormally(conditional.ThenBranch) || CanCompleteNormally(conditional.ElseBranch),
            WhileNode loop when loop.Condition is LiteralNode { Value: true } => false,
            ForNode loop when loop.Condition == null || loop.Condition is LiteralNode { Value: true } => false,
            _ => true
        };

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

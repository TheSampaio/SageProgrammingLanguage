using Sage.Ast;
using Sage.Enums;
using Sage.Interfaces;
using Sage.Utilities;
using System.Numerics;
using System.Globalization;

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
        private readonly Dictionary<string, (NativeResourceDeclarationNode Node, ExternBlockNode Origin, string? File)> _nativeResources = new(StringComparer.Ordinal);
        private string _currentModule = "";
        private string? _currentReturnType;

        /// <summary>Depth of nested unsafe contexts (unsafe blocks, unsafe functions, extern signatures).</summary>
        private int _unsafeDepth;
        private int _loopDepth;
        private int _functionDepth;
        private int _borrowExpressionDepth;
        private AstNode? _owningInitializer;
        private AssignmentNode? _standaloneAssignment;
        private int _consumingArgumentDepth;

        /// <summary>True when analysis is currently inside an unsafe context.</summary>
        private bool InUnsafe => _unsafeDepth > 0;

        public void Analyze(ProgramNode ast) => ast.Accept(this);

        /// <summary>
        /// Pre-registers module-level symbols to allow forward references and out-of-order calls.
        /// </summary>
        public void RegisterModuleSymbols(ProgramNode moduleAst)
        {
            foreach (var structure in moduleAst.Statements.OfType<StructDeclarationNode>()) RegisterStructDefinition(structure);
            foreach (var external in moduleAst.Statements.OfType<ExternBlockNode>()) RegisterExternBlock(external);
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
            ValidateArrayLength(func.ReturnType, func);
            foreach (var parameter in func.Parameters) ValidateArrayLength(parameter.Type, func);
            if (TypeSystem.IsBorrowType(func.ReturnType))
                throw new CompilerException(func, "S173", "Borrowed return values require lifetime contracts that are not implemented yet.");
            if (TypeSystem.IsArrayType(func.ReturnType))
                throw new CompilerException(func, "S188", "Fixed-array returns require a value ABI that is not implemented yet.");
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
            foreach (var opaque in node.Declarations.OfType<ExternTypeNode>())
            {
                if (_nativeResources.ContainsKey(opaque.Name))
                    throw new CompilerException(opaque, "S150", $"Opaque type '{opaque.Name}' conflicts with a native resource type.");
                TypeSystem.RegisterOpaqueType(opaque.Name);
            }
            foreach (var resource in node.Declarations.OfType<NativeResourceDeclarationNode>()) RegisterNativeResource(resource, node);
            foreach (var decl in node.Declarations)
            {
                switch (decl)
                {
                    case FunctionDeclarationNode func:
                        RejectResourceValueType(func.ReturnType, func);
                        foreach (var parameter in func.Parameters) RejectResourceValueType(parameter.Type, func);
                        ValidateArrayLength(func.ReturnType, func);
                        foreach (var parameter in func.Parameters) ValidateArrayLength(parameter.Type, func);
                        if (TypeSystem.IsBorrowType(func.ReturnType) || func.Parameters.Any(parameter => TypeSystem.IsBorrowType(parameter.Type)))
                            throw new CompilerException(func, "S173", "Native ABI declarations cannot use Sage borrowing contracts.");
                        if (_functionDeclarations.ContainsKey($"{node.Alias}::{func.Name}"))
                            throw new CompilerException(func, "S150",
                                $"Native alias '{node.Alias}::{func.Name}' conflicts with a Sage callable.");
                        string name = $"{node.Alias}::{func.Name}";
                        var signature = new CallableSignature(func.ReturnType, func.Parameters.Select(parameter => parameter.Type),
                            isUnsafe: true, nativeHeader: node.Header, nativeLibrary: node.Library);
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

        private void RegisterNativeResource(NativeResourceDeclarationNode node, ExternBlockNode origin)
        {
            if (_nativeResources.TryGetValue(node.Name, out var previous))
            {
                if (ReferenceEquals(previous.Node, node)) return;
                throw new CompilerException(node, "S150", $"Resource type '{node.Name}' is already declared.");
            }
            if (TypeSystem.IsCInteropType(node.Name))
                throw new CompilerException(node, "S190", $"Resource name '{node.Name}' conflicts with a native type alias.");
            if (_structDeclarations.ContainsKey(node.Name) || TypeSystem.IsOpaqueType(node.Name) || _symbolTable.Resolve(node.Name) != null)
                throw new CompilerException(node, "S150", $"Resource type '{node.Name}' conflicts with an existing declaration.");
            _nativeResources.Add(node.Name, (node, origin, CompilerLogger.CurrentFile));
            _symbolTable.Define(node.Name, node.Name, isType: true);
        }

        /// <summary>Resolves resource contracts after every imported native declaration is registered.</summary>
        public void ValidateNativeResourceContracts()
        {
            string? previousFile = CompilerLogger.CurrentFile;
            var validated = new List<NativeResourceContract>();
            foreach (var (node, origin, file) in _nativeResources.Values)
            {
                // Keep the originating file on failure so the CLI reports the declaration that failed.
                CompilerLogger.CurrentFile = file;
                string nativeType = node.NativeType;
                string pointee = nativeType.EndsWith('*') ? nativeType[..^1] : "";
                if (pointee.Length == 0 || pointee.EndsWith('*') ||
                    !(pointee == "none" || TypeSystem.IsCInteropType(pointee) || TypeSystem.IsOpaqueType(pointee)))
                    throw new CompilerException(node, "S190", "A native resource requires one pointer to an opaque or native ABI type.");
                string binding = $"{origin.Alias}::{node.DestructorName}";
                var destructor = _symbolTable.Resolve(binding);
                var signature = destructor?.Signature;
                if (destructor is not { IsFunction: true, IsExtern: true } || signature == null ||
                    signature.ParameterTypes.Count != 1 || signature.ParameterTypes[0] != nativeType ||
                    signature.ReturnType is not ("none" or "c_void" or "c_int") ||
                    signature.NativeHeader != origin.Header || signature.NativeLibrary != origin.Library)
                    throw new CompilerException(node, "S190", $"Destructor '{binding}' must have one '{nativeType}' parameter, return none/c_void/c_int, and share the resource's native origin.");
                validated.Add(new NativeResourceContract(node.Name, nativeType, origin.Alias, node.DestructorName,
                    signature.ReturnType, origin.Header, origin.Library));
            }
            foreach (var contract in validated) TypeSystem.RegisterNativeResource(contract);
            CompilerLogger.CurrentFile = previousFile;
        }

        private static void RejectResourceValueType(string type, AstNode operation)
        {
            string valueType = TypeSystem.GetBorrowedType(type);
            if (TypeSystem.IsArrayType(valueType)) valueType = TypeSystem.GetArrayBaseType(valueType);
            valueType = valueType.TrimEnd('*');
            if (TypeSystem.IsNativeResource(valueType))
                throw new CompilerException(operation, "S191", "Native resources support local owners and explicit borrows; this value type or operation has no ownership contract yet.");
        }

        private static bool IsResourceBorrow(string type) => TypeSystem.IsBorrowType(type) &&
            TypeSystem.IsNativeResource(TypeSystem.GetBorrowedType(type));

        private void RejectResourceBindingName(string name, AstNode operation)
        {
            if (_nativeResources.ContainsKey(name))
                throw new CompilerException(operation, "S150", $"Binding '{name}' conflicts with a native resource type name.");
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
            bool returnsResource = TypeSystem.IsNativeResource(node.ReturnType);
            if (!returnsResource || node.IsExtern) RejectResourceValueType(node.ReturnType, node);
            if (returnsResource && node.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                throw new CompilerException(node, "S191", "The entry point cannot return an owning native resource.");
            if (node.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                foreach (var parameter in node.Parameters) RejectResourceValueType(parameter.Type, node);
            foreach (var parameter in node.Parameters)
                if (!IsResourceBorrow(parameter.Type) && !TypeSystem.IsNativeResource(parameter.Type)) RejectResourceValueType(parameter.Type, node);
            foreach (var parameter in node.Parameters) RejectResourceBindingName(parameter.Name, node);
            var duplicateParameter = node.Parameters.GroupBy(parameter => parameter.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicateParameter != null)
                throw new CompilerException(node, "S154", $"Parameter '{duplicateParameter.Key}' is declared more than once in '{node.Name}'.");
            foreach (var parameter in node.Parameters.Where(parameter => TypeSystem.IsBorrowType(parameter.Type)))
                if (!IsBorrowableValue(TypeSystem.GetBorrowedType(parameter.Type)))
                    throw new CompilerException(node, "S176",
                        $"Borrowed parameter '{parameter.Name}' requires a scalar, a supported struct, or a native resource; '{parameter.Type}' is not supported yet.");
            foreach (var parameter in node.Parameters.Where(parameter => TypeSystem.IsArrayType(parameter.Type) && !TypeSystem.IsBorrowType(parameter.Type)))
                RequireCopyableArray(parameter.Type, node);

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
            try { node.Body.Accept(this); }
            finally { _unsafeDepth--; }
            return "none";
        }

        public string Visit(VariableDeclarationNode node)
        {
            bool isResource = TypeSystem.IsNativeResource(node.Type);
            if (_functionDepth == 0 || (!isResource && !IsResourceBorrow(node.Type))) RejectResourceValueType(node.Type, node);
            RejectResourceBindingName(node.Name, node);
            ValidateArrayLength(node.Type, node);
            if (_symbolTable.IsDefinedInCurrentScope(node.Name))
                throw new CompilerException(node, "S102", $"Variable '{node.Name}' is already defined.");
            if (TypeSystem.IsBorrowType(node.Type)) return AnalyzeBorrowedBinding(node);

            if (isResource && node.Initializer is not (IdentifierNode or FunctionCallNode) &&
                (node.Initializer is not CastExpressionNode adoption || adoption.TargetType != node.Type))
                throw new CompilerException(node, "S192", "A resource local requires a named owner move, a direct owning call, or direct unsafe adoption from its declared native pointer.");

            if (TypeSystem.IsRawPointer(node.Type) && !InUnsafe)
                throw new CompilerException(node, "S121",
                    $"Raw pointer type '{node.Type}' can only be used inside an 'unsafe' block. Use 'new' for safe heap allocation.");

            if (node.Initializer != null)
            {
                var previousInitializer = _owningInitializer;
                _owningInitializer = node.Initializer;
                try { AnalyzeValue(node.Initializer, node.Type, "S101", $"Initializer of '{node.Name}'"); }
                finally { _owningInitializer = previousInitializer; }
                if (TypeSystem.IsArrayType(node.Type) && node.Initializer is not ArrayInitializationNode)
                {
                    if (_functionDepth == 0)
                        throw new CompilerException(node, "S188", "Array copy initialization requires a function scope.");
                    RequireCopyableArray(node.Type, node);
                }

                // A variable initialized from 'new' becomes a safe heap reference.
                node.IsReference = node.Initializer.IsReference;
            }

            if (node.Initializer is IdentifierNode source && (node.IsReference || isResource))
            {
                if (isResource && _symbolTable.Resolve(source.Name) is not { IsOwningValue: true })
                    throw new CompilerException(source, "S191", "A borrowed resource cannot initialize an owning value. Ownership stays with its source.");
                ConsumeReference(source, node, OwnershipKind.Moved);
            }

            _symbolTable.Define(node.Name, node.Type, isConstant: node.IsConstant,
                isReference: node.IsReference, declarationLoopDepth: _loopDepth);
            return node.Type;
        }

        public string Visit(BinaryExpressionNode node)
        {
            bool leftNull = IsContextualNull(node.Left);
            bool rightNull = IsContextualNull(node.Right);
            if (leftNull || rightNull)
            {
                if (leftNull == rightNull || node.Operator is not (TokenType.EqualEqual or TokenType.NotEqual))
                    throw new CompilerException(node, "S195", "Contextual null supports only == or != against a named native resource owner or borrow.");
                if (_consumingArgumentDepth > 0)
                    throw new CompilerException(node, "S194", "Evaluate resource presence into a local value before passing it alongside consumed arguments.");
                var resource = leftNull ? node.Right : node.Left;
                string type = resource.Accept(this);
                if (resource is not IdentifierNode identifier || !TypeSystem.IsNativeResource(type) ||
                    _symbolTable.ResolveLocal(identifier.Name) is not { IsFunction: false, IsType: false })
                    throw new CompilerException(node, "S195", "Contextual null requires a named native resource owner or borrow.");
                (leftNull ? node.Left : node.Right).VariableType = "null";
                node.IsResourceNullComparison = true;
                node.VariableType = "b8";
                return "b8";
            }
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

        private bool IsContextualNull(AstNode node) => node is IdentifierNode { Name: "null" } && _symbolTable.Resolve("null") == null;

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
            bool returnsResource = TypeSystem.IsNativeResource(signature.ReturnType);
            if (!returnsResource || symbol.IsExtern) RejectResourceValueType(signature.ReturnType, node);
            if (symbol.IsExtern)
                foreach (string parameter in signature.ParameterTypes) RejectResourceValueType(parameter, node);
            if (signature.IsUnsafe && !InUnsafe)
                throw new CompilerException(node, "S164", $"Calling {(symbol.IsExtern ? "native" : "unsafe")} function '{name}' requires an unsafe context.");
            if (returnsResource && (_functionDepth == 0 || !ReferenceEquals(node, _owningInitializer)))
                throw new CompilerException(node, "S193", "An owning resource result must directly initialize a local owner before it can be used.");
            if (node.Arguments.Count != signature.ParameterTypes.Count)
                throw new CompilerException(node, "S160",
                    $"Function '{name}' expects {signature.ParameterTypes.Count} arguments, but received {node.Arguments.Count}.");

            bool hasBorrowedParameters = signature.ParameterTypes.Any(TypeSystem.IsBorrowType);
            bool hasOwningParameters = signature.ParameterTypes.Any(TypeSystem.IsNativeResource);
            var borrowed = new Dictionary<SymbolMetadata, (BorrowKind Kind, BorrowExpressionNode Expression)>();
            var transferred = new HashSet<SymbolMetadata>();
            for (int index = 0; index < node.Arguments.Count; index++)
            {
                var arg = node.Arguments[index];
                string parameterType = signature.ParameterTypes[index];
                if (TypeSystem.IsBorrowType(parameterType))
                {
                    if (arg is not BorrowExpressionNode borrow)
                        throw new CompilerException(arg, "S170", $"Argument {index + 1} of '{name}' requires an explicit '{parameterType}' borrow.");
                    if (borrow.Target is IdentifierNode borrowedId && _symbolTable.ResolveLocal(borrowedId.Name) is { } binding &&
                        transferred.Contains(BorrowRoot(binding)))
                        throw new CompilerException(borrow, "S194", "A call cannot both consume and borrow the same resource.");
                    _borrowExpressionDepth++;
                    string borrowedType;
                    try { borrowedType = borrow.Accept(this); }
                    finally { _borrowExpressionDepth--; }
                    if (borrowedType != parameterType)
                        throw new CompilerException(borrow, "S170", $"Argument {index + 1} of '{name}' expects '{parameterType}', not '{borrowedType}'.");
                    var source = BorrowRoot(_symbolTable.Resolve(((IdentifierNode)borrow.Target).Name)!);
                    var kind = TypeSystem.GetBorrowKind(borrowedType);
                    if (borrowed.TryGetValue(source, out var previous) && (kind == BorrowKind.Mutable || previous.Kind == BorrowKind.Mutable))
                        throw new CompilerException(borrow, "S172",
                            $"This borrow conflicts with the borrow at ({previous.Expression.Line},{previous.Expression.Column}) in the same call.");
                    borrowed[source] = (kind, borrow);
                    continue;
                }
                if (arg is BorrowExpressionNode)
                    throw new CompilerException(arg, "S170", $"Argument {index + 1} of '{name}' expects a value, not a borrow.");
                if (TypeSystem.IsNativeResource(parameterType))
                {
                    if (arg is not IdentifierNode owner)
                        throw new CompilerException(arg, "S194", "A consuming resource argument must name a local owner. Bind producer results before the call.");
                    AnalyzeValue(owner, parameterType, "S161", $"Argument {index + 1} of '{name}'");
                    var source = _symbolTable.ResolveLocal(owner.Name);
                    if (source is not { IsOwningValue: true })
                        throw new CompilerException(owner, "S191", "A borrowed resource cannot be consumed. Ownership stays with its source.");
                    if (borrowed.ContainsKey(BorrowRoot(source)))
                        throw new CompilerException(owner, "S194", "A call cannot both consume and borrow the same resource.");
                    ConsumeReference(owner, node, OwnershipKind.Moved);
                    transferred.Add(BorrowRoot(source));
                    continue;
                }
                if ((hasBorrowedParameters || hasOwningParameters) && !IsPureBorrowCallArgument(arg))
                    throw new CompilerException(arg, hasOwningParameters ? "S194" : "S177",
                        "Evaluate calls and mutations into local values before passing them alongside borrowed or consumed arguments.");
                if (hasOwningParameters) _consumingArgumentDepth++;
                try
                {
                    if (symbol.IsExtern) arg.Accept(this);
                    else AnalyzeValue(arg, parameterType, "S161", $"Argument {index + 1} of '{name}'");
                }
                finally { if (hasOwningParameters) _consumingArgumentDepth--; }
                RequireValueContract(arg);
            }

            return symbol.Type;
        }

        public string Visit(BorrowExpressionNode node)
        {
            if (_borrowExpressionDepth == 0)
                throw new CompilerException(node, "S173", "A borrow must initialize a borrowed local or be a direct argument to a borrowed parameter.");
            if (node.Target is not IdentifierNode identifier)
                throw new CompilerException(node, "S175", "Borrow a named variable; temporary values and field-specific borrows are not supported yet.");
            string type = identifier.Accept(this);
            var source = _symbolTable.ResolveLocal(identifier.Name);
            if (source == null || source.IsFunction || !IsBorrowableValue(type))
                throw new CompilerException(node, "S176", "Only local variables or parameters with supported value types can be borrowed.");
            if (node.IsMutable && (source.IsConstant || source.Borrowing == BorrowKind.Shared))
                throw new CompilerException(node, "S171", $"Cannot mutably borrow '{identifier.Name}' through shared or constant access.");
            if (node.IsMutable) EnsureBorrowAccess(source, node, isWrite: true);
            node.VariableType = (node.IsMutable ? "&mut " : "&") + type;
            return node.VariableType;
        }

        private string AnalyzeBorrowedBinding(VariableDeclarationNode node)
        {
            if (node.Initializer is not BorrowExpressionNode borrow)
                throw new CompilerException(node, "S170", "A borrowed local requires an explicit borrow initializer.");
            _borrowExpressionDepth++;
            string type;
            try { type = borrow.Accept(this); }
            finally { _borrowExpressionDepth--; }
            if (type != node.Type)
                throw new CompilerException(node, "S170", $"Borrowed binding '{node.Name}' requires '{node.Type}', not '{type}'.");
            var source = _symbolTable.Resolve(((IdentifierNode)borrow.Target).Name)!;
            _symbolTable.Define(node.Name, TypeSystem.GetBorrowedType(type), isConstant: node.IsConstant,
                borrowing: TypeSystem.GetBorrowKind(type), loan: new BorrowLoan(source, borrow.Line, borrow.Column));
            return type;
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

        /// <summary>Checks an expression with its expected type before lowering aggregate literals.</summary>
        private string AnalyzeValue(AstNode expression, string targetType, string code, string description)
        {
            if (expression is StructInitializationNode structure)
            {
                ValidateStructFields(structure.Fields, targetType, structure);
            }
            else if (expression is ArrayInitializationNode array)
            {
                // Empty braces have no syntactic field names; context distinguishes zeroed structs and arrays.
                if (TypeSystem.IsArrayType(targetType))
                {
                    if (array.Elements.Count > TypeSystem.GetArraySize(targetType))
                        throw new CompilerException(array, "S182", $"Too many elements for '{targetType}'.");
                    string elementType = TypeSystem.GetArrayBaseType(targetType);
                    foreach (var element in array.Elements)
                    {
                        AnalyzeValue(element, elementType, "S181", "Array element");
                        RequireValueContract(element);
                    }
                }
                else if (array.Elements.Count != 0 || !_structDeclarations.ContainsKey(targetType))
                    throw new CompilerException(array, "S180", $"Array initializer cannot initialize '{targetType}'.");
            }
            else
            {
                string sourceType = expression.Accept(this);
                if (!TypeSystem.AreTypesCompatible(targetType, sourceType) &&
                    !IsAutoPromotableLiteral(targetType, expression))
                    throw new CompilerException(expression, code, $"{description} requires '{targetType}', but received '{sourceType}'.");
                if (expression is LiteralNode literal) literal.TypeName = targetType;
            }
            expression.VariableType = targetType;
            return targetType;
        }

        private void ValidateStructFields(Dictionary<string, AstNode> fields, string type, AstNode initializer)
        {
            if (!_structDeclarations.TryGetValue(type, out var structure))
                throw new CompilerException(initializer, "S180", $"Named-field initializer requires a known struct, not '{type}'.");
            foreach (var (name, value) in fields)
            {
                var field = structure.Fields.FirstOrDefault(field => field.Name == name)
                    ?? throw new CompilerException(value, "S131", $"Struct '{type}' does not contain field '{name}'.");
                if (TypeSystem.IsArrayType(field.Type) && value is not ArrayInitializationNode)
                    throw new CompilerException(value, "S188", "Initialize an array field with a literal, then copy into it by assignment.");
                AnalyzeValue(value, field.Type, "S181", $"Field '{type}.{name}'");
                RequireValueContract(value);
            }
        }

        // --- Standard Visitor Implementations ---

        public string Visit(IdentifierNode node)
        {
            var symbol = _symbolTable.Resolve(node.Name)
                ?? throw new CompilerException(node, "S105", $"Identifier '{node.Name}' not declared.");
            if (symbol.IsType) throw new CompilerException(node, "S191", $"Resource type '{node.Name}' is not a value.");
            if (symbol.IsOwningValue && symbol.Ownership.Kind != OwnershipKind.Available)
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
                    $"Owner '{node.Name}' {reason} at ({state.Line},{state.Column}) and cannot be used here.");
            }
            node.VariableType = symbol.Type;
            node.IsReference = symbol.IsReference;
            node.IsBorrowedValue = symbol.Borrowing != BorrowKind.None;
            EnsureBorrowAccess(symbol, node, isWrite: false);
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
            if (TypeSystem.IsNativeResource(targetType))
            {
                if (!ReferenceEquals(node, _standaloneAssignment) || node.Target is not IdentifierNode target ||
                    node.Expression is not IdentifierNode source)
                    throw new CompilerException(node, "S143", "Resource reassignment requires a standalone assignment between named local owners.");
                var destination = _symbolTable.ResolveLocal(target.Name);
                if (destination is not { IsOwningValue: true })
                    throw new CompilerException(target, "S143", "A borrowed resource cannot be reassigned as an owner.");
                AnalyzeValue(source, targetType, "S108", "Assignment");
                var origin = _symbolTable.ResolveLocal(source.Name);
                if (origin is not { IsOwningValue: true })
                    throw new CompilerException(source, "S191", "A borrowed resource cannot be moved into an owner.");
                if (ReferenceEquals(destination, origin))
                    throw new CompilerException(node, "S143", "A resource owner cannot be assigned to itself.");
                ConsumeReference(source, node, OwnershipKind.Moved);
                node.VariableType = targetType;
                return targetType;
            }
            if (TypeSystem.IsArrayType(targetType))
            {
                if (_functionDepth == 0) throw new CompilerException(node, "S188", "Array copy assignment requires a function scope.");
                RequireStableArrayPlace(node.Target);
                RequireCopyableArray(targetType, node);
            }
            if (node.Target.IsReference)
                throw new CompilerException(node, "S143",
                    "Reassigning an owning value is not supported yet. Create a new binding to transfer ownership.");
            AnalyzeValue(node.Expression, targetType, "S108", "Assignment");
            RequireValueContract(node.Expression);
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

        public string Visit(NativeResourceDeclarationNode node) => "none";

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
            if (_currentReturnType == null)
                throw new CompilerException(node, "S162", "A return statement must be inside a function.");
            if (TypeSystem.IsNativeResource(_currentReturnType))
            {
                if (node.Expression is not IdentifierNode owner)
                    throw new CompilerException(node, "S193", "Return a named local resource owner. Bind a producer or adoption result before returning it.");
                AnalyzeValue(owner, _currentReturnType, "S162", "Return expression");
                if (_symbolTable.Resolve(owner.Name) is not { IsOwningValue: true })
                    throw new CompilerException(owner, "S191", "A borrowed resource cannot be returned as an owner. Ownership stays with its source.");
                ConsumeReference(owner, node, OwnershipKind.Moved);
                node.VariableType = _currentReturnType;
                return _currentReturnType;
            }
            string type = AnalyzeValue(node.Expression, _currentReturnType, "S162", "Return expression");
            RequireValueContract(node.Expression);
            return type;
        }
        public string Visit(ExpressionStatementNode node)
        {
            var previous = _standaloneAssignment;
            _standaloneAssignment = node.Expression as AssignmentNode;
            try { return node.Expression.Accept(this); }
            finally { _standaloneAssignment = previous; }
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
            if (TypeSystem.IsArrayType(node.TargetType))
                throw new CompilerException(node, "S188", "A cast cannot construct a fixed array. Use an array literal or a checked value copy.");
            if (TypeSystem.GetNativeResource(node.TargetType) is { } resource)
            {
                if (!InUnsafe || _functionDepth == 0 || !ReferenceEquals(node, _owningInitializer))
                    throw new CompilerException(node, "S192", "Native resource adoption requires unsafe and must directly initialize a local owner.");
                string sourceType = node.Expression.Accept(this);
                if (sourceType != resource.NativeType)
                    throw new CompilerException(node, "S192", $"Adopting '{resource.Name}' requires its exact native pointer '{resource.NativeType}', not '{sourceType}'.");
                node.VariableType = node.TargetType;
                return node.TargetType;
            }
            RejectResourceValueType(node.TargetType, node);
            string expressionType = node.Expression.Accept(this);
            if (TypeSystem.IsArrayType(expressionType) && !TypeSystem.IsRawPointer(node.TargetType))
                throw new CompilerException(node, "S188", "A fixed array may only be cast to a raw pointer at an explicit unsafe native boundary.");
            if (TypeSystem.GetNativeResource(expressionType) is { } sourceResource)
            {
                if (!InUnsafe || node.Expression is not IdentifierNode || node.TargetType != sourceResource.NativeType)
                    throw new CompilerException(node, "S192", $"A resource handle may only be exposed from a named owner or borrow as '{sourceResource.NativeType}' inside unsafe.");
                node.VariableType = node.TargetType;
                return node.TargetType;
            }
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
            foreach (var field in node.Fields) RejectResourceValueType(field.Type, node);
            if (node.Fields.Any(field => TypeSystem.IsBorrowType(field.Type)))
                throw new CompilerException(node, "S173", "Struct fields cannot store borrowed references yet.");
            RegisterStructDefinition(node);
            _symbolTable.Define(node.Name, "struct");
            return "none";
        }

        public string Visit(StructInitializationNode node) => throw new CompilerException(node, "S180",
            "A struct initializer needs a declared destination type.");

        public string Visit(NewExpressionNode node)
        {
            if (_functionDepth == 0 || !ReferenceEquals(node, _owningInitializer))
                throw new CompilerException(node, "S146", "An allocation must directly initialize a local owning binding before its value can be used.");
            if (!_structDeclarations.ContainsKey(node.TypeName))
                throw new CompilerException(node, "S130",
                    $"'new' requires a known struct type, but '{node.TypeName}' is not a struct.");

            ValidateStructFields(node.Fields, node.TypeName, node);

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

        public string Visit(ArrayInitializationNode node) => throw new CompilerException(node, "S180",
            "An array initializer needs a declared destination type.");

        private void ConsumeReference(IdentifierNode source, AstNode operation, OwnershipKind kind)
        {
            var symbol = _symbolTable.Resolve(source.Name)!;
            EnsureBorrowAccess(symbol, operation, isWrite: true);
            if (_loopDepth > symbol.DeclarationLoopDepth)
                throw new CompilerException(operation, "S145",
                    $"Owner '{source.Name}' is declared outside this loop and cannot be moved or deleted on a potentially repeated path.");
            symbol.Ownership = new OwnershipState(kind, operation.Line, operation.Column);
        }

        private void RegisterStructDefinition(StructDeclarationNode node)
        {
            if (_nativeResources.ContainsKey(node.Name))
                throw new CompilerException(node, "S150", $"Struct '{node.Name}' conflicts with a native resource type.");
            foreach (var field in node.Fields) ValidateArrayLength(field.Type, node);
            var duplicate = node.Fields.GroupBy(field => field.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                throw new CompilerException(node, "S183", $"Field '{node.Name}.{duplicate.Key}' is declared more than once.");
            if (_structDeclarations.TryGetValue(node.Name, out var existing) && !ReferenceEquals(existing, node))
                throw new CompilerException(node, "S150", $"Struct '{node.Name}' is already declared.");
            _structDeclarations[node.Name] = node;
        }

        private bool IsBorrowableValue(string type) => TypeSystem.IsNativeResource(type) ||
            (!TypeSystem.IsArrayType(type) && TypeCapabilities.IsCopyable(type, _structDeclarations));

        private void RequireCopyableArray(string type, AstNode operation)
        {
            if (!TypeCapabilities.IsCopyable(type, _structDeclarations))
                throw new CompilerException(operation, "S188", $"Array value copying requires scalar or recursively copyable struct elements; '{type}' has no supported copy contract.");
        }

        private static bool IsStableArrayPlace(AstNode expression) => expression switch
        {
            IdentifierNode => true,
            MemberAccessNode member => IsStableArrayPlace(member.Object),
            ArrayAccessNode array => IsStableArrayPlace(array.Array),
            _ => false
        };

        private static void RequireStableArrayPlace(AstNode expression)
        {
            if (!IsStableArrayPlace(expression))
                throw new CompilerException(expression, "S189", "Bind the containing value to a local before indexing or mutating its array field.");
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
            if (symbol != null) EnsureBorrowAccess(symbol, operation, isWrite: true);
        }

        private static SymbolMetadata BorrowRoot(SymbolMetadata binding)
        {
            while (binding.Loan != null) binding = binding.Loan.Source;
            return binding;
        }

        private static bool IsBorrowAncestor(SymbolMetadata candidate, SymbolMetadata binding)
        {
            for (SymbolMetadata? current = binding; current != null; current = current.Loan?.Source)
                if (ReferenceEquals(candidate, current)) return true;
            return false;
        }

        private void EnsureBorrowAccess(SymbolMetadata binding, AstNode operation, bool isWrite)
        {
            var root = BorrowRoot(binding);
            foreach (var live in _symbolTable.ActiveBorrowBindings())
            {
                if (!ReferenceEquals(BorrowRoot(live), root) || IsBorrowAncestor(live, binding)) continue;
                if (isWrite || live.Borrowing == BorrowKind.Mutable)
                    throw new CompilerException(operation, "S178",
                        $"Access conflicts with a live borrow at ({live.Loan!.Line},{live.Loan.Column}). End its scope before accessing the source this way.");
            }
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
            if (expression.VariableType != null && TypeSystem.IsNativeResource(expression.VariableType))
                throw new CompilerException(expression, "S191", "A native resource cannot be copied or passed as an ordinary value. Move it into a local owning binding.");
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

            if (isArray) RequireStableArrayPlace(node.Array);

            if (isPointer && !InUnsafe)
                throw new CompilerException(node, "S123",
                    "Indexing a raw pointer dereferences memory and requires an 'unsafe' block.");

            string indexType = node.Index.Accept(this);
            if (!TypeSystem.IsNumeric(indexType) || TypeSystem.IsFloatingPoint(indexType))
                throw new CompilerException(node, "S112", $"Array index must be an integer, got '{indexType}'.");

            node.CheckedLength = isArray ? TypeSystem.GetArraySize(arrayType) : null;
            if (node.CheckedLength is int length && TryGetIntegerLiteral(node.Index, out var index) &&
                (index < 0 || index >= length))
                throw new CompilerException(node.Index, "S185", $"Array index {index} is outside [0,{length}).");

            node.VariableType = isArray
                ? TypeSystem.GetArrayBaseType(arrayType)
                : arrayType[..^1];

            return node.VariableType;
        }

        private static void ValidateArrayLength(string type, AstNode declaration)
        {
            if (TypeSystem.IsArrayType(type) && TypeSystem.GetArraySize(type) <= 0)
                throw new CompilerException(declaration, "S187", $"Fixed-array type '{type}' requires a positive length representable as i32.");
        }

        private static bool TryGetIntegerLiteral(AstNode expression, out BigInteger value)
        {
            if (expression is LiteralNode { TypeName: "i32" } literal)
                return BigInteger.TryParse(literal.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            if (expression is UnaryExpressionNode { Operator: TokenType.Minus } unary &&
                TryGetIntegerLiteral(unary.Operand, out value))
            {
                value = -value;
                return true;
            }
            value = default;
            return false;
        }
    }
}

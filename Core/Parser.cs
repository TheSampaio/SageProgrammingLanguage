using Sage.Ast;
using Sage.Enums;
using Sage.Interfaces;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Performs syntax analysis (parsing) on a stream of Sage tokens.
    /// Implements a Recursive Descent Parser to construct an Abstract Syntax Tree (AST).
    /// </summary>
    /// <param name="tokens">The list of tokens produced by the Lexer.</param>
    /// <param name="fileName">The name of the source file being parsed (for error reporting).</param>
    public class Parser(List<Token> tokens, string fileName) : IParser
    {
        private readonly List<Token> _tokens = tokens;
        private readonly string _fileName = fileName;
        private int _pos = 0;

        /// <summary>Gets the current token in the stream without consuming it.</summary>
        private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

        /// <summary>
        /// Helper method to initialize an AST node with its source code location.
        /// </summary>
        private static T CreateNode<T>(T node, Token token) where T : AstNode
        {
            node.Line = token.Line;
            node.Column = token.Column;
            return node;
        }

        /// <summary>
        /// Ensures the current token matches the expected type and consumes it.
        /// </summary>
        /// <exception cref="CompilerException">Thrown if the current token does not match the expected type.</exception>
        private Token Consume(TokenType type, string errorMessage = "")
        {
            if (Current.Type == type)
            {
                var t = Current;
                _pos++;
                return t;
            }

            string msg = string.IsNullOrEmpty(errorMessage)
                ? $"Expected {type} but found {Current.Type} ('{Current.Value}')"
                : errorMessage;

            throw new CompilerException(Current, "S001", msg);
        }

        /// <summary>
        /// Checks if the current token matches the given type. If so, consumes it and returns true.
        /// </summary>
        private bool Match(TokenType type)
        {
            if (Current.Type == type)
            {
                _pos++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the current token represents a known data type.
        /// This is used to detect the start of a Variable Declaration.
        /// </summary>
        /// <summary>
        /// Checks if the current token represents a known data type.
        /// Based strictly on Sage.Enums.TokenType.
        /// </summary>
        private bool IsType(Token token)
        {
            return token.Type is
                // Signed Integers
                TokenType.Type_I8 or TokenType.Type_I16 or TokenType.Type_I32 or TokenType.Type_I64 or
                // Unsigned Integers
                TokenType.Type_U8 or TokenType.Type_U16 or TokenType.Type_U32 or TokenType.Type_U64 or
                // Floating Point
                TokenType.Type_F32 or TokenType.Type_F64 or
                // Boolean & Text & Void
                TokenType.Type_B8 or
                TokenType.Type_C8 or TokenType.Type_C16 or TokenType.Type_C32 or
                TokenType.Type_Str or
                TokenType.Type_Void;
        }

        /// <summary>
        /// Entry point of the parser. Processes the token stream into a <see cref="ProgramNode"/>.
        /// </summary>
        public ProgramNode Parse()
        {
            var program = new ProgramNode { Line = 1, Column = 1 };
            while (Current.Type != TokenType.EndOfFile)
            {
                if (Current.Type == TokenType.Keyword_Module)
                    program.Statements.Add(ParseModule());
                else
                    program.Statements.Add(ParseStatement());
            }
            return program;
        }

        /// <summary>Parses a module definition and its internal functions.</summary>
        private ModuleNode ParseModule()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_Module);
            string name = Consume(TokenType.Identifier).Value;
            Consume(TokenType.OpenBrace);

            var module = CreateNode(new ModuleNode(name), startToken);

            while (Current.Type != TokenType.CloseBrace && Current.Type != TokenType.EndOfFile)
            {
                // 'unsafe' inside a module always introduces an unsafe function (modules hold no statements).
                if (Current.Type == TokenType.Keyword_Func || Current.Type == TokenType.Keyword_Unsafe)
                {
                    module.Members.Add(ParseFunction(module.Name));
                }
                else if (Current.Type == TokenType.Keyword_Extern)
                {
                    module.Members.Add(ParseExternBlock());
                }
                else
                {
                    throw new CompilerException(Current, "S002", "Only functions and extern blocks are supported in modules.");
                }
            }

            Consume(TokenType.CloseBrace);
            return module;
        }

        /// <summary>
        /// Parses an FFI block: extern alias("header") { ... }
        /// </summary>
        private ExternBlockNode ParseExternBlock()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_Extern);

            // 1. Alias (namespace local)
            string alias = Consume(TokenType.Identifier).Value;

            // 2. Header File ("stdio.h"), plus an optional link library ("ws2_32").
            Consume(TokenType.OpenParen);
            string header = Consume(TokenType.String).Value;
            string? library = null;
            if (Match(TokenType.Comma))
            {
                library = Consume(TokenType.String, "Expected a link-library name string after ','.").Value;
            }
            Consume(TokenType.CloseParen);

            Consume(TokenType.OpenBrace);

            var declarations = new List<AstNode>();

            while (Current.Type != TokenType.CloseBrace && Current.Type != TokenType.EndOfFile)
            {
                // Opaque C type: 'type FILE;' — introduces a named C type without a Sage-side layout.
                // 'type' is a contextual keyword recognized only inside extern blocks.
                if (Current.Type == TokenType.Identifier && Current.Value == "type")
                {
                    _pos++; // consume 'type'
                    string typeName = Consume(TokenType.Identifier, "Expected an opaque type name after 'type'.").Value;
                    Consume(TokenType.Semicolon, "Opaque type declarations end with ';'.");
                    declarations.Add(CreateNode(new ExternTypeNode(typeName), startToken));
                    continue;
                }

                if (Match(TokenType.Keyword_Func))
                {
                    // The extern function is registered by the SemanticAnalyzer as alias::funcName.
                    string funcName = Consume(TokenType.Identifier).Value;
                    var parameters = ParseParameterList();
                    Consume(TokenType.Colon);
                    string retType = ConsumeType();
                    Consume(TokenType.Semicolon); // Externs have no body.

                    var funcNode = new FunctionDeclarationNode(funcName, retType, parameters, null!, alias)
                    {
                        IsExtern = true
                    };
                    declarations.Add(CreateNode(funcNode, startToken));
                }
                else
                {
                    throw new CompilerException(Current, "S006", "Only 'func' and 'type' declarations are allowed inside extern blocks.");
                }
            }

            Consume(TokenType.CloseBrace);
            return CreateNode(new ExternBlockNode(alias, header, declarations, library), startToken);
        }

        /// <summary>Parses a single statement or declaration.</summary>
        private AstNode ParseStatement()
        {
            var startToken = Current;

            // 1. Import directive
            if (Match(TokenType.Keyword_Use))
            {
                string name = Consume(TokenType.Identifier).Value;
                Consume(TokenType.Semicolon);
                return CreateNode(new UseNode(name), startToken);
            }

            // 2. Function definition / top-level FFI block
            if (Current.Type == TokenType.Keyword_Func) return ParseFunction("");
            if (Current.Type == TokenType.Keyword_Extern) return ParseExternBlock();

            // 2b. Unsafe: either an 'unsafe { }' block or an 'unsafe func' definition.
            if (Current.Type == TokenType.Keyword_Unsafe)
            {
                var next = _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : _tokens[^1];
                return next.Type == TokenType.Keyword_Func ? ParseFunction("") : ParseUnsafeBlock();
            }

            // 3. Control Flow
            if (Current.Type == TokenType.Keyword_If) return ParseIf();
            if (Current.Type == TokenType.Keyword_While) return ParseWhile();
            if (Current.Type == TokenType.Keyword_For) return ParseFor();

            if (Match(TokenType.Keyword_Return))
            {
                var expr = ParseExpression();
                Consume(TokenType.Semicolon);
                return CreateNode(new ReturnNode(expr), startToken);
            }

            // Releases a safe heap reference obtained from 'new'.
            if (Match(TokenType.Keyword_Delete))
            {
                var target = ParseExpression();
                Consume(TokenType.Semicolon);
                return CreateNode(new DeleteNode(target), startToken);
            }

            if (Current.Type == TokenType.Keyword_Struct) return ParseStructDeclaration();

            // 4. Variable Declaration
            // Verifica se começa com 'var' ou 'const'
            if (Current.Type == TokenType.Keyword_Var || Current.Type == TokenType.Keyword_Const)
            {
                return ParseVariableDeclaration();
            }

            // 5. Expression Statement (Assignments, Calls)
            // Ex: x = 1; (Assignment usa Equals, mas é parseado dentro de ParseExpression -> ParseAssignment)
            var expression = ParseExpression();
            Consume(TokenType.Semicolon);
            return CreateNode(new ExpressionStatementNode(expression), startToken);
        }

        /// <summary>Parses an 'if' statement, including optional 'else' blocks.</summary>
        private IfNode ParseIf()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_If);
            Consume(TokenType.OpenParen);
            var condition = ParseExpression();
            Consume(TokenType.CloseParen);

            var thenBranch = ParseBlock();
            AstNode? elseBranch = null;

            if (Match(TokenType.Keyword_Else))
            {
                // Support to 'else if' recursive
                if (Current.Type == TokenType.Keyword_If)
                {
                    elseBranch = ParseIf();
                }
                else
                {
                    elseBranch = ParseBlock();
                }
            }

            return CreateNode(new IfNode(condition, thenBranch, elseBranch), startToken);
        }

        /// <summary>Parses a 'while' loop.</summary>
        private WhileNode ParseWhile()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_While);
            Consume(TokenType.OpenParen);
            var condition = ParseExpression();
            Consume(TokenType.CloseParen);

            var body = ParseBlock();
            return CreateNode(new WhileNode(condition, body), startToken);
        }

        /// <summary>Parses a 'for' loop: for(init; condition; increment) { ... }</summary>
        private ForNode ParseFor()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_For);
            Consume(TokenType.OpenParen);

            // 1. Initializer (can be empty, a declaration, or an expression)
            AstNode? initializer = null;
            if (Current.Type != TokenType.Semicolon)
            {
                // FIX: Check for 'var' or 'const' instead of IsType
                if (Current.Type == TokenType.Keyword_Var || Current.Type == TokenType.Keyword_Const)
                {
                    initializer = ParseVariableDeclaration();
                    // Note: ParseVariableDeclaration() already consumes the trailing ';'
                }
                else
                {
                    initializer = ParseExpression();
                    Consume(TokenType.Semicolon);
                }
            }
            else
            {
                Consume(TokenType.Semicolon); // Empty initializer
            }

            // 2. Condition
            AstNode? condition = null;
            if (Current.Type != TokenType.Semicolon)
            {
                condition = ParseExpression();
            }
            Consume(TokenType.Semicolon);

            // 3. Increment
            AstNode? increment = null;
            if (Current.Type != TokenType.CloseParen)
            {
                increment = ParseExpression();
            }
            Consume(TokenType.CloseParen);

            var body = ParseBlock();

            return CreateNode(new ForNode(initializer, condition, increment, body), startToken);
        }

        /// <summary>
        /// Parses a variable declaration: var name: type = value;
        /// </summary>
        private VariableDeclarationNode ParseVariableDeclaration()
        {
            var startToken = Current;
            bool isConstant = false;

            // 1. Consume Keyword (var / const)
            if (Match(TokenType.Keyword_Const))
            {
                isConstant = true;
            }
            else
            {
                Consume(TokenType.Keyword_Var, "Expected 'var' or 'const'.");
            }

            // 2. Identifier Name
            string name = Consume(TokenType.Identifier).Value;

            // 3. Type Annotation
            Consume(TokenType.Colon, "Expected ':' after variable name.");
            string type = ConsumeType(); // Retorna "str", "i32", "none*", etc.

            // 4. Initializer
            AstNode? initializer = null;
            if (Match(TokenType.Equals))
            {
                initializer = ParseExpression();
            }
            else if (!type.Contains('[')) // Arrays não exigem inicialização!
            {
                throw new CompilerException(startToken, "S012", "Standard variables must be initialized.");
            }

            Consume(TokenType.Semicolon);

            return CreateNode(new VariableDeclarationNode(name, type, initializer, isConstant), startToken);
        }

        /// <summary>Parses an 'unsafe { }' block, a scoped region where raw pointer operations are allowed.</summary>
        private UnsafeBlockNode ParseUnsafeBlock()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_Unsafe);
            var body = ParseBlock();
            return CreateNode(new UnsafeBlockNode(body), startToken);
        }

        /// <summary>Parses a parenthesized formal parameter list: (name: type, name: type, ...).</summary>
        private List<ParameterNode> ParseParameterList()
        {
            Consume(TokenType.OpenParen);

            var parameters = new List<ParameterNode>();
            if (Current.Type != TokenType.CloseParen)
            {
                do
                {
                    string pName = Consume(TokenType.Identifier).Value;
                    Consume(TokenType.Colon);
                    string pType = ConsumeType();
                    parameters.Add(new ParameterNode(pName, pType));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.CloseParen);
            return parameters;
        }

        /// <summary>Parses function declarations, including 'unsafe' and external C interop functions.</summary>
        private FunctionDeclarationNode ParseFunction(string moduleOwner)
        {
            var startToken = Current;
            bool isUnsafe = Match(TokenType.Keyword_Unsafe);
            bool isExtern = Match(TokenType.Keyword_Extern);
            Consume(TokenType.Keyword_Func);

            string name = Consume(TokenType.Identifier).Value;
            var parameters = ParseParameterList();
            Consume(TokenType.Colon);
            string retType = ConsumeType();

            if (isExtern)
            {
                Consume(TokenType.Semicolon);
                return CreateNode(new FunctionDeclarationNode(name, retType, parameters, null!, moduleOwner)
                { IsExtern = true, IsUnsafe = isUnsafe }, startToken);
            }

            return CreateNode(new FunctionDeclarationNode(name, retType, parameters, ParseBlock(), moduleOwner)
            { IsUnsafe = isUnsafe }, startToken);
        }

        /// <summary>Parses a scoped block of code enclosed in braces.</summary>
        private BlockNode ParseBlock()
        {
            var startToken = Current;
            Consume(TokenType.OpenBrace);

            var statements = new List<AstNode>();
            while (Current.Type != TokenType.CloseBrace && Current.Type != TokenType.EndOfFile)
            {
                statements.Add(ParseStatement());
            }

            Consume(TokenType.CloseBrace);
            // Cria o nó já com a lista completa, garantindo imutabilidade se necessário
            return CreateNode(new BlockNode(statements), startToken);
        }

        /// <summary>
        /// Consumes a type token and handles pointer syntax (e.g., "i32", "none*", "u8**").
        /// </summary>
        private string ConsumeType()
        {
            if (!IsType(Current) && Current.Type != TokenType.Identifier)
                throw new CompilerException(Current, "S003", $"Expected a type but found {Current.Type} ('{Current.Value}')");

            string type = Current.Value;
            _pos++;

            // Pointers
            while (Current.Type == TokenType.Asterisk)
            {
                type += "*";
                _pos++;
            }

            // Arrays
            if (Match(TokenType.OpenBracket))
            {
                var sizeToken = Consume(TokenType.Integer, "Array type must specify a fixed size. Example: i32[4]");
                Consume(TokenType.CloseBracket, "Expected ']' after array size.");
                type += $"[{sizeToken.Value}]";
            }

            return type;
        }

        // --- Expression Hierarchy (Operator Precedence) ---

        private AstNode ParseExpression() => ParseAssignment();

        private AstNode ParseAssignment()
        {
            var expr = ParseLogicalOr();
            if (Match(TokenType.Equals))
            {
                var opToken = Current;
                var value = ParseAssignment();
                if (expr is IdentifierNode || expr is MemberAccessNode || expr is ArrayAccessNode)
                    return CreateNode(new AssignmentNode(expr, value), opToken);

                throw new CompilerException(opToken, "S005", "Invalid assignment target.");
            }
            return expr;
        }

        private AstNode ParseLogicalOr()
        {
            var left = ParseLogicalAnd();
            while (Current.Type == TokenType.PipePipe)
            {
                var opToken = Current; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, TokenType.PipePipe, ParseLogicalAnd()), opToken);
            }
            return left;
        }

        private AstNode ParseLogicalAnd()
        {
            var left = ParseEquality();
            while (Current.Type == TokenType.AmpersandAmpersand)
            {
                var opToken = Current; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, TokenType.AmpersandAmpersand, ParseEquality()), opToken);
            }
            return left;
        }

        private AstNode ParseEquality()
        {
            var left = ParseRelational();
            while (Current.Type == TokenType.EqualEqual || Current.Type == TokenType.NotEqual)
            {
                var opToken = Current;
                var op = Current.Type; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, op, ParseRelational()), opToken);
            }
            return left;
        }

        private AstNode ParseRelational()
        {
            var left = ParseAdditive();
            while (Current.Type == TokenType.Less || Current.Type == TokenType.LessEqual ||
                   Current.Type == TokenType.Greater || Current.Type == TokenType.GreaterEqual)
            {
                var opToken = Current;
                var op = Current.Type; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, op, ParseAdditive()), opToken);
            }
            return left;
        }

        private AstNode ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
            {
                var opToken = Current;
                var op = Current.Type; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, op, ParseMultiplicative()), opToken);
            }
            return left;
        }

        private AstNode ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Current.Type == TokenType.Asterisk || Current.Type == TokenType.Slash || Current.Type == TokenType.Percent)
            {
                var opToken = Current;
                var op = Current.Type; _pos++;
                left = CreateNode(new BinaryExpressionNode(left, op, ParseUnary()), opToken);
            }
            return left;
        }

        private AstNode ParseUnary()
        {
            if (Current.Type == TokenType.Bang || Current.Type == TokenType.Minus)
            {
                var opToken = Current;
                var op = Current.Type; _pos++;
                return CreateNode(new UnaryExpressionNode(op, ParseUnary()), opToken);
            }
            return ParsePostfix();
        }

        private AstNode ParsePostfix()
        {
            var left = ParseCastExpression();
            if (Current.Type == TokenType.PlusPlus)
            {
                var opToken = Current; _pos++;
                return CreateNode(new UnaryExpressionNode(TokenType.PlusPlus, left, isPostfix: true), opToken);
            }
            return left;
        }

        private AstNode ParseCastExpression()
        {
            var expr = ParseMemberAccess();

            while (Current.Type == TokenType.Keyword_As)
            {
                var opToken = Current; _pos++;
                expr = CreateNode(new CastExpressionNode(expr, ConsumeType()), opToken);
            }

            return expr;
        }

        /// <summary>Parses primary expressions: literals, identifiers, and parenthesized expressions.</summary>
        private AstNode ParsePrimary()
        {
            var startToken = Current;

            if (Match(TokenType.Keyword_True)) return CreateNode(new LiteralNode(true, "b8"), startToken);
            if (Match(TokenType.Keyword_False)) return CreateNode(new LiteralNode(false, "b8"), startToken);
            if (Current.Type == TokenType.Keyword_New) return ParseNewExpression();
            if (Current.Type == TokenType.Integer) return CreateNode(new LiteralNode(Consume(TokenType.Integer).Value, "i32"), startToken);
            if (Current.Type == TokenType.Float) return CreateNode(new LiteralNode(Consume(TokenType.Float).Value, "f64"), startToken);

            if (Current.Type == TokenType.String)
            {
                string value = Consume(TokenType.String).Value;

                if (value.Contains('{') && value.Contains('}'))
                {
                    var interpNode = CreateNode(new InterpolatedStringNode(), startToken);
                    int lastClose = 0;

                    for (int i = 0; i < value.Length; i++)
                    {
                        if (value[i] == '{')
                        {
                            if (i > lastClose)
                            {
                                interpNode.Parts.Add(CreateNode(new LiteralNode(value[lastClose..i], "str"), startToken));
                            }

                            int end = value.IndexOf('}', i);
                            if (end == -1) throw new CompilerException(startToken, "S010", "Unclosed interpolation brace '}'.");

                            string exprStr = value[(i + 1)..end];
                            if (string.IsNullOrWhiteSpace(exprStr))
                                throw new CompilerException(startToken, "S011", "Empty interpolation expression.");

                            string? format = null;
                            int lastColonIdx = exprStr.LastIndexOf(':');

                            if (lastColonIdx != -1 && (lastColonIdx == 0 || exprStr[lastColonIdx - 1] != ':'))
                            {
                                format = exprStr[(lastColonIdx + 1)..];
                                exprStr = exprStr[..lastColonIdx];
                            }

                            var subLexer = new Lexer(exprStr);
                            var subTokens = subLexer.Tokenize();
                            var subParser = new Parser(subTokens, _fileName);

                            var parsedExpr = subParser.ParseExpression();
                            parsedExpr.FormatSpecifier = format;

                            interpNode.Parts.Add(parsedExpr);

                            i = end;
                            lastClose = end + 1;
                        }
                    }

                    if (lastClose < value.Length)
                    {
                        interpNode.Parts.Add(CreateNode(new LiteralNode(value[lastClose..], "str"), startToken));
                    }

                    return interpNode;
                }

                return CreateNode(new LiteralNode(value, "str"), startToken);
            }

            if (Current.Type == TokenType.Identifier)
            {
                string name = Consume(TokenType.Identifier).Value;
                while (Match(TokenType.DoubleColon)) name += "::" + Consume(TokenType.Identifier).Value;

                if (Match(TokenType.OpenParen))
                {
                    var args = new List<AstNode>();
                    if (Current.Type != TokenType.CloseParen)
                    {
                        do { args.Add(ParseExpression()); } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.CloseParen);
                    return CreateNode(new FunctionCallNode(name, args), startToken);
                }
                return CreateNode(new IdentifierNode(name), startToken);
            }

            if (Match(TokenType.OpenParen))
            {
                var expr = ParseExpression();
                Consume(TokenType.CloseParen);
                return expr;
            }

            // Struct Initialization: { field = value, field2 = value2 }
            if (Match(TokenType.OpenBrace))
            {
                Token tokenInside = Current;
                Token tokenAfterInside = _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : _tokens[^1];

                if (tokenInside.Type == TokenType.Identifier && tokenAfterInside.Type == TokenType.Equals)
                {
                    var fields = new Dictionary<string, AstNode>();

                    if (Current.Type != TokenType.CloseBrace)
                    {
                        do
                        {
                            string fieldName = Consume(TokenType.Identifier).Value;
                            Consume(TokenType.Equals, "Expected '=' after field name.");
                            fields[fieldName] = ParseExpression();

                        } while (Match(TokenType.Comma));
                    }

                    Consume(TokenType.CloseBrace, "Expected '}' to close struct initialization.");
                    return CreateNode(new StructInitializationNode(fields), startToken);
                }
                else
                {
                    var elements = new List<AstNode>();
                    if (Current.Type != TokenType.CloseBrace)
                    {
                        do
                        {
                            elements.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.CloseBrace, "Expected '}' to close array initialization.");
                    return CreateNode(new ArrayInitializationNode(elements), startToken);
                }
            }

            throw new CompilerException(Current, "S004", $"Unexpected token: {Current.Type}");
        }

        /// <summary>
        /// Parses a 'new' expression: new TypeName { field = value, ... }.
        /// Allocates a value on the heap and yields a memory-safe reference.
        /// </summary>
        private NewExpressionNode ParseNewExpression()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_New);

            string typeName = Consume(TokenType.Identifier, "Expected a type name after 'new'.").Value;
            Consume(TokenType.OpenBrace, "Expected '{' to initialize the value created with 'new'.");

            var fields = new Dictionary<string, AstNode>();
            if (Current.Type != TokenType.CloseBrace)
            {
                do
                {
                    string fieldName = Consume(TokenType.Identifier).Value;
                    Consume(TokenType.Equals, "Expected '=' after field name.");
                    fields[fieldName] = ParseExpression();
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.CloseBrace, "Expected '}' to close the 'new' initialization.");
            return CreateNode(new NewExpressionNode(typeName, fields), startToken);
        }

        private StructDeclarationNode ParseStructDeclaration()
        {
            var startToken = Current;
            Consume(TokenType.Keyword_Struct);
            string name = Consume(TokenType.Identifier).Value;
            Consume(TokenType.OpenBrace);

            var fields = new List<VariableDeclarationNode>();
            while (Current.Type != TokenType.CloseBrace && Current.Type != TokenType.EndOfFile)
            {
                string fieldName = Consume(TokenType.Identifier).Value;
                Consume(TokenType.Colon);
                string fieldType = ConsumeType();
                Consume(TokenType.Semicolon);

                fields.Add(new VariableDeclarationNode(fieldName, fieldType, null!, false));
            }
            Consume(TokenType.CloseBrace);

            return CreateNode(new StructDeclarationNode(name, fields), startToken);
        }

        private AstNode ParseMemberAccess()
        {
            var left = ParsePrimary();
            while (true)
            {
                if (Match(TokenType.Dot))
                {
                    var opToken = Current;
                    string propertyName = Consume(TokenType.Identifier, "Expected property name.").Value;
                    left = CreateNode(new MemberAccessNode(left, propertyName), opToken);
                }
                else if (Match(TokenType.OpenBracket))
                {
                    var opToken = Current;
                    var index = ParseExpression();
                    Consume(TokenType.CloseBracket, "Expected ']' after array index.");
                    left = CreateNode(new ArrayAccessNode(left, index), opToken);
                }
                else break;
            }
            return left;
        }
    }
}
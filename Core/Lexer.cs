using System.Text;
using Sage.Enums;
using Sage.Interfaces;
using Sage.Utilities;

namespace Sage.Core
{
    /// <summary>
    /// Performs lexical analysis on Sage source code.
    /// This class scans the raw input text and converts it into a sequence of <see cref="Token"/> objects
    /// based on the Sage language grammar.
    /// </summary>
    /// <param name="text">The raw source code to be tokenized.</param>
    public class Lexer(string text) : ILexer
    {
        private readonly string _text = text;
        private int _pos = 0;
        private int _line = 1;
        private int _col = 1;

        /// <summary>Gets the character at the current scanner position.</summary>
        private char Current => _pos < _text.Length ? _text[_pos] : '\0';

        /// <summary>Peeks at the character immediately following the current position.</summary>
        private char Lookahead => _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';

        /// <summary>
        /// Internal map of Sage keywords and built-in types to their respective <see cref="TokenType"/>.
        /// </summary>
        private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "use", TokenType.Keyword_Use },
        { "extern", TokenType.Keyword_Extern },
        { "func", TokenType.Keyword_Func },
        { "return", TokenType.Keyword_Return },
        { "module", TokenType.Keyword_Module },
        { "var", TokenType.Keyword_Var },
        { "const", TokenType.Keyword_Const },
        { "as", TokenType.Keyword_As },
        { "true", TokenType.Keyword_True },
        { "false", TokenType.Keyword_False },
        { "if", TokenType.Keyword_If },
        { "else", TokenType.Keyword_Else },
        { "while", TokenType.Keyword_While },
        { "for", TokenType.Keyword_For },
        { "struct", TokenType.Keyword_Struct },
        { "unsafe", TokenType.Keyword_Unsafe },
        { "new", TokenType.Keyword_New },
        { "delete", TokenType.Keyword_Delete },
        // Types
        { "i8", TokenType.Type_I8 }, { "u8", TokenType.Type_U8 },
        { "i16", TokenType.Type_I16 }, { "u16", TokenType.Type_U16 },
        { "i32", TokenType.Type_I32 }, { "u32", TokenType.Type_U32 },
        { "i64", TokenType.Type_I64 }, { "u64", TokenType.Type_U64 },
        { "f32", TokenType.Type_F32 }, { "f64", TokenType.Type_F64 },
        { "b8", TokenType.Type_B8 }, { "str", TokenType.Type_Str },
        { "none", TokenType.Type_Void },
    };

        /// <summary>
        /// Scans the entire source text and produces a list of valid tokens.
        /// The stream always concludes with an <c>EndOfFile</c> token.
        /// </summary>
        /// <returns>A list of identified <see cref="Token"/> objects.</returns>
        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (_pos < _text.Length)
            {
                char c = Current;

                if (char.IsWhiteSpace(c))
                {
                    Advance();
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    tokens.Add(LexIdentifier());
                    continue;
                }

                if (char.IsDigit(c))
                {
                    tokens.Add(LexNumber());
                    continue;
                }

                if (c == '"')
                {
                    tokens.Add(LexString());
                    continue;
                }

                Token? symbol = LexSymbol();
                if (symbol != null) tokens.Add(symbol);
            }

            tokens.Add(new Token(TokenType.EndOfFile, "\0", _line, _col));
            return tokens;
        }

        /// <summary>
        /// Advances the current position of the scanner and updates line/column trackers.
        /// </summary>
        private void Advance()
        {
            if (_pos < _text.Length)
            {
                if (_text[_pos] == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else
                {
                    _col++;
                }
                _pos++;
            }
        }

        /// <summary>
        /// Consumes an alphanumeric identifier and determines if it is a keyword or a user-defined name.
        /// </summary>
        /// <returns>A token of type <c>Identifier</c> or a specific <c>Keyword</c> type.</returns>
        private Token LexIdentifier()
        {
            int startPos = _pos;
            int startCol = _col;

            while (_pos < _text.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
            {
                Advance();
            }

            string value = _text[startPos.._pos];
            var type = Keywords.GetValueOrDefault(value, TokenType.Identifier);

            return new Token(type, value, _line, startCol);
        }

        /// <summary>
        /// Consumes a numeric sequence and distinguishes between <c>Integer</c> and <c>Float</c> literals.
        /// </summary>
        /// <returns>A numeric token.</returns>
        private Token LexNumber()
        {
            int startPos = _pos;
            int startCol = _col;

            while (_pos < _text.Length && char.IsDigit(Current)) Advance();

            // Support for floating point notation
            if (Current == '.' && char.IsDigit(Lookahead))
            {
                Advance();
                while (_pos < _text.Length && char.IsDigit(Current)) Advance();
                return new Token(TokenType.Float, _text[startPos.._pos], _line, startCol);
            }

            return new Token(TokenType.Integer, _text[startPos.._pos], _line, startCol);
        }

        /// <summary>
        /// Consumes a string literal enclosed in double quotes.
        /// </summary>
        /// <returns>A token containing the string content without the quotes.</returns>
        private Token LexString()
        {
            int startCol = _col;
            int startLine = _line;

            Advance(); // Skip opening "

            var sb = new StringBuilder();
            while (_pos < _text.Length && Current != '"')
            {
                // Preserve escape sequences verbatim so the C backend interprets them
                // (Sage string escapes mirror C escapes). This also stops an escaped
                // quote (\") from terminating the literal prematurely.
                if (Current == '\\')
                {
                    sb.Append(Current);
                    Advance();
                    if (_pos < _text.Length)
                    {
                        sb.Append(Current);
                        Advance();
                    }
                    continue;
                }

                sb.Append(Current);
                Advance();
            }

            if (_pos >= _text.Length)
            {
                CompilerLogger.LogError(new Token(TokenType.String, sb.ToString(), startLine, startCol),
                    "S013", "Unterminated string literal.");
            }
            else
            {
                Advance(); // Skip closing "
            }

            return new Token(TokenType.String, sb.ToString(), startLine, startCol);
        }

        /// <summary>
        /// Processes symbols, multi-character operators, and skips comments.
        /// </summary>
        /// <returns>A symbolic token, or <c>null</c> if a comment was skipped.</returns>
        private Token? LexSymbol()
        {
            char c = Current;
            char next = Lookahead;
            int startCol = _col;

            // Skip single-line comments
            if (c == '/' && next == '/')
            {
                while (_pos < _text.Length && Current != '\n') Advance();
                return null;
            }

            // Multi-character Operator Resolution
            if (c == ':' && next == ':') { Advance(); Advance(); return new Token(TokenType.DoubleColon, "::", _line, startCol); }
            if (c == '=' && next == '=') { Advance(); Advance(); return new Token(TokenType.EqualEqual, "==", _line, startCol); }
            if (c == '!' && next == '=') { Advance(); Advance(); return new Token(TokenType.NotEqual, "!=", _line, startCol); }
            if (c == '<' && next == '=') { Advance(); Advance(); return new Token(TokenType.LessEqual, "<=", _line, startCol); }
            if (c == '>' && next == '=') { Advance(); Advance(); return new Token(TokenType.GreaterEqual, ">=", _line, startCol); }
            if (c == '&' && next == '&') { Advance(); Advance(); return new Token(TokenType.AmpersandAmpersand, "&&", _line, startCol); }
            if (c == '|' && next == '|') { Advance(); Advance(); return new Token(TokenType.PipePipe, "||", _line, startCol); }
            if (c == '+' && next == '+') { Advance(); Advance(); return new Token(TokenType.PlusPlus, "++", _line, startCol); }

            // Single-character Operator Resolution
            Advance();
            TokenType type = c switch
            {
                '+' => TokenType.Plus,
                '-' => TokenType.Minus,
                '*' => TokenType.Asterisk,
                '/' => TokenType.Slash,
                '%' => TokenType.Percent,
                '=' => TokenType.Equals,
                '(' => TokenType.OpenParen,
                ')' => TokenType.CloseParen,
                '{' => TokenType.OpenBrace,
                '}' => TokenType.CloseBrace,
                '[' => TokenType.OpenBracket,
                ']' => TokenType.CloseBracket,
                ';' => TokenType.Semicolon,
                ',' => TokenType.Comma,
                ':' => TokenType.Colon,
                '.' => TokenType.Dot,
                '<' => TokenType.Less,
                '>' => TokenType.Greater,
                '!' => TokenType.Bang,
                _ => TokenType.Unknown
            };

            if (type == TokenType.Unknown)
            {
                CompilerLogger.LogError(new Token(type, c.ToString(), _line, startCol), "S000", $"Unexpected character '{c}'.");
            }

            return new Token(type, c.ToString(), _line, startCol);
        }
    }
}
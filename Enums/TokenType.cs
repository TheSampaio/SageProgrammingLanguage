namespace Sage.Enums
{
    /// <summary>
    /// Defines all valid token types recognized by the Sage Lexer and Parser.
    /// Organized by functional categories for architectural clarity.
    /// </summary>
    public enum TokenType
    {
        // --- Keywords ---
        Keyword_Use,
        Keyword_Extern,
        Keyword_Func,
        Keyword_Return,
        Keyword_Module,
        Keyword_Var,
        Keyword_Const,
        Keyword_As,
        Keyword_True,
        Keyword_False,
        Keyword_If,
        Keyword_Else,
        Keyword_While,
        Keyword_For,
        Keyword_Struct,
        Keyword_Unsafe,
        Keyword_New,
        Keyword_Delete,

        // --- Signed Integers ---
        Type_I8,
        Type_I16,
        Type_I32,
        Type_I64,

        // --- Unsigned Integers ---
        Type_U8,
        Type_U16,
        Type_U32,
        Type_U64,

        // --- Floating Point ---
        Type_F32,
        Type_F64,

        // --- Boolean & Text Types ---
        Type_B8,      // 8-bit Boolean
        Type_C8,      // 8-bit Character
        Type_C16,     // 16-bit Character
        Type_C32,     // 32-bit Character
        Type_Str,     // String pointer
        Type_Void,    // Represents "none" or no return value

        // --- Arithmetic Operators ---
        Plus,         // +
        PlusPlus,     // ++
        Minus,        // -
        Asterisk,     // *
        Slash,        // /
        Percent,      // %
        Equals,       // =

        // --- Comparison & Logical Operators ---
        EqualEqual,   // ==
        NotEqual,     // !=
        Less,         // <
        LessEqual,    // <=
        Greater,      // >
        GreaterEqual, // >=
        Bang,         // !
        AmpersandAmpersand, // &&
        PipePipe,     // ||

        // --- Punctuation & Delimiters ---
        OpenParen,    // (
        CloseParen,   // )
        OpenBrace,    // {
        CloseBrace,   // }
        OpenBracket,  // [
        CloseBracket, // ]
        Semicolon,    // ;
        Comma,        // ,
        Colon,        // :
        DoubleColon,  // ::
        Dot,          // .
        Arrow,        // ->

        // --- Literals & Identifiers ---
        Identifier,   // User-defined names
        Integer,      // Numeric literal
        Float,        // Floating-point literal
        String,       // String literal

        // --- Special ---
        Unknown,      // Error fallback
        EndOfFile     // End of token stream (\0)
    }
}
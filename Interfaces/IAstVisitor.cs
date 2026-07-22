using Sage.Ast;

namespace Sage.Interfaces
{
    /// <summary>
    /// Defines a visitor for the Sage Abstract Syntax Tree (AST).
    /// Implements the Visitor Pattern to decouple node structures from their processing logic,
    /// such as code generation, semantic analysis, or optimization.
    /// </summary>
    /// <typeparam name="out T">The return type of the visit operations.</typeparam>
    public interface IAstVisitor<out T>
    {
        // --- Structural Nodes ---

        /// <summary>Visits the root node representing the entire Sage program.</summary>
        T Visit(ProgramNode node);

        /// <summary>Visits a module or namespace definition.</summary>
        T Visit(ModuleNode node);

        /// <summary>Visits a function declaration, including its signature and implementation body.</summary>
        T Visit(FunctionDeclarationNode node);

        /// <summary>Visits a block of code scoped within braces {}.</summary>
        T Visit(BlockNode node);

        /// <summary>Visits an <c>unsafe { }</c> block that permits raw pointer operations.</summary>
        T Visit(UnsafeBlockNode node);

        // --- Statement Nodes ---

        /// <summary>Visits a variable or constant declaration.</summary>
        T Visit(VariableDeclarationNode node);

        /// <summary>Visits an assignment operation where a value is bound to a target.</summary>
        T Visit(AssignmentNode node);

        /// <summary>Visits a return statement.</summary>
        T Visit(ReturnNode node);

        /// <summary>Visits a standalone expression acting as a statement.</summary>
        T Visit(ExpressionStatementNode node);

        /// <summary>Visits a block containing external declarations, typically for C integration.</summary>
        T Visit(ExternBlockNode node);

        /// <summary>Visits a 'use' directive used for importing modules.</summary>
        T Visit(UseNode node);

        /// <summary>Visits a <c>delete</c> statement that releases a safe heap reference.</summary>
        T Visit(DeleteNode node);

        /// <summary>Visits an opaque C type declaration (<c>type Name;</c>) inside an extern block.</summary>
        T Visit(ExternTypeNode node);

        // --- Control Flow Nodes ---

        /// <summary>Visits an if-else conditional branching structure.</summary>
        T Visit(IfNode node);

        /// <summary>Visits a while loop structure.</summary>
        T Visit(WhileNode node);

        /// <summary>Visits a for loop structure.</summary>
        T Visit(ForNode node);

        // --- Expression Nodes ---

        /// <summary>Visits a binary operation such as arithmetic, comparison, or logical operations.</summary>
        T Visit(BinaryExpressionNode node);

        /// <summary>Visits a unary operation such as logical NOT, negation, or increments.</summary>
        T Visit(UnaryExpressionNode node);

        /// <summary>Visits an explicit type cast expression.</summary>
        T Visit(CastExpressionNode node);

        /// <summary>Visits a function call expression including arguments.</summary>
        T Visit(FunctionCallNode node);

        /// <summary>Visits a member access operation (e.g., object.property).</summary>
        T Visit(MemberAccessNode node);

        /// <summary>Visits an array indexing operation (e.g., array[index]).</summary>
        T Visit(ArrayAccessNode node);

        // --- Literal & Data Nodes ---

        /// <summary>Visits a literal value such as an integer, float, boolean, or string.</summary>
        T Visit(LiteralNode node);

        /// <summary>Visits an identifier representing a variable, function, or type name.</summary>
        T Visit(IdentifierNode node);

        /// <summary>Visits a string containing interpolation markers and expressions.</summary>
        T Visit(InterpolatedStringNode node);

        /// <summary>Visits a struct type definition.</summary>
        T Visit(StructDeclarationNode node);

        /// <summary>Visits a struct initialization expression.</summary>
        T Visit(StructInitializationNode node);

        /// <summary>Visits a <c>new</c> expression that allocates a safe heap reference.</summary>
        T Visit(NewExpressionNode node);

        /// <summary>Visits an array literal or initialization expression.</summary>
        T Visit(ArrayInitializationNode node);
    }
}
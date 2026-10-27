namespace Sage.Core;

/// <summary>A lexical borrow retains the binding it borrowed and its source location.</summary>
public sealed record BorrowLoan(SymbolMetadata Source, int Line, int Column);

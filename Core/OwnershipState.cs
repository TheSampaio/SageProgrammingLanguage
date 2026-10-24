namespace Sage.Core;

/// <summary>The availability of a legacy heap reference along a control-flow path.</summary>
public enum OwnershipKind { Available, Moved, Released, MaybeUnavailable }

/// <summary>Records the operation that invalidated an owning binding.</summary>
public sealed record OwnershipState(OwnershipKind Kind, int Line = 0, int Column = 0)
{
    public static readonly OwnershipState Available = new(OwnershipKind.Available);
}

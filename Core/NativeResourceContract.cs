namespace Sage.Core;

/// <summary>A validated declared ABI shape, not proof of a native implementation's ownership behavior.</summary>
public sealed record NativeResourceContract(
    string Name,
    string NativeType,
    string NativeAlias,
    string DestructorName,
    string DestructorReturnType,
    string NativeHeader,
    string? NativeLibrary,
    string? DestructorSymbol = null)
{
    public string DestructorBinding => $"{NativeAlias}::{DestructorName}";

    /// <summary>The C symbol cleanup calls: the declared name, or a generated C++ adapter.</summary>
    public string DestructorCSymbol => DestructorSymbol ?? DestructorName;
}

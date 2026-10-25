namespace Sage.Core;

/// <summary>A validated declared ABI shape, not proof of a native implementation's ownership behavior.</summary>
public sealed record NativeResourceContract(
    string Name,
    string NativeType,
    string NativeAlias,
    string DestructorName,
    string DestructorReturnType,
    string NativeHeader,
    string? NativeLibrary)
{
    public string DestructorBinding => $"{NativeAlias}::{DestructorName}";
}

namespace Sage.Core;

/// <summary>Immutable callable contract shared by registration and call analysis.</summary>
public sealed class CallableSignature(
    string returnType,
    IEnumerable<string> parameterTypes,
    bool isUnsafe = false,
    string? nativeHeader = null,
    string? nativeLibrary = null)
{
    public string ReturnType { get; } = returnType;
    public IReadOnlyList<string> ParameterTypes { get; } = Array.AsReadOnly(parameterTypes.ToArray());
    public bool IsUnsafe { get; } = isUnsafe;
    public string? NativeHeader { get; } = nativeHeader;
    public string? NativeLibrary { get; } = nativeLibrary;

    /// <summary>Parameter names do not affect a native ABI contract.</summary>
    public bool Matches(CallableSignature other) =>
        ReturnType == other.ReturnType && ParameterTypes.SequenceEqual(other.ParameterTypes) &&
        IsUnsafe == other.IsUnsafe && NativeHeader == other.NativeHeader && NativeLibrary == other.NativeLibrary;
}

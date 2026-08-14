using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Required for C# record types on .NET Framework.
    /// This type is natively included in .NET 5+ but must be
    /// manually defined when targeting .NET Framework.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

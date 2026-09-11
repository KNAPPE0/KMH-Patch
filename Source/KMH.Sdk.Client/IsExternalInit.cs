// Polyfill: net472 has no IsExternalInit, which the compiler requires for 'init' setters.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

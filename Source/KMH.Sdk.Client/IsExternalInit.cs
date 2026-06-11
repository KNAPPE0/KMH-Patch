// Polyfill so 'init' setters compile on net472. The C# compiler looks up
// System.Runtime.CompilerServices.IsExternalInit when it sees an 'init' setter; net6+ has it built in, net472
// doesn't, so we declare a stub. ComponentModel-style - both .NET and Roslyn accept it
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

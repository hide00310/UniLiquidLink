using System.Runtime.CompilerServices;

// Grant the test assembly access to internal members (e.g. JsonRpcProtocol, Server._registry)
// now that Tests/ has its own asmdef separate from the UniLiquidLink assembly.
[assembly: InternalsVisibleTo("UniLiquidLink.Tests")]

using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.DotPeekCommon.Product;

namespace DotPeekMcp.Plugin;

[ZoneMarker]
public sealed class ZoneMarker : IRequire<DotPeekProductZone>;

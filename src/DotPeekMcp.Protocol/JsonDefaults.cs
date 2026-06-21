using System.Text.Json;

namespace DotPeekMcp.Protocol;

public static class JsonDefaults {
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
    WriteIndented = false
  };
}

using System.Text.Json;

namespace DotPeekMcp.Protocol;

public sealed class BridgeToolCall {
  public BridgeToolCall(string name, JsonElement arguments) {
    Name = name;
    Arguments = arguments;
  }

  public string Name { get; }
  public JsonElement Arguments { get; }
}

public sealed class BridgeToolResult {
  public BridgeToolResult(bool success, JsonElement? data, BridgeToolError? error) {
    Success = success;
    Data = data;
    Error = error;
  }

  public bool Success { get; }
  public JsonElement? Data { get; }
  public BridgeToolError? Error { get; }

  public static BridgeToolResult FromData<T>(T data) {
    var element = JsonSerializer.SerializeToElement(data, JsonDefaults.Options);
    return new BridgeToolResult(true, element, null);
  }

  public static BridgeToolResult FromError(string code, string message) {
    return new BridgeToolResult(false, null, new BridgeToolError(code, message));
  }
}

public sealed class BridgeToolError {
  public BridgeToolError(string code, string message) {
    Code = code;
    Message = message;
  }

  public string Code { get; }
  public string Message { get; }
}

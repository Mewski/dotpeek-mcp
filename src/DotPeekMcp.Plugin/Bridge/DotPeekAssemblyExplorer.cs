using System.Reflection;
using DotPeekMcp.Plugin.Metadata;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class DotPeekAssemblyExplorer {
  private readonly object _lock = new();

  public object GetStatus() {
    var services = DotPeekMcpSolutionServices.Snapshot();
    return new {
      available = services.AssemblyExplorerManager is not null,
      mode = services.AssemblyExplorerManager is null ? "unavailable" : "dotpeek_assembly_explorer",
      registered_at = services.RegisteredAt,
      manager_type = services.AssemblyExplorerManager?.GetType().FullName ?? string.Empty
    };
  }

  public AssemblyExplorerOpenResult TryOpen(AssemblySession session) {
    lock (_lock) {
      var diagnostics = new List<string>();
      try {
        var manager = DotPeekMcpSolutionServices.Snapshot().AssemblyExplorerManager;
        if (manager is null) {
          return AssemblyExplorerOpenResult.Failed("dotPeek solution AssemblyExplorerManager is not available.", diagnostics);
        }

        return DotPeekMcpSolutionServices.InvokeOnPrimaryThread(() => {
          var fileSystemPath = JetBrainsReflection.ParseFileSystemPath(session.Path);
          var canAdd = InvokeOptionalBool(manager, "CanAddItemByPath", new[] { fileSystemPath });
          diagnostics.Add("can_add_item_by_path=" + (canAdd?.ToString().ToLowerInvariant() ?? "unknown"));
          if (canAdd == false) {
            return AssemblyExplorerOpenResult.Succeeded(false, diagnostics);
          }

          var array = Array.CreateInstance(fileSystemPath.GetType(), 1);
          array.SetValue(fileSystemPath, 0);
          InvokeRequired(manager, "AddItemsByPath", new[] { array });
          diagnostics.Add("add_items_by_path=called_on_primary_thread");
          return AssemblyExplorerOpenResult.Succeeded(true, diagnostics);
        });
      }
      catch (Exception exception) {
        return AssemblyExplorerOpenResult.Failed(JetBrainsReflection.FormatException(exception), diagnostics);
      }
    }
  }

  private static bool? InvokeOptionalBool(object target, string methodName, object[] arguments) {
    var method = FindMethod(target, methodName, arguments);
    if (method is null) {
      return null;
    }

    var value = method.Invoke(target, arguments);
    return value is bool result ? result : null;
  }

  private static void InvokeRequired(object target, string methodName, object[] arguments) {
    var method = FindMethod(target, methodName, arguments)
        ?? throw new InvalidOperationException(target.GetType().FullName + "." + methodName + " was not found.");
    method.Invoke(target, arguments);
  }

  private static MethodInfo? FindMethod(object target, string methodName, object[] arguments) {
    return target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(method => {
          if (method.Name != methodName) {
            return false;
          }

          var parameters = method.GetParameters();
          if (parameters.Length != arguments.Length) {
            return false;
          }

          for (var i = 0; i < parameters.Length; i++) {
            if (!parameters[i].ParameterType.IsInstanceOfType(arguments[i])) {
              return false;
            }
          }

          return true;
        });
  }
}

internal sealed class AssemblyExplorerOpenResult {
  public bool Success { get; set; }
  public bool GuiOpened { get; set; }
  public string Error { get; set; } = string.Empty;
  public string[] Diagnostics { get; set; } = Array.Empty<string>();

  public static AssemblyExplorerOpenResult Succeeded(bool guiOpened, IEnumerable<string> diagnostics) {
    return new AssemblyExplorerOpenResult {
      Success = true,
      GuiOpened = guiOpened,
      Diagnostics = diagnostics.ToArray()
    };
  }

  public static AssemblyExplorerOpenResult Failed(string error, IEnumerable<string> diagnostics) {
    return new AssemblyExplorerOpenResult {
      Error = error,
      Diagnostics = diagnostics.ToArray()
    };
  }
}

using System.Reflection;
using DotPeekMcp.Plugin.Bridge;

namespace DotPeekMcp.Plugin;

internal static class DotPeekMcpSolutionServices {
  private static readonly object Gate = new();
  private static readonly List<object> AssemblyCookies = new();
  private static object? _assemblyExplorerManager;
  private static object? _projectGenerationManager;
  private static object? _assemblyFactory;
  private static object? _assemblyCollection;
  private static object? _shellLocks;
  private static DateTimeOffset? _registeredAt;

  public static void Register(object assemblyExplorerManager, object projectGenerationManager, object assemblyFactory, object assemblyCollection, object shellLocks) {
    lock (Gate) {
      _assemblyExplorerManager = assemblyExplorerManager;
      _projectGenerationManager = projectGenerationManager;
      _assemblyFactory = assemblyFactory;
      _assemblyCollection = assemblyCollection;
      _shellLocks = shellLocks;
      _registeredAt = DateTimeOffset.UtcNow;
    }
  }

  public static SolutionServicesSnapshot Snapshot() {
    lock (Gate) {
      return new SolutionServicesSnapshot(
        _assemblyExplorerManager,
        _projectGenerationManager,
        _assemblyFactory,
        _assemblyCollection,
        _shellLocks,
        _registeredAt,
        AssemblyCookies.Count);
    }
  }

  public static T InvokeOnPrimaryThread<T>(Func<T> action) {
    var shellLocks = Snapshot().ShellLocks;
    if (shellLocks is null) {
      return action();
    }

    var dispatcher = shellLocks.GetType().GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance)?.GetValue(shellLocks)
        ?? throw new InvalidOperationException("IShellLocks.Dispatcher was not available.");
    var checkAccessValue = dispatcher.GetType().GetMethod("CheckAccess", BindingFlags.Public | BindingFlags.Instance)?.Invoke(dispatcher, Array.Empty<object>());
    var checkAccess = checkAccessValue is bool value && value;
    if (checkAccess) {
      return action();
    }

    var taskPriorityType = JetBrainsReflection.RequireType("JetBrains.Platform.Core", "JetBrains.Util.Threading.Tasks.TaskPriority");
    var invoke = dispatcher.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(Action), taskPriorityType }, null)
        ?? throw new InvalidOperationException("JetDispatcher.Invoke(string, Action, TaskPriority) was not found.");
    var priority = Enum.ToObject(taskPriorityType, 0);
    T result = default!;
    Exception? error = null;
    Action wrapper = () => {
      try {
        result = action();
      }
      catch (Exception exception) {
        error = exception;
      }
    };

    invoke.Invoke(dispatcher, new object[] { "dotpeek-mcp", wrapper, priority });
    if (error is not null) {
      throw error;
    }

    return result;
  }

  public static void InvokeOnPrimaryThread(Action action) {
    InvokeOnPrimaryThread(() => {
      action();
      return true;
    });
  }

  public static object? GetAssemblyFile(string assemblyPath, List<string> diagnostics) {
    var snapshot = Snapshot();
    if (snapshot.AssemblyCollection is null || snapshot.AssemblyFactory is null) {
      diagnostics.Add("solution_services=missing_assembly_factory");
      return null;
    }

    var assemblyLocation = JetBrainsReflection.CreateAssemblyLocation(assemblyPath);
    var existing = TryGetFileByLocation(snapshot.AssemblyCollection, assemblyLocation, diagnostics);
    if (existing is not null) {
      diagnostics.Add("assembly_file=existing_collection_entry");
      return existing;
    }

    var addRef = snapshot.AssemblyFactory.GetType()
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(method => {
          var parameters = method.GetParameters();
          return method.Name == "AddRef"
              && parameters.Length == 3
              && parameters[0].ParameterType.FullName == "JetBrains.Metadata.Reader.API.IAssemblyLocation";
        }) ?? throw new InvalidOperationException("AssemblyFactory.AddRef(IAssemblyLocation, string, IModuleReferenceResolveContext) was not found.");

    var cookie = addRef.Invoke(snapshot.AssemblyFactory, new[] { assemblyLocation, "dotpeek-mcp", null })
        ?? throw new InvalidOperationException("AssemblyFactory.AddRef returned null.");
    lock (Gate) {
      AssemblyCookies.Add(cookie);
    }

    diagnostics.Add("assembly_file=created_cookie");
    return cookie.GetType().GetProperty("AssemblyFile", BindingFlags.Public | BindingFlags.Instance)?.GetValue(cookie)
        ?? throw new InvalidOperationException("IAssemblyCookie.AssemblyFile returned null.");
  }

  private static object? TryGetFileByLocation(object assemblyCollection, object assemblyLocation, List<string> diagnostics) {
    var method = assemblyCollection.GetType()
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(candidate => {
          var parameters = candidate.GetParameters();
          return candidate.Name == "GetFileByLocation"
              && parameters.Length == 1
              && parameters[0].ParameterType.FullName == "JetBrains.Metadata.Reader.API.IAssemblyLocation";
        });
    if (method is null) {
      diagnostics.Add("assembly_collection_get_by_location=missing");
      return null;
    }

    return method.Invoke(assemblyCollection, new[] { assemblyLocation });
  }
}

internal sealed class SolutionServicesSnapshot {
  public SolutionServicesSnapshot(
      object? assemblyExplorerManager,
      object? projectGenerationManager,
      object? assemblyFactory,
      object? assemblyCollection,
      object? shellLocks,
      DateTimeOffset? registeredAt,
      int assemblyCookieCount) {
    AssemblyExplorerManager = assemblyExplorerManager;
    ProjectGenerationManager = projectGenerationManager;
    AssemblyFactory = assemblyFactory;
    AssemblyCollection = assemblyCollection;
    ShellLocks = shellLocks;
    RegisteredAt = registeredAt;
    AssemblyCookieCount = assemblyCookieCount;
  }

  public object? AssemblyExplorerManager { get; }
  public object? ProjectGenerationManager { get; }
  public object? AssemblyFactory { get; }
  public object? AssemblyCollection { get; }
  public object? ShellLocks { get; }
  public DateTimeOffset? RegisteredAt { get; }
  public int AssemblyCookieCount { get; }
}

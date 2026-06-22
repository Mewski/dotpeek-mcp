using System.Diagnostics;
using System.Reflection;

namespace DotPeekMcp.Plugin.Bridge;

internal static class JetBrainsReflection {
  public static object ParseFileSystemPath(string path) {
    var fileSystemPathType = RequireType("JetBrains.Platform.Core", "JetBrains.Util.FileSystemPath");
    var internStrategyType = RequireType("JetBrains.Platform.Core", "JetBrains.Util.FileSystemPathInternStrategy");
    var parseMethod = fileSystemPathType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), internStrategyType }, null)
        ?? throw new InvalidOperationException("FileSystemPath.Parse(string, FileSystemPathInternStrategy) was not found.");

    return parseMethod.Invoke(null, new[] { path, Enum.ToObject(internStrategyType, 0) })
        ?? throw new InvalidOperationException("FileSystemPath.Parse returned null for " + path + ".");
  }

  public static object ToVirtualFileSystemPath(string path) {
    var fileSystemPath = ParseFileSystemPath(path);
    var extensionsType = RequireType("JetBrains.Platform.Core", "JetBrains.Util.FileSystemPathExtensions");
    var method = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(candidate => {
          var parameters = candidate.GetParameters();
          return candidate.Name == "ToVirtualFileSystemPath"
              && parameters.Length == 1
              && parameters[0].ParameterType.FullName == "JetBrains.Util.FileSystemPath";
        }) ?? throw new InvalidOperationException("FileSystemPathExtensions.ToVirtualFileSystemPath(FileSystemPath) was not found.");

    return method.Invoke(null, new[] { fileSystemPath })
        ?? throw new InvalidOperationException("FileSystemPathExtensions.ToVirtualFileSystemPath returned null for " + path + ".");
  }

  public static object CreateAssemblyLocation(string path) {
    var fileSystemPath = ParseFileSystemPath(path);
    var assemblyLocationExType = RequireType("JetBrains.Platform.Metadata", "JetBrains.Metadata.Reader.API.AssemblyLocationEx");
    var toAssemblyLocation = assemblyLocationExType
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(method => {
          var parameters = method.GetParameters();
          return method.Name == "ToAssemblyLocation"
              && parameters.Length == 1
              && parameters[0].ParameterType.FullName == "JetBrains.Util.FileSystemPath";
        }) ?? throw new InvalidOperationException("AssemblyLocationEx.ToAssemblyLocation(FileSystemPath) was not found.");

    return toAssemblyLocation.Invoke(null, new[] { fileSystemPath })
        ?? throw new InvalidOperationException("AssemblyLocationEx.ToAssemblyLocation returned null for " + path + ".");
  }

  public static Type RequireType(string assemblyName, string typeName) {
    var assembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

    assembly ??= LoadJetBrainsAssembly(assemblyName);
    return assembly.GetType(typeName, true)
        ?? throw new InvalidOperationException("Type was not found: " + typeName);
  }

  public static object? WaitForTaskResult(object task) {
    if (task is not Task dotNetTask) {
      return task;
    }

    dotNetTask.GetAwaiter().GetResult();
    return task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(task);
  }

  public static string FormatException(Exception exception) {
    if (exception is TargetInvocationException { InnerException: { } inner }) {
      exception = inner;
    }

    return exception.GetType().Name + ": " + exception.Message;
  }

  private static Assembly LoadJetBrainsAssembly(string assemblyName) {
    try {
      return Assembly.Load(assemblyName);
    }
    catch {
      var dllName = assemblyName + ".dll";
      foreach (var directory in GetAssemblyProbeDirectories()) {
        var path = Path.Combine(directory, dllName);
        if (File.Exists(path)) {
          return Assembly.LoadFrom(path);
        }
      }

      throw;
    }
  }

  private static IEnumerable<string> GetAssemblyProbeDirectories() {
    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddDirectory(directories, AppDomain.CurrentDomain.BaseDirectory);
    AddDirectory(directories, Path.GetDirectoryName(typeof(JetBrainsReflection).Assembly.Location));
    AddDirectory(directories, Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty));
    return directories;
  }

  private static void AddDirectory(HashSet<string> directories, string? directory) {
    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) {
      directories.Add(directory!);
    }
  }
}

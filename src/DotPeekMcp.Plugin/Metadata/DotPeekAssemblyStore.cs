namespace DotPeekMcp.Plugin.Metadata;

internal sealed class DotPeekAssemblyStore {
  private readonly object _lock = new();
  private readonly AssemblyMetadataLoader _loader = new();
  private readonly List<AssemblySession> _sessions = new();
  private int _nextId = 1;

  public AssemblySession Open(string path) {
    if (string.IsNullOrWhiteSpace(path)) {
      throw new ArgumentException("Assembly path is required.", nameof(path));
    }

    var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    if (!File.Exists(fullPath)) {
      throw new FileNotFoundException("Assembly file was not found.", fullPath);
    }

    lock (_lock) {
      var existing = _sessions.FirstOrDefault(session => string.Equals(session.Path, fullPath, StringComparison.OrdinalIgnoreCase));
      if (existing is not null) {
        return existing;
      }

      var session = new AssemblySession {
        Id = "asm_" + _nextId++,
        Path = fullPath,
        OpenedAt = DateTimeOffset.UtcNow,
        Metadata = _loader.Load(fullPath)
      };
      _sessions.Add(session);
      return session;
    }
  }

  public AssemblySession[] List() {
    lock (_lock) {
      return _sessions.ToArray();
    }
  }

  public AssemblySession Resolve(string assembly) {
    if (string.IsNullOrWhiteSpace(assembly)) {
      throw new ArgumentException("Assembly session ID or path is required.", nameof(assembly));
    }

    lock (_lock) {
      var session = _sessions.FirstOrDefault(item =>
          string.Equals(item.Id, assembly, StringComparison.OrdinalIgnoreCase)
          || string.Equals(item.Path, assembly, StringComparison.OrdinalIgnoreCase));
      if (session is not null) {
        return session;
      }
    }

    if (File.Exists(assembly)) {
      return Open(assembly);
    }

    throw new KeyNotFoundException("Assembly is not open: " + assembly);
  }
}

namespace DotPeekMcp.Proxy.Cli;

internal sealed class CommandLine {
  private readonly string[] _arguments;

  public CommandLine(IEnumerable<string> arguments) {
    _arguments = arguments.ToArray();
  }

  public IReadOnlyList<string> Arguments => _arguments;

  public bool HasFlag(params string[] names) {
    return _arguments.Any(argument => names.Any(name => string.Equals(argument, name, StringComparison.Ordinal)));
  }

  public string GetOption(string defaultValue, params string[] names) {
    return TryGetOption(out var value, names) ? value : defaultValue;
  }

  public int GetIntOption(int defaultValue, params string[] names) {
    if (!TryGetOption(out var value, names)) {
      return defaultValue;
    }

    if (int.TryParse(value, out var parsed)) {
      return parsed;
    }

    throw new ArgumentException($"Expected an integer value for {names[0]}.");
  }

  public bool TryGetOption(out string value, params string[] names) {
    for (var index = 0; index < _arguments.Length; index++) {
      var argument = _arguments[index];
      foreach (var name in names) {
        if (string.Equals(argument, name, StringComparison.Ordinal)) {
          if (index + 1 >= _arguments.Length || _arguments[index + 1].StartsWith("-", StringComparison.Ordinal)) {
            throw new ArgumentException($"Missing value for {name}.");
          }

          value = _arguments[index + 1];
          return true;
        }

        var prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal)) {
          value = argument[prefix.Length..];
          return true;
        }
      }
    }

    value = string.Empty;
    return false;
  }
}

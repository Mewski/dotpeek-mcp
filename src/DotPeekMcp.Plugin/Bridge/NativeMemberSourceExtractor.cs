using DotPeekMcp.Plugin.Metadata;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class NativeMemberSourceExtractor {
  public MemberSourceExtractResult TryExtract(string source, TypeMetadata type, MemberMetadata member) {
    var diagnostics = new List<string>();
    var lines = SplitLines(source);
    foreach (var candidate in BuildCandidateNames(type, member)) {
      var index = FindCandidateLine(lines, candidate, member);
      if (index < 0) {
        diagnostics.Add("candidate_not_found=" + candidate);
        continue;
      }

      var extracted = TryExtractAt(lines, index, diagnostics);
      if (!string.IsNullOrWhiteSpace(extracted)) {
        diagnostics.Add("candidate=" + candidate);
        return MemberSourceExtractResult.Succeeded(extracted, diagnostics);
      }
    }

    return MemberSourceExtractResult.Failed(diagnostics);
  }

  private static string[] SplitLines(string source) {
    return source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
  }

  private static IEnumerable<string> BuildCandidateNames(TypeMetadata type, MemberMetadata member) {
    var names = new List<string>();
    var typeName = StripGenericArity(type.Name);
    if (member.Name == ".ctor") {
      names.Add(typeName);
    }
    else if (member.Name == ".cctor") {
      names.Add("static " + typeName);
    }
    else {
      names.Add(member.Name);
      foreach (var prefix in new[] { "get_", "set_", "add_", "remove_" }) {
        if (member.Name.StartsWith(prefix, StringComparison.Ordinal)) {
          names.Add(member.Name.Substring(prefix.Length));
        }
      }
    }

    return names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal);
  }

  private static int FindCandidateLine(string[] lines, string candidate, MemberMetadata member) {
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].Trim();
      if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) {
        continue;
      }

      if (!line.Contains(candidate)) {
        continue;
      }

      if (member.Kind == "method") {
        if (line.Contains(candidate + "(") || candidate.StartsWith("static ", StringComparison.Ordinal)) {
          return IncludeAttributes(lines, i);
        }
      }
      else if (member.Kind == "property" || member.Kind == "event") {
        if (ContainsWord(line, candidate)) {
          return IncludeAttributes(lines, i);
        }
      }
      else if (member.Kind == "field") {
        if (ContainsWord(line, candidate)) {
          return IncludeAttributes(lines, i);
        }
      }
    }

    return -1;
  }

  private static int IncludeAttributes(string[] lines, int index) {
    var start = index;
    while (start > 0) {
      var previous = lines[start - 1].Trim();
      if (previous.StartsWith("[", StringComparison.Ordinal)) {
        start--;
        continue;
      }

      break;
    }

    return start;
  }

  private static string TryExtractAt(string[] lines, int index, List<string> diagnostics) {
    var firstBodyLine = -1;
    for (var i = index; i < Math.Min(lines.Length, index + 16); i++) {
      if (lines[i].Contains("=>") || lines[i].Contains(";")) {
        var singleLine = ExtractUntilSemicolon(lines, index);
        if (!string.IsNullOrWhiteSpace(singleLine)) {
          diagnostics.Add("extract=semicolon");
          return singleLine;
        }
      }

      if (lines[i].Contains("{")) {
        firstBodyLine = i;
        break;
      }
    }

    if (firstBodyLine < 0) {
      diagnostics.Add("extract=no_body_marker");
      return string.Empty;
    }

    var balance = 0;
    var sawOpen = false;
    for (var i = index; i < lines.Length; i++) {
      foreach (var character in StripLineComment(lines[i])) {
        if (character == '{') {
          balance++;
          sawOpen = true;
        }
        else if (character == '}') {
          balance--;
        }
      }

      if (sawOpen && balance == 0) {
        diagnostics.Add("extract=balanced_braces");
        return string.Join(Environment.NewLine, lines.Skip(index).Take(i - index + 1)).Trim();
      }
    }

    diagnostics.Add("extract=unbalanced_braces");
    return string.Empty;
  }

  private static string ExtractUntilSemicolon(string[] lines, int index) {
    for (var i = index; i < Math.Min(lines.Length, index + 16); i++) {
      if (lines[i].Contains(";")) {
        return string.Join(Environment.NewLine, lines.Skip(index).Take(i - index + 1)).Trim();
      }
    }

    return string.Empty;
  }

  private static string StripLineComment(string line) {
    var index = line.IndexOf("//", StringComparison.Ordinal);
    return index < 0 ? line : line.Substring(0, index);
  }

  private static bool ContainsWord(string line, string word) {
    var index = line.IndexOf(word, StringComparison.Ordinal);
    while (index >= 0) {
      var before = index == 0 || !IsIdentifier(line[index - 1]);
      var afterIndex = index + word.Length;
      var after = afterIndex >= line.Length || !IsIdentifier(line[afterIndex]);
      if (before && after) {
        return true;
      }

      index = line.IndexOf(word, index + 1, StringComparison.Ordinal);
    }

    return false;
  }

  private static bool IsIdentifier(char character) {
    return char.IsLetterOrDigit(character) || character == '_';
  }

  private static string StripGenericArity(string name) {
    var index = name.IndexOf('`');
    return index < 0 ? name : name.Substring(0, index);
  }
}

internal sealed class MemberSourceExtractResult {
  public bool Success { get; set; }
  public string Source { get; set; } = string.Empty;
  public string[] Diagnostics { get; set; } = Array.Empty<string>();

  public static MemberSourceExtractResult Succeeded(string source, IEnumerable<string> diagnostics) {
    return new MemberSourceExtractResult {
      Success = true,
      Source = source,
      Diagnostics = diagnostics.ToArray()
    };
  }

  public static MemberSourceExtractResult Failed(IEnumerable<string> diagnostics) {
    return new MemberSourceExtractResult {
      Diagnostics = diagnostics.ToArray()
    };
  }
}

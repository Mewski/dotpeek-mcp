using System.Text;

namespace DotPeekMcp.Plugin.Metadata;

internal sealed class MetadataSourceWriter {
  public string WriteType(AssemblySession session, TypeMetadata type) {
    var builder = new StringBuilder();
    builder.AppendLine("// Metadata-only declaration generated inside the dotPeek MCP plugin.");
    builder.AppendLine("// Method bodies are stubs because native dotPeek decompilation was unavailable for this request.");
    builder.AppendLine("// Assembly: " + session.Metadata.Name + " (" + session.Path + ")");
    builder.AppendLine();

    if (!string.IsNullOrEmpty(type.Namespace)) {
      builder.AppendLine("namespace " + type.Namespace + " {");
      WriteTypeBody(builder, type, "  ");
      builder.AppendLine("}");
    }
    else {
      WriteTypeBody(builder, type, string.Empty);
    }

    return builder.ToString();
  }

  public string WriteMember(AssemblySession session, TypeMetadata type, MemberMetadata member) {
    var builder = new StringBuilder();
    builder.AppendLine("// Metadata-only member declaration generated inside the dotPeek MCP plugin.");
    builder.AppendLine("// Assembly: " + session.Metadata.Name + " (" + session.Path + ")");
    builder.AppendLine("// Type: " + type.FullName);
    builder.AppendLine("// Token: " + member.Token);
    builder.AppendLine();
    WriteMember(builder, type, member, string.Empty);
    return builder.ToString();
  }

  private static void WriteTypeBody(StringBuilder builder, TypeMetadata type, string indent) {
    if (type.Kind == "delegate") {
      builder.AppendLine(indent + BuildDelegateDeclaration(type));
      return;
    }

    builder.AppendLine(indent + BuildTypeDeclaration(type) + " {");

    foreach (var member in type.Fields.Where(ShouldWriteField)) {
      builder.AppendLine(indent + "  " + member.Signature);
    }

    WriteBlankLineIfNeeded(builder, type.Fields.Any(ShouldWriteField), type.Properties.Length + type.Events.Length + type.Methods.Length > 0);

    foreach (var member in type.Properties) {
      builder.AppendLine(indent + "  " + member.Signature);
    }

    foreach (var member in type.Events) {
      builder.AppendLine(indent + "  " + member.Signature);
    }

    WriteBlankLineIfNeeded(builder, type.Properties.Length + type.Events.Length > 0, type.Methods.Any(ShouldWriteMethod));

    foreach (var member in type.Methods.Where(ShouldWriteMethod)) {
      WriteMember(builder, type, member, indent + "  ");
    }

    builder.AppendLine(indent + "}");
  }

  private static void WriteMember(StringBuilder builder, TypeMetadata type, MemberMetadata member, string indent) {
    if (member.Kind != "method") {
      builder.AppendLine(indent + member.Signature);
      return;
    }

    if (type.Kind == "interface" || member.IsAbstract) {
      builder.AppendLine(indent + member.Signature + ";");
      return;
    }

    builder.AppendLine(indent + member.Signature + " { throw new System.NotImplementedException(); }");
  }

  private static string BuildTypeDeclaration(TypeMetadata type) {
    var modifiers = new List<string> { type.Accessibility };
    if (type.Kind == "class") {
      if (type.IsAbstract && type.IsSealed) {
        modifiers.Add("static");
      }
      else {
        if (type.IsAbstract) {
          modifiers.Add("abstract");
        }

        if (type.IsSealed) {
          modifiers.Add("sealed");
        }
      }
    }

    modifiers.Add(type.Kind);
    modifiers.Add(type.Name);

    var declaration = string.Join(" ", modifiers.Where(part => !string.IsNullOrWhiteSpace(part)));
    if (ShouldWriteBaseType(type)) {
      declaration += " : " + type.BaseType;
    }

    return declaration;
  }

  private static string BuildDelegateDeclaration(TypeMetadata type) {
    var invoke = type.Methods.FirstOrDefault(method => string.Equals(method.Name, "Invoke", StringComparison.Ordinal));
    if (invoke is null) {
      return type.Accessibility + " delegate void " + type.Name + "();";
    }

    var signature = invoke.Signature;
    signature = signature.Replace("public virtual ", string.Empty).Replace("public ", string.Empty);
    signature = signature.Replace("Invoke(", type.Name + "(");
    return type.Accessibility + " delegate " + signature + ";";
  }

  private static bool ShouldWriteBaseType(TypeMetadata type) {
    if (string.IsNullOrEmpty(type.BaseType)) {
      return false;
    }

    if (type.Kind != "class") {
      return false;
    }

    return !string.Equals(type.BaseType, "System.Object", StringComparison.Ordinal)
        && !string.Equals(type.BaseType, "object", StringComparison.Ordinal);
  }

  private static bool ShouldWriteField(MemberMetadata member) {
    return !member.Name.StartsWith("<", StringComparison.Ordinal);
  }

  private static bool ShouldWriteMethod(MemberMetadata member) {
    if (member.Name == ".ctor" || member.Name == ".cctor") {
      return true;
    }

    return !member.Name.StartsWith("get_", StringComparison.Ordinal)
        && !member.Name.StartsWith("set_", StringComparison.Ordinal)
        && !member.Name.StartsWith("add_", StringComparison.Ordinal)
        && !member.Name.StartsWith("remove_", StringComparison.Ordinal)
        && !member.Name.StartsWith("raise_", StringComparison.Ordinal);
  }

  private static void WriteBlankLineIfNeeded(StringBuilder builder, bool previousGroupWritten, bool nextGroupHasItems) {
    if (previousGroupWritten && nextGroupHasItems) {
      builder.AppendLine();
    }
  }
}

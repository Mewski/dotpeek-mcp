namespace DotPeekMcp.Plugin.Metadata;

internal sealed class MetadataSignatureContext {
  public MetadataSignatureContext(string[] typeGenericParameters, string[] methodGenericParameters) {
    TypeGenericParameters = typeGenericParameters;
    MethodGenericParameters = methodGenericParameters;
  }

  public string[] TypeGenericParameters { get; }
  public string[] MethodGenericParameters { get; }

  public string GetTypeGenericParameter(int index) {
    return index >= 0 && index < TypeGenericParameters.Length
        ? TypeGenericParameters[index]
        : "T" + index;
  }

  public string GetMethodGenericParameter(int index) {
    return index >= 0 && index < MethodGenericParameters.Length
        ? MethodGenericParameters[index]
        : "TMethod" + index;
  }
}

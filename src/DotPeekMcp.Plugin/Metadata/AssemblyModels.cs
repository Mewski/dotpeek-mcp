namespace DotPeekMcp.Plugin.Metadata;

internal sealed class AssemblySession {
  public string Id { get; set; } = string.Empty;
  public string Path { get; set; } = string.Empty;
  public DateTimeOffset OpenedAt { get; set; }
  public AssemblyMetadata Metadata { get; set; } = new();
}

internal sealed class AssemblyMetadata {
  public string Name { get; set; } = string.Empty;
  public string Version { get; set; } = string.Empty;
  public string Culture { get; set; } = string.Empty;
  public string PublicKeyToken { get; set; } = string.Empty;
  public string ModuleName { get; set; } = string.Empty;
  public string Mvid { get; set; } = string.Empty;
  public string MetadataVersion { get; set; } = string.Empty;
  public string TargetFramework { get; set; } = string.Empty;
  public string Machine { get; set; } = string.Empty;
  public bool IsExecutable { get; set; }
  public TypeMetadata[] Types { get; set; } = Array.Empty<TypeMetadata>();
  public AssemblyReferenceMetadata[] References { get; set; } = Array.Empty<AssemblyReferenceMetadata>();
  public ResourceMetadata[] Resources { get; set; } = Array.Empty<ResourceMetadata>();
}

internal sealed class TypeMetadata {
  public string Token { get; set; } = string.Empty;
  public string Namespace { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string MetadataName { get; set; } = string.Empty;
  public string FullName { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public string Accessibility { get; set; } = string.Empty;
  public string BaseType { get; set; } = string.Empty;
  public string[] GenericParameters { get; set; } = Array.Empty<string>();
  public bool IsAbstract { get; set; }
  public bool IsSealed { get; set; }
  public MemberMetadata[] Fields { get; set; } = Array.Empty<MemberMetadata>();
  public MemberMetadata[] Properties { get; set; } = Array.Empty<MemberMetadata>();
  public MemberMetadata[] Events { get; set; } = Array.Empty<MemberMetadata>();
  public MemberMetadata[] Methods { get; set; } = Array.Empty<MemberMetadata>();
}

internal sealed class MemberMetadata {
  public string Token { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string FullName { get; set; } = string.Empty;
  public string Signature { get; set; } = string.Empty;
  public string Type { get; set; } = string.Empty;
  public string Accessibility { get; set; } = string.Empty;
  public bool IsStatic { get; set; }
  public bool IsAbstract { get; set; }
  public bool IsVirtual { get; set; }
  public bool IsSpecialName { get; set; }
}

internal sealed class AssemblyReferenceMetadata {
  public string Token { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Version { get; set; } = string.Empty;
  public string Culture { get; set; } = string.Empty;
  public string PublicKeyToken { get; set; } = string.Empty;
}

internal sealed class ResourceMetadata {
  public string Token { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Visibility { get; set; } = string.Empty;
  public long Offset { get; set; }
  public string Implementation { get; set; } = string.Empty;
}

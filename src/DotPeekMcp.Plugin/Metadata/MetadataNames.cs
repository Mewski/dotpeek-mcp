using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotPeekMcp.Plugin.Metadata;

internal static class MetadataNames {
  public static string FormatToken(EntityHandle handle) {
    return "0x" + MetadataTokens.GetToken(handle).ToString("X8");
  }

  public static string TrimGenericArity(string name) {
    var tick = name.IndexOf('`');
    return tick >= 0 ? name.Substring(0, tick) : name;
  }

  public static string CleanTypeName(string name, IReadOnlyList<string> genericParameters) {
    var clean = TrimGenericArity(name);
    return genericParameters.Count == 0
        ? clean
        : clean + "<" + string.Join(", ", genericParameters) + ">";
  }

  public static string GetTypeFullName(MetadataReader reader, TypeDefinitionHandle handle, bool includeGenericParameters) {
    var type = reader.GetTypeDefinition(handle);
    var name = reader.GetString(type.Name);
    var declaringType = type.GetDeclaringType();
    var genericParameters = includeGenericParameters ? GetGenericParameterNames(reader, type.GetGenericParameters()) : Array.Empty<string>();
    var cleanName = CleanTypeName(name, genericParameters);

    if (!declaringType.IsNil) {
      return GetTypeFullName(reader, declaringType, includeGenericParameters) + "." + cleanName;
    }

    var @namespace = reader.GetString(type.Namespace);
    return string.IsNullOrEmpty(@namespace) ? cleanName : @namespace + "." + cleanName;
  }

  public static string GetTypeMetadataName(MetadataReader reader, TypeDefinitionHandle handle) {
    var type = reader.GetTypeDefinition(handle);
    var name = reader.GetString(type.Name);
    var declaringType = type.GetDeclaringType();
    if (!declaringType.IsNil) {
      return GetTypeMetadataName(reader, declaringType) + "+" + name;
    }

    var @namespace = reader.GetString(type.Namespace);
    return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
  }

  public static string GetTypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle, bool includeGenericParameters) {
    var reference = reader.GetTypeReference(handle);
    var name = reader.GetString(reference.Name);
    var cleanName = includeGenericParameters ? TrimGenericArity(name) : name;

    if (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
      return GetTypeReferenceFullName(reader, (TypeReferenceHandle)reference.ResolutionScope, includeGenericParameters) + "." + cleanName;
    }

    var @namespace = reader.GetString(reference.Namespace);
    return string.IsNullOrEmpty(@namespace) ? cleanName : @namespace + "." + cleanName;
  }

  public static string ResolveTypeName(MetadataReader reader, EntityHandle handle, MetadataSignatureContext context, MetadataSignatureTypeProvider provider) {
    return handle.Kind switch {
      HandleKind.TypeDefinition => GetTypeFullName(reader, (TypeDefinitionHandle)handle, false),
      HandleKind.TypeReference => GetTypeReferenceFullName(reader, (TypeReferenceHandle)handle, false),
      HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(provider, context),
      _ => string.Empty
    };
  }

  public static string[] GetGenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles) {
    var names = new List<string>();
    foreach (var handle in handles) {
      var parameter = reader.GetGenericParameter(handle);
      var name = reader.GetString(parameter.Name);
      names.Add(string.IsNullOrEmpty(name) ? "T" + names.Count : name);
    }

    return names.ToArray();
  }
}

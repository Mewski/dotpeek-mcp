using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotPeekMcp.Plugin.Metadata;

internal sealed class AssemblyMetadataLoader {
  private readonly MetadataSignatureTypeProvider _signatureProvider = new();

  public AssemblyMetadata Load(string path) {
    using var stream = File.OpenRead(path);
    using var peReader = new PEReader(stream);
    if (!peReader.HasMetadata) {
      throw new BadImageFormatException("The file is a PE image but does not contain CLR metadata.");
    }

    var reader = peReader.GetMetadataReader();
    var module = reader.GetModuleDefinition();
    var metadata = new AssemblyMetadata {
      ModuleName = reader.GetString(module.Name),
      Mvid = reader.GetGuid(module.Mvid).ToString("D"),
      MetadataVersion = reader.MetadataVersion,
      Machine = peReader.PEHeaders.CoffHeader.Machine.ToString(),
      IsExecutable = peReader.PEHeaders.IsExe,
      Types = LoadTypes(reader),
      References = LoadReferences(reader),
      Resources = LoadResources(reader)
    };

    if (reader.IsAssembly) {
      var assembly = reader.GetAssemblyDefinition();
      metadata.Name = reader.GetString(assembly.Name);
      metadata.Version = assembly.Version?.ToString() ?? string.Empty;
      metadata.Culture = reader.GetString(assembly.Culture);
      metadata.PublicKeyToken = FormatPublicKeyToken(reader.GetBlobBytes(assembly.PublicKey));
      metadata.TargetFramework = TryReadTargetFramework(reader, assembly);
    }
    else {
      metadata.Name = Path.GetFileNameWithoutExtension(path);
    }

    return metadata;
  }

  private TypeMetadata[] LoadTypes(MetadataReader reader) {
    var types = new List<TypeMetadata>();
    foreach (var handle in reader.TypeDefinitions) {
      var definition = reader.GetTypeDefinition(handle);
      var name = reader.GetString(definition.Name);
      if (name == "<Module>") {
        continue;
      }

      var genericParameters = MetadataNames.GetGenericParameterNames(reader, definition.GetGenericParameters());
      var context = new MetadataSignatureContext(genericParameters, Array.Empty<string>());
      var baseType = MetadataNames.ResolveTypeName(reader, definition.BaseType, context, _signatureProvider);
      var type = new TypeMetadata {
        Token = MetadataNames.FormatToken(handle),
        Namespace = reader.GetString(definition.Namespace),
        Name = MetadataNames.CleanTypeName(name, genericParameters),
        MetadataName = MetadataNames.GetTypeMetadataName(reader, handle),
        FullName = MetadataNames.GetTypeFullName(reader, handle, true),
        Kind = GetTypeKind(definition, baseType),
        Accessibility = GetTypeAccessibility(definition.Attributes),
        BaseType = baseType,
        GenericParameters = genericParameters,
        IsAbstract = (definition.Attributes & TypeAttributes.Abstract) != 0,
        IsSealed = (definition.Attributes & TypeAttributes.Sealed) != 0
      };

      type.Fields = LoadFields(reader, definition, type, context);
      type.Properties = LoadProperties(reader, definition, type, context);
      type.Events = LoadEvents(reader, definition, type, context);
      type.Methods = LoadMethods(reader, definition, type, genericParameters);
      types.Add(type);
    }

    return types
        .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
  }

  private MemberMetadata[] LoadFields(MetadataReader reader, TypeDefinition definition, TypeMetadata declaringType, MetadataSignatureContext context) {
    var fields = new List<MemberMetadata>();
    foreach (var handle in definition.GetFields()) {
      var field = reader.GetFieldDefinition(handle);
      var name = reader.GetString(field.Name);
      var fieldType = TryDecode(() => field.DecodeSignature(_signatureProvider, context), "unknown");
      var metadata = new MemberMetadata {
        Token = MetadataNames.FormatToken(handle),
        Kind = "field",
        Name = name,
        FullName = declaringType.FullName + "." + name,
        Type = fieldType,
        Accessibility = GetFieldAccessibility(field.Attributes),
        IsStatic = (field.Attributes & FieldAttributes.Static) != 0,
        IsSpecialName = (field.Attributes & FieldAttributes.SpecialName) != 0
      };
      metadata.Signature = BuildFieldSignature(metadata);
      fields.Add(metadata);
    }

    return fields.ToArray();
  }

  private MemberMetadata[] LoadProperties(MetadataReader reader, TypeDefinition definition, TypeMetadata declaringType, MetadataSignatureContext context) {
    var properties = new List<MemberMetadata>();
    foreach (var handle in definition.GetProperties()) {
      var property = reader.GetPropertyDefinition(handle);
      var name = reader.GetString(property.Name);
      var signature = TryDecode(() => property.DecodeSignature(_signatureProvider, context), default(MethodSignature<string>));
      var accessors = property.GetAccessors();
      var accessorMethods = GetAccessorMethods(reader, accessors.Getter, accessors.Setter);
      var propertyType = signature.ReturnType ?? "unknown";
      var parameters = signature.ParameterTypes.IsDefaultOrEmpty
          ? string.Empty
          : "[" + string.Join(", ", signature.ParameterTypes) + "]";
      var metadata = new MemberMetadata {
        Token = MetadataNames.FormatToken(handle),
        Kind = "property",
        Name = name,
        FullName = declaringType.FullName + "." + name,
        Type = propertyType,
        Accessibility = GetMostVisibleMethodAccessibility(accessorMethods),
        IsStatic = accessorMethods.Any(method => (method.Attributes & MethodAttributes.Static) != 0),
        IsSpecialName = (property.Attributes & PropertyAttributes.SpecialName) != 0
      };
      metadata.Signature = BuildPropertySignature(metadata, parameters, !accessors.Getter.IsNil, !accessors.Setter.IsNil);
      properties.Add(metadata);
    }

    return properties.ToArray();
  }

  private MemberMetadata[] LoadEvents(MetadataReader reader, TypeDefinition definition, TypeMetadata declaringType, MetadataSignatureContext context) {
    var events = new List<MemberMetadata>();
    foreach (var handle in definition.GetEvents()) {
      var eventDefinition = reader.GetEventDefinition(handle);
      var name = reader.GetString(eventDefinition.Name);
      var accessors = eventDefinition.GetAccessors();
      var accessorMethods = GetAccessorMethods(reader, accessors.Adder, accessors.Remover, accessors.Raiser);
      var eventType = MetadataNames.ResolveTypeName(reader, eventDefinition.Type, context, _signatureProvider);
      var metadata = new MemberMetadata {
        Token = MetadataNames.FormatToken(handle),
        Kind = "event",
        Name = name,
        FullName = declaringType.FullName + "." + name,
        Type = eventType,
        Accessibility = GetMostVisibleMethodAccessibility(accessorMethods),
        IsStatic = accessorMethods.Any(method => (method.Attributes & MethodAttributes.Static) != 0),
        IsSpecialName = (eventDefinition.Attributes & EventAttributes.SpecialName) != 0
      };
      metadata.Signature = BuildEventSignature(metadata);
      events.Add(metadata);
    }

    return events.ToArray();
  }

  private MemberMetadata[] LoadMethods(MetadataReader reader, TypeDefinition definition, TypeMetadata declaringType, string[] typeGenericParameters) {
    var methods = new List<MemberMetadata>();
    foreach (var handle in definition.GetMethods()) {
      var method = reader.GetMethodDefinition(handle);
      var methodGenericParameters = MetadataNames.GetGenericParameterNames(reader, method.GetGenericParameters());
      var context = new MetadataSignatureContext(typeGenericParameters, methodGenericParameters);
      var name = reader.GetString(method.Name);
      var decoded = TryDecode(() => method.DecodeSignature(_signatureProvider, context), default(MethodSignature<string>));
      var parameterNames = GetParameterNames(reader, method);
      var metadata = new MemberMetadata {
        Token = MetadataNames.FormatToken(handle),
        Kind = "method",
        Name = GetFriendlyMethodName(name, declaringType),
        FullName = declaringType.FullName + "." + name,
        Type = decoded.ReturnType ?? "void",
        Accessibility = GetMethodAccessibility(method.Attributes),
        IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
        IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
        IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
        IsSpecialName = (method.Attributes & MethodAttributes.SpecialName) != 0
      };
      metadata.Signature = BuildMethodSignature(metadata, name, declaringType, methodGenericParameters, decoded, parameterNames);
      methods.Add(metadata);
    }

    return methods.ToArray();
  }

  private AssemblyReferenceMetadata[] LoadReferences(MetadataReader reader) {
    var references = new List<AssemblyReferenceMetadata>();
    foreach (var handle in reader.AssemblyReferences) {
      var reference = reader.GetAssemblyReference(handle);
      references.Add(new AssemblyReferenceMetadata {
        Token = MetadataNames.FormatToken(handle),
        Name = reader.GetString(reference.Name),
        Version = reference.Version?.ToString() ?? string.Empty,
        Culture = reader.GetString(reference.Culture),
        PublicKeyToken = FormatPublicKeyToken(reader.GetBlobBytes(reference.PublicKeyOrToken))
      });
    }

    return references
        .OrderBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
  }

  private ResourceMetadata[] LoadResources(MetadataReader reader) {
    var resources = new List<ResourceMetadata>();
    foreach (var handle in reader.ManifestResources) {
      var resource = reader.GetManifestResource(handle);
      resources.Add(new ResourceMetadata {
        Token = MetadataNames.FormatToken(handle),
        Name = reader.GetString(resource.Name),
        Visibility = (resource.Attributes & ManifestResourceAttributes.Public) != 0 ? "public" : "private",
        Offset = resource.Offset,
        Implementation = resource.Implementation.IsNil ? "embedded" : resource.Implementation.Kind.ToString()
      });
    }

    return resources
        .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
  }

  private static MethodDefinition[] GetAccessorMethods(MetadataReader reader, params MethodDefinitionHandle[] handles) {
    return handles
        .Where(handle => !handle.IsNil)
        .Select(reader.GetMethodDefinition)
        .ToArray();
  }

  private static Dictionary<int, string> GetParameterNames(MetadataReader reader, MethodDefinition method) {
    var parameters = new Dictionary<int, string>();
    foreach (var handle in method.GetParameters()) {
      var parameter = reader.GetParameter(handle);
      if (parameter.SequenceNumber <= 0) {
        continue;
      }

      var name = reader.GetString(parameter.Name);
      parameters[parameter.SequenceNumber] = string.IsNullOrEmpty(name) ? "arg" + (parameter.SequenceNumber - 1) : name;
    }

    return parameters;
  }

  private static string BuildFieldSignature(MemberMetadata field) {
    return JoinParts(field.Accessibility, field.IsStatic ? "static" : string.Empty, field.Type, field.Name) + ";";
  }

  private static string BuildPropertySignature(MemberMetadata property, string parameters, bool hasGetter, bool hasSetter) {
    var accessors = string.Join(" ", new[] {
      hasGetter ? "get;" : string.Empty,
      hasSetter ? "set;" : string.Empty
    }.Where(part => !string.IsNullOrEmpty(part)));
    return JoinParts(property.Accessibility, property.IsStatic ? "static" : string.Empty, property.Type, property.Name + parameters) + " { " + accessors + " }";
  }

  private static string BuildEventSignature(MemberMetadata eventMetadata) {
    return JoinParts(eventMetadata.Accessibility, eventMetadata.IsStatic ? "static" : string.Empty, "event", eventMetadata.Type, eventMetadata.Name) + ";";
  }

  private static string BuildMethodSignature(
      MemberMetadata method,
      string rawName,
      TypeMetadata declaringType,
      string[] methodGenericParameters,
      MethodSignature<string> signature,
      IReadOnlyDictionary<int, string> parameterNames) {
    var friendlyName = GetFriendlyMethodName(rawName, declaringType);
    if (methodGenericParameters.Length > 0 && rawName != ".ctor" && rawName != ".cctor") {
      friendlyName += "<" + string.Join(", ", methodGenericParameters) + ">";
    }

    var parameters = new List<string>();
    for (var i = 0; i < signature.ParameterTypes.Length; i++) {
      var type = signature.ParameterTypes[i] ?? "unknown";
      var name = parameterNames.TryGetValue(i + 1, out var parameterName) ? parameterName : "arg" + i;
      parameters.Add(type + " " + name);
    }

    if (rawName == ".ctor") {
      return JoinParts(method.Accessibility, friendlyName + "(" + string.Join(", ", parameters) + ")");
    }

    if (rawName == ".cctor") {
      return JoinParts("static", declaringType.Name + "()");
    }

    return JoinParts(
        method.Accessibility,
        method.IsStatic ? "static" : string.Empty,
        method.IsAbstract ? "abstract" : method.IsVirtual ? "virtual" : string.Empty,
        signature.ReturnType ?? "void",
        friendlyName + "(" + string.Join(", ", parameters) + ")");
  }

  private static string GetFriendlyMethodName(string rawName, TypeMetadata declaringType) {
    return rawName switch {
      ".ctor" => declaringType.Name,
      ".cctor" => declaringType.Name,
      _ => rawName
    };
  }

  private static string GetTypeKind(TypeDefinition definition, string baseType) {
    if ((definition.Attributes & TypeAttributes.Interface) != 0) {
      return "interface";
    }

    if (string.Equals(baseType, "System.Enum", StringComparison.Ordinal)) {
      return "enum";
    }

    if (string.Equals(baseType, "System.MulticastDelegate", StringComparison.Ordinal)
        || string.Equals(baseType, "System.Delegate", StringComparison.Ordinal)) {
      return "delegate";
    }

    if (string.Equals(baseType, "System.ValueType", StringComparison.Ordinal)) {
      return "struct";
    }

    return "class";
  }

  private static string GetTypeAccessibility(TypeAttributes attributes) {
    return (attributes & TypeAttributes.VisibilityMask) switch {
      TypeAttributes.Public => "public",
      TypeAttributes.NestedPublic => "public",
      TypeAttributes.NestedFamily => "protected",
      TypeAttributes.NestedFamORAssem => "protected internal",
      TypeAttributes.NestedFamANDAssem => "private protected",
      TypeAttributes.NestedAssembly => "internal",
      TypeAttributes.NestedPrivate => "private",
      _ => "internal"
    };
  }

  private static string GetFieldAccessibility(FieldAttributes attributes) {
    return (attributes & FieldAttributes.FieldAccessMask) switch {
      FieldAttributes.Public => "public",
      FieldAttributes.Family => "protected",
      FieldAttributes.FamORAssem => "protected internal",
      FieldAttributes.FamANDAssem => "private protected",
      FieldAttributes.Assembly => "internal",
      FieldAttributes.Private => "private",
      _ => "private"
    };
  }

  private static string GetMethodAccessibility(MethodAttributes attributes) {
    return (attributes & MethodAttributes.MemberAccessMask) switch {
      MethodAttributes.Public => "public",
      MethodAttributes.Family => "protected",
      MethodAttributes.FamORAssem => "protected internal",
      MethodAttributes.FamANDAssem => "private protected",
      MethodAttributes.Assembly => "internal",
      MethodAttributes.Private => "private",
      _ => "private"
    };
  }

  private static string GetMostVisibleMethodAccessibility(MethodDefinition[] methods) {
    if (methods.Length == 0) {
      return "private";
    }

    return methods
        .Select(method => GetMethodAccessibility(method.Attributes))
        .OrderBy(GetAccessibilityRank)
        .First();
  }

  private static int GetAccessibilityRank(string accessibility) {
    return accessibility switch {
      "public" => 0,
      "protected internal" => 1,
      "protected" => 2,
      "internal" => 3,
      "private protected" => 4,
      _ => 5
    };
  }

  private static string JoinParts(params string[] parts) {
    return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
  }

  private static T TryDecode<T>(Func<T> decode, T fallback) {
    try {
      return decode();
    }
    catch (BadImageFormatException) {
      return fallback;
    }
    catch (InvalidOperationException) {
      return fallback;
    }
  }

  private string TryReadTargetFramework(MetadataReader reader, AssemblyDefinition assembly) {
    foreach (var handle in assembly.GetCustomAttributes()) {
      var attribute = reader.GetCustomAttribute(handle);
      var attributeType = GetCustomAttributeTypeName(reader, attribute.Constructor);
      if (!string.Equals(attributeType, "System.Runtime.Versioning.TargetFrameworkAttribute", StringComparison.Ordinal)) {
        continue;
      }

      try {
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() == 1) {
          return blob.ReadSerializedString() ?? string.Empty;
        }
      }
      catch (BadImageFormatException) {
        return string.Empty;
      }
    }

    return string.Empty;
  }

  private string GetCustomAttributeTypeName(MetadataReader reader, EntityHandle constructor) {
    if (constructor.Kind == HandleKind.MemberReference) {
      var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
      return member.Parent.Kind switch {
        HandleKind.TypeReference => MetadataNames.GetTypeReferenceFullName(reader, (TypeReferenceHandle)member.Parent, false),
        HandleKind.TypeDefinition => MetadataNames.GetTypeFullName(reader, (TypeDefinitionHandle)member.Parent, false),
        _ => string.Empty
      };
    }

    if (constructor.Kind == HandleKind.MethodDefinition) {
      var method = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
      var declaringType = method.GetDeclaringType();
      return declaringType.IsNil ? string.Empty : MetadataNames.GetTypeFullName(reader, declaringType, false);
    }

    return string.Empty;
  }

  private static string FormatPublicKeyToken(byte[] bytes) {
    if (bytes.Length == 0) {
      return string.Empty;
    }

    if (bytes.Length <= 8) {
      return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    using var sha1 = System.Security.Cryptography.SHA1.Create();
    var hash = sha1.ComputeHash(bytes);
    Array.Reverse(hash, hash.Length - 8, 8);
    return BitConverter.ToString(hash, hash.Length - 8, 8).Replace("-", string.Empty).ToLowerInvariant();
  }
}

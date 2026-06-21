using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotPeekMcp.Plugin.Metadata;

internal sealed class MetadataSignatureTypeProvider : ISignatureTypeProvider<string, MetadataSignatureContext> {
  public string GetArrayType(string elementType, ArrayShape shape) {
    var rank = Math.Max(shape.Rank, 1);
    return rank == 1
        ? elementType + "[]"
        : elementType + "[" + new string(',', rank - 1) + "]";
  }

  public string GetByReferenceType(string elementType) {
    return "ref " + elementType;
  }

  public string GetFunctionPointerType(MethodSignature<string> signature) {
    var types = signature.ParameterTypes.Concat(new[] { signature.ReturnType });
    return "delegate*<" + string.Join(", ", types) + ">";
  }

  public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) {
    return MetadataNames.TrimGenericArity(genericType) + "<" + string.Join(", ", typeArguments) + ">";
  }

  public string GetGenericMethodParameter(MetadataSignatureContext genericContext, int index) {
    return genericContext.GetMethodGenericParameter(index);
  }

  public string GetGenericTypeParameter(MetadataSignatureContext genericContext, int index) {
    return genericContext.GetTypeGenericParameter(index);
  }

  public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) {
    return unmodifiedType;
  }

  public string GetPinnedType(string elementType) {
    return elementType;
  }

  public string GetPointerType(string elementType) {
    return elementType + "*";
  }

  public string GetPrimitiveType(PrimitiveTypeCode typeCode) {
    return typeCode switch {
      PrimitiveTypeCode.Boolean => "bool",
      PrimitiveTypeCode.Byte => "byte",
      PrimitiveTypeCode.Char => "char",
      PrimitiveTypeCode.Double => "double",
      PrimitiveTypeCode.Int16 => "short",
      PrimitiveTypeCode.Int32 => "int",
      PrimitiveTypeCode.Int64 => "long",
      PrimitiveTypeCode.IntPtr => "nint",
      PrimitiveTypeCode.Object => "object",
      PrimitiveTypeCode.SByte => "sbyte",
      PrimitiveTypeCode.Single => "float",
      PrimitiveTypeCode.String => "string",
      PrimitiveTypeCode.TypedReference => "typedref",
      PrimitiveTypeCode.UInt16 => "ushort",
      PrimitiveTypeCode.UInt32 => "uint",
      PrimitiveTypeCode.UInt64 => "ulong",
      PrimitiveTypeCode.UIntPtr => "nuint",
      PrimitiveTypeCode.Void => "void",
      _ => typeCode.ToString()
    };
  }

  public string GetSZArrayType(string elementType) {
    return elementType + "[]";
  }

  public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) {
    return MetadataNames.GetTypeFullName(reader, handle, false);
  }

  public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) {
    return MetadataNames.GetTypeReferenceFullName(reader, handle, false);
  }

  public string GetTypeFromSpecification(MetadataReader reader, MetadataSignatureContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
    return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
  }
}

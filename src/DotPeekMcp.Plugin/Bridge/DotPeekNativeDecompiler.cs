using System.Linq.Expressions;
using System.Reflection;
using DotPeekMcp.Plugin.Metadata;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class DotPeekNativeDecompiler {
  private readonly object _lock = new();

  public NativeDecompilerStatus GetStatus() {
    try {
      _ = JetBrainsReflection.RequireType("JetBrains.Lifetimes", "JetBrains.Lifetimes.Lifetime");
      _ = JetBrainsReflection.RequireType("JetBrains.Platform.Core", "JetBrains.Application.Progress.ProgressIndicator");
      _ = JetBrainsReflection.RequireType("JetBrains.Platform.Core", "JetBrains.Util.FileSystemPath");
      _ = JetBrainsReflection.RequireType("JetBrains.Platform.Metadata", "JetBrains.Metadata.Reader.API.MetadataLoader");
      _ = JetBrainsReflection.RequireType("JetBrains.Platform.Metadata", "JetBrains.Metadata.Reader.API.AssemblyLocationEx");
      _ = JetBrainsReflection.RequireType("JetBrains.ReSharper.Feature.Services.ExternalSources", "JetBrains.ReSharper.Feature.Services.ExternalSources.MetadataTranslator.MetadataTranslatorOptions");
      _ = JetBrainsReflection.RequireType("JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp", "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.MetadataTranslator.CSharpMetadataTranslator");
      return new NativeDecompilerStatus {
        available = true,
        mode = "dotpeek_decompiler"
      };
    }
    catch (Exception exception) {
      return new NativeDecompilerStatus {
        available = false,
        mode = "unavailable",
        error = JetBrainsReflection.FormatException(exception)
      };
    }
  }

  public NativeDecompilerResult TryDecompileType(AssemblySession session, TypeMetadata type) {
    lock (_lock) {
      var diagnostics = new List<string>();
      object? loader = null;

      try {
        var lifetimeType = JetBrainsReflection.RequireType("JetBrains.Lifetimes", "JetBrains.Lifetimes.Lifetime");
        var progressIndicatorType = JetBrainsReflection.RequireType("JetBrains.Platform.Core", "JetBrains.Application.Progress.ProgressIndicator");
        var virtualFileSystemPathType = JetBrainsReflection.RequireType("JetBrains.Platform.Core", "JetBrains.Util.VirtualFileSystemPath");
        var metadataLoaderType = JetBrainsReflection.RequireType("JetBrains.Platform.Metadata", "JetBrains.Metadata.Reader.API.MetadataLoader");
        var metadataTypeInfoType = JetBrainsReflection.RequireType("JetBrains.Platform.Metadata", "JetBrains.Metadata.Reader.API.IMetadataTypeInfo");
        var optionsType = JetBrainsReflection.RequireType("JetBrains.ReSharper.Feature.Services.ExternalSources", "JetBrains.ReSharper.Feature.Services.ExternalSources.MetadataTranslator.MetadataTranslatorOptions");
        var translatorType = JetBrainsReflection.RequireType("JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp", "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.MetadataTranslator.CSharpMetadataTranslator");

        var lifetime = lifetimeType.GetProperty("Eternal", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("JetBrains Lifetime.Eternal property was not found.");
        var progress = Activator.CreateInstance(progressIndicatorType, lifetime)
            ?? throw new InvalidOperationException("Could not create JetBrains ProgressIndicator.");
        var options = CreateOptions(optionsType);
        var folderArray = Array.CreateInstance(virtualFileSystemPathType, 0);
        var loaderConstructor = metadataLoaderType.GetConstructor(new[] { virtualFileSystemPathType.MakeArrayType() })
            ?? throw new InvalidOperationException("MetadataLoader(VirtualFileSystemPath[]) constructor was not found.");
        loader = loaderConstructor.Invoke(new object[] { folderArray });

        diagnostics.Add("metadata_loader_resolver=empty_folders");
        var assemblyLocation = JetBrainsReflection.CreateAssemblyLocation(session.Path);
        var metadataAssembly = LoadMetadataAssembly(loader, assemblyLocation);
        if (metadataAssembly is null) {
          return NativeDecompilerResult.Failed("JetBrains MetadataLoader returned null for " + session.Path, diagnostics);
        }

        var typeInfo = ResolveTypeInfo(metadataAssembly, type, diagnostics);
        if (typeInfo is null) {
          return NativeDecompilerResult.Failed("JetBrains metadata reader could not resolve type " + type.MetadataName + ".", diagnostics);
        }

        var translator = Activator.CreateInstance(translatorType, lifetime)
            ?? throw new InvalidOperationException("Could not create CSharpMetadataTranslator.");
        var translateMethod = translatorType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => IsNativeTypeTranslateMethod(method, metadataTypeInfoType, optionsType));
        if (translateMethod is null) {
          throw new InvalidOperationException("CSharpMetadataTranslator.TranslateTopLevelTypeElementByDecompiler overload was not found.");
        }

        var source = translateMethod.Invoke(translator, new[] { typeInfo, null, options, null, progress, null, 0 }) as string;
        if (source is null || string.IsNullOrWhiteSpace(source)) {
          return NativeDecompilerResult.Failed("JetBrains decompiler returned no source for " + type.MetadataName + ".", diagnostics);
        }

        diagnostics.Add("resolved_type=" + type.MetadataName);
        return NativeDecompilerResult.Succeeded(source, diagnostics);
      }
      catch (Exception exception) {
        return NativeDecompilerResult.Failed(JetBrainsReflection.FormatException(exception), diagnostics);
      }
      finally {
        (loader as IDisposable)?.Dispose();
      }
    }
  }

  private static object CreateOptions(Type optionsType) {
    var method = optionsType.GetMethod("GetDefaultOptionsForPdb", BindingFlags.Public | BindingFlags.Static);
    if (method is not null) {
      return method.Invoke(null, new object[] { false })
          ?? throw new InvalidOperationException("MetadataTranslatorOptions.GetDefaultOptionsForPdb returned null.");
    }

    return Activator.CreateInstance(optionsType)
        ?? throw new InvalidOperationException("Could not create MetadataTranslatorOptions.");
  }

  private static object? LoadMetadataAssembly(object loader, object assemblyLocation) {
    var method = loader.GetType()
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(candidate => {
          var parameters = candidate.GetParameters();
          return candidate.Name == "TryLoadFrom"
              && parameters.Length == 4
              && parameters[0].ParameterType.FullName == "JetBrains.Metadata.Reader.API.IAssemblyLocation";
        }) ?? throw new InvalidOperationException("MetadataLoader.TryLoadFrom(IAssemblyLocation, ...) was not found.");

    var loadReferenceDelegate = CreateTrueDelegate(method.GetParameters()[1].ParameterType);
    return method.Invoke(loader, new[] { assemblyLocation, loadReferenceDelegate, true, null });
  }

  private static object? ResolveTypeInfo(object metadataAssembly, TypeMetadata type, List<string> diagnostics) {
    var method = metadataAssembly.GetType().GetMethod("GetTypeInfoFromQualifiedName", new[] { typeof(string), typeof(bool) })
        ?? throw new InvalidOperationException("IMetadataAssembly.GetTypeInfoFromQualifiedName was not found.");

    foreach (var candidate in BuildTypeNameCandidates(type)) {
      var typeInfo = method.Invoke(metadataAssembly, new object[] { candidate, false });
      if (IsResolved(typeInfo)) {
        diagnostics.Add("type_name_candidate=" + candidate);
        return typeInfo;
      }
    }

    diagnostics.Add("tried_type_names=" + string.Join(", ", BuildTypeNameCandidates(type)));
    return null;
  }

  private static bool IsResolved(object? metadataEntity) {
    if (metadataEntity is null) {
      return false;
    }

    var property = metadataEntity.GetType().GetProperty("IsResolved", BindingFlags.Public | BindingFlags.Instance);
    return property?.GetValue(metadataEntity) is true;
  }

  private static IEnumerable<string> BuildTypeNameCandidates(TypeMetadata type) {
    return new[] {
      type.MetadataName,
      type.MetadataName.Replace('+', '.'),
      RemoveGenericDisplayNames(type.FullName),
      type.FullName
    }.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.Ordinal);
  }

  private static string RemoveGenericDisplayNames(string text) {
    var result = text;
    while (true) {
      var start = result.IndexOf('<');
      if (start < 0) {
        return result;
      }

      var depth = 0;
      for (var i = start; i < result.Length; i++) {
        if (result[i] == '<') {
          depth++;
        }
        else if (result[i] == '>') {
          depth--;
          if (depth == 0) {
            result = result.Remove(start, i - start + 1);
            break;
          }
        }

        if (i == result.Length - 1) {
          return result;
        }
      }
    }
  }

  private static bool IsNativeTypeTranslateMethod(MethodInfo method, Type metadataTypeInfoType, Type optionsType) {
    if (method.Name != "TranslateTopLevelTypeElementByDecompiler") {
      return false;
    }

    var parameters = method.GetParameters();
    return parameters.Length == 7
        && parameters[0].ParameterType == metadataTypeInfoType
        && parameters[2].ParameterType == optionsType;
  }

  private static Delegate CreateTrueDelegate(Type delegateType) {
    var invoke = delegateType.GetMethod("Invoke")
        ?? throw new InvalidOperationException("Delegate type has no Invoke method: " + delegateType.FullName);
    var parameter = Expression.Parameter(invoke.GetParameters()[0].ParameterType, "value");
    return Expression.Lambda(delegateType, Expression.Constant(true), parameter).Compile();
  }

}

internal sealed class NativeDecompilerResult {
  public bool Success { get; set; }
  public string Mode { get; set; } = "unavailable";
  public string Source { get; set; } = string.Empty;
  public string Error { get; set; } = string.Empty;
  public string[] Diagnostics { get; set; } = Array.Empty<string>();

  public static NativeDecompilerResult Succeeded(string source, IEnumerable<string> diagnostics) {
    return new NativeDecompilerResult {
      Success = true,
      Mode = "dotpeek_decompiler",
      Source = source,
      Diagnostics = diagnostics.ToArray()
    };
  }

  public static NativeDecompilerResult Failed(string error, IEnumerable<string> diagnostics) {
    return new NativeDecompilerResult {
      Success = false,
      Error = error,
      Diagnostics = diagnostics.ToArray()
    };
  }
}

internal sealed class NativeDecompilerStatus {
  public bool available { get; set; }
  public string mode { get; set; } = "unavailable";
  public string error { get; set; } = string.Empty;
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

#pragma warning disable RSEXPERIMENTAL002 // SemanticModel.GetInterceptableLocation is
// evaluation-only in Microsoft.CodeAnalysis.CSharp 4.11.0 (the version this project
// pins). Verified against the real compiler in a throwaway spike before this code was
// written: the (int version, string data) InterceptsLocationAttribute shape and the
// GetInterceptableLocation()/GetInterceptsLocationAttributeSyntax() API pair are the
// ONLY mechanism this compiler accepts for interceptors - the legacy (filePath, line,
// column) constructor compiles but is flagged obsolete (CS9270) and points here.

namespace Loom.Telemetry.Generators
{
    [Generator]
    public sealed class LoomProfileGenerator : IIncrementalGenerator
    {
        // Fixed namespace the generated interceptor classes live in. Consumers must set
        // MSBuild property InterceptorsNamespaces to include this namespace for the
        // interceptors to take effect. The package ships build/buildTransitive props
        // that do this automatically - see Loom.Telemetry/build/LoomDiagnostics.Telemetry.props.
        internal const string InterceptorsNamespace = "Loom.Telemetry.GeneratedInterceptors";

        // [LoomProfile] on a concrete class/struct method only intercepts calls that
        // resolve directly to THAT method. A call made through an interface-typed
        // reference resolves to the interface's method symbol instead, which carries no
        // attribute unless the interface method itself is tagged - see PACKAGE.md's
        // "Interfaces and dependency injection" section. That failure is otherwise
        // silent: clean build, clean publish, no metrics. This diagnostic is the fix for
        // the failure being undiscoverable - see PROMPT-interface-warning.md.
        internal static readonly DiagnosticDescriptor InterfaceDispatchNotCoveredWarning = new(
            id: "LOOM0001",
            title: "LoomProfile does not intercept calls made through this interface",
            messageFormat:
                "[LoomProfile] on '{0}.{1}' will not intercept calls made through interface " +
                "'{2}', because '{2}.{1}' does not itself carry [LoomProfile]. Add " +
                "[LoomProfile] to '{2}.{1}' to cover calls made through that interface " +
                "(dependency-injected callers typically call through the interface, not the " +
                "concrete type).",
            category: "Loom.Telemetry.Interceptors",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Emit attributes
            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("LoomAttributes.g.cs", AttributesSource);
            });

            // Find profiled methods
            var profiledMethods = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Loom.Telemetry.LoomProfileAttribute",
                    predicate: static (node, _) => node is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetMethodInfo(ctx))
                .Where(static m => m != null)
                .Collect();

            // Find every CALL SITE of a [LoomProfile]-tagged method, anywhere in this
            // compilation, and pair it with an interceptable location. This is what
            // makes existing, unmodified call sites - the way every real caller already
            // writes them - actually route through the timing/error-recording code.
            // The *_Profiled() extension method above records nothing unless a caller
            // opts in to the new name; this pipeline requires no caller changes at all.
            var callSites = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is InvocationExpressionSyntax,
                    transform: static (ctx, ct) => GetCallSiteInfo(ctx, ct))
                .Where(static c => c != null)
                .Collect()
                .Combine(context.CompilationProvider);

            context.RegisterSourceOutput(callSites, static (spc, pair) =>
            {
                var (sites, compilation) = pair;
                if (sites.IsDefaultOrEmpty)
                    return;

                var source = GenerateInterceptors(sites!, compilation);
                spc.AddSource("LoomProfileInterceptors.g.cs", source);
            });

            // Find tracked properties
            var trackedProperties = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Loom.Telemetry.LoomTrackAttribute",
                    predicate: static (node, _) => node is PropertyDeclarationSyntax,
                    transform: static (ctx, _) => GetPropertyInfo(ctx))
                .Where(static p => p != null)
                .Collect();

            // Generate profiled wrappers
            context.RegisterSourceOutput(profiledMethods, static (spc, methods) =>
            {
                foreach (var method in methods)
                {
                    ReportInterfaceDispatchWarningIfNeeded(spc, method!);
                }

                foreach (var group in methods.GroupBy(m => m!.ContainingType))
                {
                    var source = GenerateProfiledWrappers(group.Key, group.ToList()!);
                    var fileName = SanitizeFileName($"{group.Key}.LoomProfile.g.cs");
                    spc.AddSource(fileName, source);
                }
            });

            // Generate tracked property implementations
            context.RegisterSourceOutput(trackedProperties, static (spc, properties) =>
            {
                foreach (var group in properties.GroupBy(p => p!.ContainingType))
                {
                    var source = GenerateTrackedProperties(group.Key, group.ToList()!);
                    var fileName = SanitizeFileName($"{group.Key}.LoomTrack.g.cs");
                    spc.AddSource(fileName, source);
                }
            });
        }

        private static MethodInfo? GetMethodInfo(GeneratorAttributeSyntaxContext context)
        {
            if (context.TargetNode is not MethodDeclarationSyntax method)
                return null;

            var symbol = context.TargetSymbol as IMethodSymbol;
            if (symbol == null)
                return null;

            var attribute = context.Attributes.FirstOrDefault();
            var explicitName = attribute?.NamedArguments
                .FirstOrDefault(kv => kv.Key == "Name")
                .Value.Value as string;

            // Prefer the attribute's own location so the interface-dispatch warning
            // (Phase 1, PROMPT-interface-warning.md) lands on the [LoomProfile] tag
            // itself; fall back to the method's name if the attribute syntax is
            // unavailable for any reason.
            var diagnosticLocation = attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? method.Identifier.GetLocation();

            return new MethodInfo
            {
                Method = method,
                Symbol = symbol,
                ContainingType = symbol.ContainingType.ToDisplayString(),
                MetricName = explicitName ?? $"{symbol.ContainingType.Name}.{symbol.Name}",
                IsAsync = symbol.IsAsync,
                ReturnsVoid = symbol.ReturnsVoid,
                ReturnType = symbol.ReturnType.ToDisplayString(),
                IsStatic = symbol.IsStatic,
                DiagnosticLocation = diagnosticLocation
            };
        }

        /// <summary>
        /// Phase 1 of PROMPT-interface-warning.md: [LoomProfile] on a concrete method
        /// only intercepts calls that resolve directly to it. A call made through an
        /// interface reference resolves to the interface's own method symbol, which
        /// carries no attribute unless tagged separately - and that failure is
        /// otherwise silent. Warns when ALL of:
        ///   - the tagged method is declared on a class or struct (not an interface -
        ///     tagging the interface method directly is the documented fix and must
        ///     stay silent);
        ///   - the containing type implements an interface whose member this method
        ///     implements;
        ///   - that interface member does NOT itself carry [LoomProfile] (a
        ///     correctly-tagged interface method must not also warn here).
        /// </summary>
        private static void ReportInterfaceDispatchWarningIfNeeded(SourceProductionContext spc, MethodInfo info)
        {
            var symbol = info.Symbol;
            var containingType = symbol.ContainingType;

            if (containingType.TypeKind != TypeKind.Class && containingType.TypeKind != TypeKind.Struct)
                return;

            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var interfaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);
                    if (!SymbolEqualityComparer.Default.Equals(implementation, symbol))
                        continue;

                    var interfaceMemberIsTagged = interfaceMember.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == "Loom.Telemetry.LoomProfileAttribute");
                    if (interfaceMemberIsTagged)
                        continue;

                    spc.ReportDiagnostic(Diagnostic.Create(
                        InterfaceDispatchNotCoveredWarning,
                        info.DiagnosticLocation,
                        containingType.Name,
                        symbol.Name,
                        iface.ToDisplayString()));
                }
            }
        }

        /// <summary>
        /// Resolves the target of an invocation and, if it carries [LoomProfile], returns
        /// the interceptable location for the call plus the same MethodInfo shape used by
        /// the *_Profiled() wrapper generation, so both paths can share body codegen.
        /// </summary>
        private static CallSiteInfo? GetCallSiteInfo(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            if (context.Node is not InvocationExpressionSyntax invocation)
                return null;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                return null;

            var profileAttribute = methodSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Loom.Telemetry.LoomProfileAttribute");
            if (profileAttribute == null)
                return null;

            var location = context.SemanticModel.GetInterceptableLocation(invocation, ct);
            if (location == null)
                return null;

            var explicitName = profileAttribute.NamedArguments
                .FirstOrDefault(kv => kv.Key == "Name")
                .Value.Value as string;

            var methodInfo = new MethodInfo
            {
                Method = null!,
                Symbol = methodSymbol,
                ContainingType = methodSymbol.ContainingType.ToDisplayString(),
                MetricName = explicitName ?? $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
                IsAsync = methodSymbol.IsAsync,
                ReturnsVoid = methodSymbol.ReturnsVoid,
                ReturnType = methodSymbol.ReturnType.ToDisplayString(),
                IsStatic = methodSymbol.IsStatic
            };

            return new CallSiteInfo { Location = location, MethodInfo = methodInfo };
        }

        /// <summary>
        /// Emits one interceptor method per DISTINCT target method (multiple call sites
        /// to the same method share one implementation - stacking one
        /// [InterceptsLocation] attribute per call site on that single method; confirmed
        /// against the real compiler that this is supported), each carrying the same
        /// timing/try-catch body as the *_Profiled() wrapper.
        /// </summary>
        private static string GenerateInterceptors(ImmutableArray<CallSiteInfo> sites, Compilation compilation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Loom.Telemetry;");
            sb.AppendLine();

            // The compiler does not embed this attribute on its own in the pinned
            // Roslyn version (confirmed: referencing it without declaring it fails
            // CS0234). Only declare it if this compilation doesn't already have one -
            // a future SDK that ships it in the BCL must win over this shim.
            if (compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InterceptsLocationAttribute") == null)
            {
                sb.AppendLine("namespace System.Runtime.CompilerServices");
                sb.AppendLine("{");
                sb.AppendLine("    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]");
                sb.AppendLine("    file sealed class InterceptsLocationAttribute : Attribute");
                sb.AppendLine("    {");
                sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine($"namespace {InterceptorsNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    file static class LoomProfileInterceptors");
            sb.AppendLine("    {");

            var groups = sites.GroupBy(s => (ISymbol)s!.MethodInfo.Symbol, SymbolEqualityComparer.Default);
            var index = 0;
            foreach (var group in groups)
            {
                var methodInfo = group.First()!.MethodInfo;
                foreach (var site in group)
                {
                    sb.AppendLine($"        {site!.Location.GetInterceptsLocationAttributeSyntax()}");
                }

                GenerateInterceptorMethod(sb, methodInfo, $"LoomProfile_Intercept_{index}");
                index++;
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void GenerateInterceptorMethod(StringBuilder sb, MethodInfo methodInfo, string interceptorName)
        {
            var symbol = methodInfo.Symbol;
            var parameters = string.Join(", ", symbol.Parameters.Select(FormatParameter));
            var arguments = string.Join(", ", symbol.Parameters.Select(p => $"{RefKindPrefix(p.RefKind)}{p.Name}"));

            var thisParam = methodInfo.IsStatic ? "" : $"this {methodInfo.ContainingType} instance";
            var fullParams = string.IsNullOrEmpty(thisParam) ? parameters :
                string.IsNullOrEmpty(parameters) ? thisParam : $"{thisParam}, {parameters}";

            var methodCall = methodInfo.IsStatic
                ? $"{methodInfo.ContainingType}.{symbol.Name}({arguments})"
                : $"instance.{symbol.Name}({arguments})";

            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            if (methodInfo.IsAsync)
            {
                sb.AppendLine($"        public static async {methodInfo.ReturnType} {interceptorName}({fullParams})");
            }
            else
            {
                sb.AppendLine($"        public static {methodInfo.ReturnType} {interceptorName}({fullParams})");
            }
            sb.AppendLine("        {");
            AppendProfiledBody(sb, methodInfo, methodCall, "            ");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string FormatParameter(IParameterSymbol p)
        {
            return $"{RefKindPrefix(p.RefKind)}{p.Type.ToDisplayString()} {p.Name}";
        }

        private static string RefKindPrefix(RefKind refKind) => refKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => ""
        };

        private static string GenerateProfiledWrappers(string containingType, List<MethodInfo> methods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Loom.Telemetry;");
            sb.AppendLine();

            var namespaceName = GetNamespace(containingType);
            var className = GetClassName(containingType);

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Generate extension class for instance methods or static class for static methods
            sb.AppendLine($"    public static class {SanitizeIdentifier(className)}_LoomProfileExtensions");
            sb.AppendLine("    {");

            foreach (var method in methods)
            {
                GenerateProfiledWrapper(sb, method);
            }

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateProfiledWrapper(StringBuilder sb, MethodInfo methodInfo)
        {
            var symbol = methodInfo.Symbol;
            var parameters = string.Join(", ", symbol.Parameters.Select(p =>
                $"{p.Type.ToDisplayString()} {p.Name}"));

            var arguments = string.Join(", ", symbol.Parameters.Select(p => p.Name));

            var thisParam = methodInfo.IsStatic ? "" : $"this {methodInfo.ContainingType} instance";
            var fullParams = string.IsNullOrEmpty(thisParam) ? parameters :
                string.IsNullOrEmpty(parameters) ? thisParam : $"{thisParam}, {parameters}";

            var methodCall = methodInfo.IsStatic
                ? $"{methodInfo.ContainingType}.{symbol.Name}({arguments})"
                : $"instance.{symbol.Name}({arguments})";

            var wrapperName = $"{symbol.Name}_Profiled";

            sb.AppendLine($"        /// <summary>Profiled wrapper for {symbol.Name}</summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");

            if (methodInfo.IsAsync)
            {
                sb.AppendLine($"        public static async {methodInfo.ReturnType} {wrapperName}({fullParams})");
            }
            else
            {
                sb.AppendLine($"        public static {methodInfo.ReturnType} {wrapperName}({fullParams})");
            }
            sb.AppendLine("        {");
            AppendProfiledBody(sb, methodInfo, methodCall, "            ");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// Shared timing/try-catch body used by both the *_Profiled() wrapper and the
        /// generated interceptors, so sync/async recording logic exists in exactly one
        /// place. <paramref name="indent"/> is the leading whitespace for each line.
        /// </summary>
        private static void AppendProfiledBody(StringBuilder sb, MethodInfo methodInfo, string methodCall, string indent)
        {
            sb.AppendLine($"{indent}var __startTicks = Stopwatch.GetTimestamp();");
            sb.AppendLine($"{indent}try");
            sb.AppendLine($"{indent}{{");

            if (methodInfo.IsAsync)
            {
                if (methodInfo.ReturnType == "System.Threading.Tasks.Task")
                {
                    sb.AppendLine($"{indent}    await {methodCall}.ConfigureAwait(false);");
                    sb.AppendLine($"{indent}    var __elapsed = Stopwatch.GetElapsedTime(__startTicks);");
                    sb.AppendLine($"{indent}    LoomRuntime.RecordMethodExecution(\"{methodInfo.MetricName}\", __elapsed, null);");
                }
                else
                {
                    sb.AppendLine($"{indent}    var __result = await {methodCall}.ConfigureAwait(false);");
                    sb.AppendLine($"{indent}    var __elapsed = Stopwatch.GetElapsedTime(__startTicks);");
                    sb.AppendLine($"{indent}    LoomRuntime.RecordMethodExecution(\"{methodInfo.MetricName}\", __elapsed, null);");
                    sb.AppendLine($"{indent}    return __result;");
                }
            }
            else
            {
                if (methodInfo.ReturnsVoid)
                {
                    sb.AppendLine($"{indent}    {methodCall};");
                    sb.AppendLine($"{indent}    var __elapsed = Stopwatch.GetElapsedTime(__startTicks);");
                    sb.AppendLine($"{indent}    LoomRuntime.RecordMethodExecution(\"{methodInfo.MetricName}\", __elapsed, null);");
                }
                else
                {
                    sb.AppendLine($"{indent}    var __result = {methodCall};");
                    sb.AppendLine($"{indent}    var __elapsed = Stopwatch.GetElapsedTime(__startTicks);");
                    sb.AppendLine($"{indent}    LoomRuntime.RecordMethodExecution(\"{methodInfo.MetricName}\", __elapsed, null);");
                    sb.AppendLine($"{indent}    return __result;");
                }
            }

            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}catch (Exception __ex)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    var __elapsed = Stopwatch.GetElapsedTime(__startTicks);");
            sb.AppendLine($"{indent}    LoomRuntime.RecordMethodExecution(\"{methodInfo.MetricName}\", __elapsed, __ex);");
            sb.AppendLine($"{indent}    throw;");
            sb.AppendLine($"{indent}}}");
        }

        private static PropertyInfo? GetPropertyInfo(GeneratorAttributeSyntaxContext context)
        {
            if (context.TargetNode is not PropertyDeclarationSyntax property)
                return null;

            var symbol = context.TargetSymbol as IPropertySymbol;
            if (symbol == null)
                return null;

            // Only support auto-properties (no custom getter/setter)
            if (property.AccessorList == null)
                return null;

            var attribute = context.Attributes.FirstOrDefault();
            var explicitName = attribute?.NamedArguments
                .FirstOrDefault(kv => kv.Key == "Name")
                .Value.Value as string;

            return new PropertyInfo
            {
                Property = property,
                Symbol = symbol,
                ContainingType = symbol.ContainingType.ToDisplayString(),
                MetricName = explicitName ?? $"{symbol.ContainingType.Name}.{symbol.Name}",
                PropertyType = symbol.Type.ToDisplayString(),
                PropertyName = symbol.Name
            };
        }

        private static string GenerateTrackedProperties(string containingType, List<PropertyInfo> properties)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using Loom.Telemetry;");
            sb.AppendLine();

            var namespaceName = GetNamespace(containingType);
            var className = GetClassName(containingType);

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    partial class {className}");
            sb.AppendLine("    {");

            foreach (var prop in properties)
            {
                GenerateTrackedProperty(sb, prop);
            }

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateTrackedProperty(StringBuilder sb, PropertyInfo prop)
        {
            var backingField = $"__loom_tracked_{prop.PropertyName}";

            // Generate backing field
            sb.AppendLine($"        private {prop.PropertyType} {backingField};");
            sb.AppendLine();

            // Generate property with tracking
            sb.AppendLine($"        /// <summary>Tracked property - records changes to LoomMetrics</summary>");
            sb.AppendLine($"        public {prop.PropertyType} {prop.PropertyName}_Tracked");
            sb.AppendLine("        {");
            sb.AppendLine($"            get => {backingField};");
            sb.AppendLine("            set");
            sb.AppendLine("            {");
            sb.AppendLine($"                {backingField} = value;");
            sb.AppendLine($"                LoomRuntime.RecordPropertyChange(\"{prop.MetricName}\", value);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string GetNamespace(string fullTypeName)
        {
            var lastDot = fullTypeName.LastIndexOf('.');
            return lastDot > 0 ? fullTypeName.Substring(0, lastDot) : string.Empty;
        }

        private static string GetClassName(string fullTypeName)
        {
            var lastDot = fullTypeName.LastIndexOf('.');
            return lastDot > 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
        }

        private static string SanitizeFileName(string name)
        {
            return name.Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(':', '_');
        }

        private static string SanitizeIdentifier(string name)
        {
            return name.Replace('.', '_').Replace('<', '_').Replace('>', '_');
        }

        private const string AttributesSource = @"// <auto-generated/>
#nullable enable
using System;

namespace Loom.Telemetry
{
    /// <summary>
    /// Marks a method for automatic profiling instrumentation.
    /// The generator will create a *_Profiled() wrapper method with timing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LoomProfileAttribute : Attribute
    {
        /// <summary>Optional explicit metric name. Defaults to ClassName.MethodName</summary>
        public string? Name { get; init; }
    }

    /// <summary>
    /// Marks a property for value-change tracking.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LoomTrackAttribute : Attribute
    {
        /// <summary>Optional explicit metric name. Defaults to ClassName.PropertyName</summary>
        public string? Name { get; init; }
    }
}";

        private sealed class MethodInfo
        {
            public MethodDeclarationSyntax Method { get; init; } = null!;
            public IMethodSymbol Symbol { get; init; } = null!;
            public string ContainingType { get; init; } = null!;
            public string MetricName { get; init; } = null!;
            public bool IsAsync { get; init; }
            public bool ReturnsVoid { get; init; }
            public string ReturnType { get; init; } = null!;
            public bool IsStatic { get; init; }
            public Location DiagnosticLocation { get; init; } = Location.None;
        }

        private sealed class PropertyInfo
        {
            public PropertyDeclarationSyntax Property { get; init; } = null!;
            public IPropertySymbol Symbol { get; init; } = null!;
            public string ContainingType { get; init; } = null!;
            public string MetricName { get; init; } = null!;
            public string PropertyType { get; init; } = null!;
            public string PropertyName { get; init; } = null!;
        }

        private sealed class CallSiteInfo
        {
            public InterceptableLocation Location { get; init; } = null!;
            public MethodInfo MethodInfo { get; init; } = null!;
        }
    }
}

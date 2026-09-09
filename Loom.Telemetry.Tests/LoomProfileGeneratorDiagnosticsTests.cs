using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Loom.Telemetry.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Loom.Telemetry.Tests;

/// <summary>
/// Runs LoomProfileGenerator directly through a CSharpGeneratorDriver against small,
/// throwaway compilations - this is PROMPT-interface-warning.md's Phase 2 coverage for
/// LOOM0001, the warning for a [LoomProfile]'d method whose interface counterpart is
/// untagged. [LoomProfile] itself (via RegisterPostInitializationOutput) is available to
/// each test's source without any extra setup: post-init output is part of the same
/// generator pass that then runs ForAttributeWithMetadataName over it.
/// </summary>
public sealed class LoomProfileGeneratorDiagnosticsTests
{
    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var trustedAssembliesPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = trustedAssembliesPaths
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "LoomProfileGeneratorDiagnosticsTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new LoomProfileGenerator());
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult().Diagnostics;
    }

    private static Diagnostic[] Loom0001Diagnostics(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Id == "LOOM0001").ToArray();

    [Fact]
    public void ClassMethodImplementingUntaggedInterfaceMember_Warns_AndNamesTheInterface()
    {
        const string source = """
            using Loom.Telemetry;

            public interface IWidget
            {
                int Compute(int x);
            }

            public class Widget : IWidget
            {
                [LoomProfile]
                public int Compute(int x) => x;
            }
            """;

        var warnings = Loom0001Diagnostics(RunGenerator(source));

        var warning = Assert.Single(warnings);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("IWidget", warning.GetMessage());
        Assert.Contains("Widget", warning.GetMessage());
        Assert.Contains("Compute", warning.GetMessage());
    }

    [Fact]
    public void InterfaceMethodTagged_DoesNotWarn()
    {
        const string source = """
            using Loom.Telemetry;

            public interface IGadget
            {
                [LoomProfile]
                int Compute(int x);
            }

            public class Gadget : IGadget
            {
                public int Compute(int x) => x;
            }
            """;

        var warnings = Loom0001Diagnostics(RunGenerator(source));

        Assert.Empty(warnings);
    }

    [Fact]
    public void ClassMethodWithNoInterface_DoesNotWarn()
    {
        const string source = """
            using Loom.Telemetry;

            public class StandaloneService
            {
                [LoomProfile]
                public int Compute(int x) => x;
            }
            """;

        var warnings = Loom0001Diagnostics(RunGenerator(source));

        Assert.Empty(warnings);
    }

    [Fact]
    public void TwoInterfaces_OnlyOneDeclaresTheMethod_WarnsAndNamesTheRightOne()
    {
        const string source = """
            using Loom.Telemetry;

            public interface IComputer
            {
                int Compute(int x);
            }

            public interface IResetter
            {
                void Reset();
            }

            public class MultiInterfaceWidget : IComputer, IResetter
            {
                [LoomProfile]
                public int Compute(int x) => x;

                public void Reset() { }
            }
            """;

        var warnings = Loom0001Diagnostics(RunGenerator(source));

        var warning = Assert.Single(warnings);
        Assert.Contains("IComputer", warning.GetMessage());
        Assert.DoesNotContain("IResetter", warning.GetMessage());
    }

    [Fact]
    public void ClassMethodAndInterfaceMemberBothTagged_DoesNotDoubleWarn()
    {
        const string source = """
            using Loom.Telemetry;

            public interface IBothTagged
            {
                [LoomProfile]
                int Compute(int x);
            }

            public class BothTaggedWidget : IBothTagged
            {
                [LoomProfile]
                public int Compute(int x) => x;
            }
            """;

        var warnings = Loom0001Diagnostics(RunGenerator(source));

        Assert.Empty(warnings);
    }
}

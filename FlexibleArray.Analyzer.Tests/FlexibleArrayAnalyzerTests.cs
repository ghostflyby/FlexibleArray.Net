using System.Collections.Immutable;
using FlexibleArray.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FlexibleArray.Analyzer.Tests;

public sealed class FlexibleArrayAnalyzerTests
{
    private const string HeaderPrelude = """
        using System.Runtime.InteropServices;
        using FlexibleArray;

        """;

    // The code fix keys on the id alone; this is the descriptor the analyzer reports FA0004 with.
    private static DiagnosticDescriptor DiagnosticDescriptorsDllImport => FlexibleArrayAnalyzer.DllImportDescriptor;

    [Fact]
    public async Task CompliantCountFieldHeader_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            struct CountHeader : IFlexibleArrayHeader<CountHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref CountHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in CountHeader header) => header.Count;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GetFirstElementOfAnotherStructsField_ReportsFA0001()
    {
        var source = HeaderPrelude + """
            struct InnerHeader { public int FirstElement; }
            struct WrongOwnerHeader : IFlexibleArrayHeader<WrongOwnerHeader, int>
            {
                public InnerHeader Inner;
                public int FirstElement;

                public static ref int GetFirstElement(ref WrongOwnerHeader header) => ref header.Inner.FirstElement;
                public static int GetTailElementCount(scoped in WrongOwnerHeader header) => 0;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0001", 9, 27));
    }

    [Fact]
    public async Task GetFirstElementOfWrongElementType_ReportsFA0001()
    {
        var source = HeaderPrelude + """
            struct WrongElementHeader : IFlexibleArrayHeader<WrongElementHeader, int>
            {
                public int Count;
                public int FirstElement;
                public long NotAnElement;

                public static ref int GetFirstElement(ref WrongElementHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in WrongElementHeader header) => header.Count;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0002", 6, 16));
    }

    [Fact]
    public async Task AutoLayout_ReportsFA0003()
    {
        var source = HeaderPrelude + """
            [StructLayout(LayoutKind.Auto)]
            struct AutoHeader : IFlexibleArrayHeader<AutoHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref AutoHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in AutoHeader header) => header.Count;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0003", 4, 8));
    }

    [Fact]
    public async Task ExplicitLayout_SkipsThePlacementCheck()
    {
        var source = HeaderPrelude + """
            [StructLayout(LayoutKind.Explicit)]
            struct ExplicitHeader : IFlexibleArrayHeader<ExplicitHeader, int>
            {
                [FieldOffset(0)] public int FirstElement;
                [FieldOffset(8)] public int Count;

                public static ref int GetFirstElement(ref ExplicitHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ExplicitHeader header) => header.Count;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WalkLengthImplementingWriteBack_ReportsFA0005()
    {
        var source = HeaderPrelude + """
            using System.Runtime.CompilerServices;

            struct WalkHeader
                : IFlexibleArrayHeader<WalkHeader, int>, IConstructibleFlexibleArrayHeader<WalkHeader, int>
            {
                public int FirstElement;

                public static ref int GetFirstElement(ref WalkHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in WalkHeader header)
                {
                    ref var h = ref Unsafe.AsRef(in header);
                    var length = 0;
                    while (Unsafe.Add(ref GetFirstElement(ref h), length) != 0)
                    {
                        length++;
                    }

                    return length;
                }
                public WalkHeader WithTailElementCount(int count) => this;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0005", 11, 23));
    }

    [Fact]
    public async Task WalkLengthWithoutWriteBack_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            using System.Runtime.CompilerServices;

            struct WalkOnlyHeader : IFlexibleArrayHeader<WalkOnlyHeader, int>
            {
                public int FirstElement;

                public static ref int GetFirstElement(ref WalkOnlyHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in WalkOnlyHeader header)
                {
                    ref var h = ref Unsafe.AsRef(in header);
                    var length = 0;
                    while (Unsafe.Add(ref GetFirstElement(ref h), length) != 0)
                    {
                        length++;
                    }

                    return length;
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DllImportWithFlexibleParameter_ReportsFA0004()
    {
        var source = HeaderPrelude + """
            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Native
            {
                [DllImport("native", EntryPoint = "read")]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0004", 15, 32));
    }

    [Fact]
    public async Task DllImportWithFlexibleReturn_ReportsFA0004()
    {
        var source = HeaderPrelude + """
            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Native
            {
                [DllImport("native")]
                internal static extern MutableFlexibleSpan<ImportHeader, int> Load();
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0004", 15, 67));
    }

    [Fact]
    public async Task DllImportWithoutFlexibleTypes_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            static class Native
            {
                [DllImport("native", EntryPoint = "read")]
                internal static extern int Count(nint view, int size);
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task LibraryImportWithFlexibleParameter_ProducesNoDiagnostics()
    {
        // The generated implementation is out of scope here, so the import is written as a
        // plain (non-partial) extern method: the analyzer keys on DllImport, not on the form.
        var source = HeaderPrelude + """
            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            internal static class Native
            {
                [LibraryImport("native", EntryPoint = "read")]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task DllImportCodeFix_RewritesToLibraryImport()
    {
        var source = HeaderPrelude + """
            using System.Runtime.CompilerServices;

            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Native
            {
                [DllImport("native", EntryPoint = "read")]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        var fixedSource = await ApplyFixAsync(source, "FA0004");
        Assert.Contains("LibraryImport", fixedSource);
        Assert.DoesNotContain("DllImport", fixedSource);
        Assert.Contains("partial class Native", fixedSource);
        Assert.Contains("partial int Count", fixedSource);
    }

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        Assert.True(diagnostics.IsEmpty,
            diagnostics.Length > 0 ? $"Expected no diagnostics, got: {diagnostics[0].Id} at {diagnostics[0].Location.GetLineSpan()}" : "");
    }

    private static async Task VerifyDiagnosticsAsync(string source, params (string Id, int Line, int Column)[] expected)
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        var actual = diagnostics
            .Select(d =>
            {
                var position = d.Location.GetLineSpan().StartLinePosition;
                return (d.Id, position.Line + 1, position.Character + 1);
            })
            .ToImmutableArray();

        Assert.Equal(
            expected.Select(e => (e.Id, e.Line, e.Column)).OrderBy(t => t),
            actual.OrderBy(t => t));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new FlexibleArrayAnalyzer()));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> ApplyFixAsync(string source, string diagnosticId)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(CreateReferences());
        var document = project.AddDocument("Test.cs", source);

        var model = await document.GetSemanticModelAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(model);

        // The diagnostic comes from the analyzer, not from the compiler, so it is built here
        // with the span the analyzer would report.
        var method = model!.SyntaxTree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "Count");
        var target = Diagnostic.Create(
            DiagnosticDescriptorsDllImport,
            method.Identifier.GetLocation());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document, target, (a, _) => actions.Add(a), TestContext.Current.CancellationToken);
        await new DllImportCodeFixProvider().RegisterCodeFixesAsync(context);
        var action = Assert.Single(actions);

        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id);
        Assert.NotNull(changed);
        return (await changed!.GetTextAsync()).ToString();
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "TestProject",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            CreateReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    // The full trusted-platform set: partial reference lists cannot resolve unmanaged
    // constraints, and the analyzer must see the same surface a consumer compile sees.
    private static IEnumerable<MetadataReference> CreateReferences()
    {
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        yield return MetadataReference.CreateFromFile(typeof(IFlexibleArrayHeader<,>).Assembly.Location);
    }
}

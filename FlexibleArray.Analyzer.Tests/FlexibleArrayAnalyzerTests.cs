using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlexibleArray.Analyzer.Tests;

public sealed class FlexibleArrayAnalyzerTests
{
    private const string HeaderPrelude = """
        using System.Runtime.InteropServices;
        using FlexibleArray;

        """;

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
    public async Task GetFirstElementWithFieldAfterPlaceholder_ReportsFA0002()
    {
        var source = HeaderPrelude + """
            struct TrailingFieldHeader : IFlexibleArrayHeader<TrailingFieldHeader, int>
            {
                public int Count;
                public int FirstElement;
                public long NotAnElement;

                public static ref int GetFirstElement(ref TrailingFieldHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in TrailingFieldHeader header) => header.Count;
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
    public async Task ExplicitLayoutWithNoOverlap_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            [StructLayout(LayoutKind.Explicit)]
            struct ExplicitHeader : IFlexibleArrayHeader<ExplicitHeader, int>
            {
                [FieldOffset(0)] public int Count;
                [FieldOffset(8)] public int FirstElement;

                public static ref int GetFirstElement(ref ExplicitHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ExplicitHeader header) => header.Count;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ExplicitLayoutWithOverlappingField_ReportsFA0002()
    {
        var source = HeaderPrelude + """
            [StructLayout(LayoutKind.Explicit)]
            struct OverlappingHeader : IFlexibleArrayHeader<OverlappingHeader, int>
            {
                [FieldOffset(0)] public int FirstElement;
                [FieldOffset(8)] public int Count;

                public static ref int GetFirstElement(ref OverlappingHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in OverlappingHeader header) => header.Count;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0002", 6, 33));
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
    public async Task WalkLengthWithTwoCalls_ReportsFA0005Once()
    {
        var source = HeaderPrelude + """
            using System.Runtime.CompilerServices;

            struct DoubleWalkHeader
                : IFlexibleArrayHeader<DoubleWalkHeader, int>, IConstructibleFlexibleArrayHeader<DoubleWalkHeader, int>
            {
                public int FirstElement;

                public static ref int GetFirstElement(ref DoubleWalkHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in DoubleWalkHeader header)
                {
                    ref var h = ref Unsafe.AsRef(in header);
                    var length = 0;
                    while (Unsafe.Add(ref GetFirstElement(ref h), length) != 0)
                    {
                        length++;
                    }

                    return length + Unsafe.Add(ref GetFirstElement(ref h), length);
                }
                public DoubleWalkHeader WithTailElementCount(int count) => this;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0005", 11, 23));
    }

    [Fact]
    public async Task WalkOverAnotherHeadersPlaceholder_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            struct OtherHeader : IFlexibleArrayHeader<OtherHeader, int>
            {
                public int FirstElement;

                public static ref int GetFirstElement(ref OtherHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in OtherHeader header) => 0;
            }

            struct ComposingHeader
                : IFlexibleArrayHeader<ComposingHeader, int>, IConstructibleFlexibleArrayHeader<ComposingHeader, int>
            {
                public int FirstElement;

                public static ref int GetFirstElement(ref ComposingHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ComposingHeader header)
                {
                    var other = default(OtherHeader);
                    _ = OtherHeader.GetFirstElement(ref other);
                    return 0;
                }
                public ComposingHeader WithTailElementCount(int count) => this;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SameNamedUnrelatedMethod_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            struct CountHeader : IFlexibleArrayHeader<CountHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref CountHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in CountHeader header) => header.Count;

                public static ref int GetFirstElement(ref CountHeader header, int slot) => ref header.FirstElement;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
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
    public async Task GetFirstElementThroughStaticStorage_ReportsFA0001()
    {
        var source = HeaderPrelude + """
            struct SharedHeader : IFlexibleArrayHeader<SharedHeader, int>
            {
                public static SharedHeader Shared;

                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref SharedHeader header) => ref Shared.FirstElement;
                public static int GetTailElementCount(scoped in SharedHeader header) => header.Count;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0001", 10, 27));
    }

    [Fact]
    public async Task GetFirstElementThroughAutoPropertyAfterPlaceholder_ReportsFA0002()
    {
        var source = HeaderPrelude + """
            struct TrailingPropertyHeader : IFlexibleArrayHeader<TrailingPropertyHeader, int>
            {
                public int Count;
                public int FirstElement;
                public long NotAnElement { get; set; }

                public static ref int GetFirstElement(ref TrailingPropertyHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in TrailingPropertyHeader header) => header.Count;
            }
            """;

        await VerifyDiagnosticsAsync(source, ("FA0002", 6, 16));
    }

    [Fact]
    public async Task ExplicitContractImplementation_ProducesNoDiagnostics()
    {
        var source = HeaderPrelude + """
            struct ExplicitHeader : IFlexibleArrayHeader<ExplicitHeader, int>
            {
                public int Count;
                public int FirstElement;

                static ref int IFlexibleArrayHeader<ExplicitHeader, int>.GetFirstElement(ref ExplicitHeader header)
                    => ref header.FirstElement;
                static int IFlexibleArrayHeader<ExplicitHeader, int>.GetTailElementCount(scoped in ExplicitHeader header)
                    => header.Count;
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

        var fixedSource = await ApplyFixAsync(source);

        Assert.Contains("[global::System.Runtime.InteropServices.LibraryImport(\"native\", EntryPoint = \"read\")]", fixedSource);
        Assert.DoesNotContain("[DllImport", fixedSource);
        Assert.Contains("partial class Native", fixedSource);
        Assert.Contains("internal static partial int Count(", fixedSource);
        Assert.DoesNotContain("extern", fixedSource);
        // The LibraryImport generator is absent in this harness, so the only remaining error is
        // the expected missing implementation declaration of the rewritten partial method.
        Assert.All(
            CreateCompilation(fixedSource).GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error),
            error => Assert.Equal("CS8795", error.Id));
    }

    [Fact]
    public async Task DllImportCodeFix_OffersNothingForStringSignature()
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
                internal static extern int Open(string path, FlexibleSpan<ImportHeader, int> view);
            }
            """;

        Assert.Empty(await GetCodeFixActionsAsync(source));
    }

    [Fact]
    public async Task DllImportCodeFix_OffersNothingForNonPortableArgument()
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
                [DllImport("native", CharSet = CharSet.Unicode)]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        Assert.Empty(await GetCodeFixActionsAsync(source));
    }

    [Fact]
    public async Task DllImportCodeFix_FollowsAnAliasedAttribute()
    {
        var source = HeaderPrelude + """
            using DI = System.Runtime.InteropServices.DllImportAttribute;

            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Native
            {
                [DI("native")]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        var fixedSource = await ApplyFixAsync(source);

        Assert.Contains("[global::System.Runtime.InteropServices.LibraryImport(\"native\")]", fixedSource);
        // Only the missing generated implementation is expected without the generator.
        Assert.All(
            CreateCompilation(fixedSource).GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error),
            error => Assert.Equal("CS8795", error.Id));
    }

    [Fact]
    public async Task DllImportCodeFix_LeavesASameNamedUserAttributeAlone()
    {
        var source = HeaderPrelude + """
            sealed class FooDllImportAttribute : System.Attribute { }

            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Native
            {
                [FooDllImport]
                [DllImport("native")]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        var fixedSource = await ApplyFixAsync(source);

        Assert.Contains("[FooDllImport]", fixedSource);
        Assert.Contains("[global::System.Runtime.InteropServices.LibraryImport(\"native\")]", fixedSource);
    }

    [Fact]
    public async Task DllImportCodeFix_SurvivesABareDllImportAttribute()
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
                [DllImport]
                internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
            }
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(CreateReferences());
        var document = project.AddDocument("Test.cs", source);

        // The bare attribute is a compiler error (CS7036), and analyzers run on error code.
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        var target = diagnostics.Single(d => d.Id == "FA0004");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document, target, (a, _) => actions.Add(a), TestContext.Current.CancellationToken);
        await new DllImportCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.Empty(actions);
    }

    [Fact]
    public async Task DllImportCodeFix_MakesTheWholeNestingChainPartial()
    {
        var source = HeaderPrelude + """
            struct ImportHeader : IFlexibleArrayHeader<ImportHeader, int>
            {
                public int Count;
                public int FirstElement;

                public static ref int GetFirstElement(ref ImportHeader header) => ref header.FirstElement;
                public static int GetTailElementCount(scoped in ImportHeader header) => header.Count;
            }

            static class Outer
            {
                static class Native
                {
                    [DllImport("native")]
                    internal static extern int Count(FlexibleSpan<ImportHeader, int> view);
                }
            }
            """;

        var fixedSource = await ApplyFixAsync(source);

        Assert.Contains("static partial class Outer", fixedSource);
        Assert.Contains("static partial class Native", fixedSource);
        Assert.Contains("internal static partial int Count(", fixedSource);
        Assert.DoesNotContain("extern", fixedSource);
        Assert.All(
            CreateCompilation(fixedSource).GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error),
            error => Assert.Equal("CS8795", error.Id));
    }

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        AssertCompiles(source);
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        Assert.True(diagnostics.IsEmpty,
            diagnostics.Length > 0 ? $"Expected no diagnostics, got: {diagnostics[0].Id} at {diagnostics[0].Location.GetLineSpan()}" : "");
    }

    private static async Task VerifyDiagnosticsAsync(string source, params (string Id, int Line, int Column)[] expected)
    {
        AssertCompiles(source);
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
        var withAnalyzers = compilation.WithAnalyzers([new FlexibleArrayAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static void AssertCompiles(string source)
    {
        var errors = CreateCompilation(source).GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
    }

    private static async Task<string> ApplyFixAsync(string source)
    {
        var (document, _, actions) = await RegisterCodeFixActionsAsync(source);
        var action = Assert.Single(actions);

        var operations = await action.GetOperationsAsync(TestContext.Current.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id);
        Assert.NotNull(changed);
        return (await changed.GetTextAsync()).ToString();
    }

    private static async Task<ImmutableArray<CodeAction>> GetCodeFixActionsAsync(string source)
    {
        var (_, _, actions) = await RegisterCodeFixActionsAsync(source);
        return actions.ToImmutableArray();
    }

    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics, List<CodeAction> Actions)> RegisterCodeFixActionsAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .AddMetadataReferences(CreateReferences());
        var document = project.AddDocument("Test.cs", source);

        AssertCompiles(source);
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        var target = diagnostics.Single(d => d.Id == "FA0004");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document, target, (a, _) => actions.Add(a), TestContext.Current.CancellationToken);
        await new DllImportCodeFixProvider().RegisterCodeFixesAsync(context);
        return (document, diagnostics, actions);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "TestProject",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            CreateReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

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

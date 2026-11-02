using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace FlexibleArray.Analyzer;

/// <summary>
/// Rewrites a [DllImport] import that declares flexible array types into the source-generated
/// [LibraryImport] form: the attribute is renamed, and the partial modifiers it requires are
/// added to the method and its containing type. Offered only when every attribute argument
/// carries over unchanged (EntryPoint, SetLastError).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DllImportCodeFixProvider)), Shared]
public sealed class DllImportCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableHashSet<string> PortableArguments =
        ImmutableHashSet.Create("EntryPoint", "SetLastError");

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("FA0004");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } declaration
                || declaration.AttributeLists.Count == 0
                || declaration.AttributeLists.SelectMany(l => l.Attributes)
                    .FirstOrDefault(a => IsDllImport(a, model)) is not { } attribute
                || !ArgumentsCarryOver(attribute))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use LibraryImport",
                    cancellationToken => RewriteAsync(context.Document, declaration, attribute, cancellationToken),
                    equivalenceKey: "UseLibraryImport"),
                diagnostic);
        }
    }

    private static bool IsDllImport(AttributeSyntax attribute, SemanticModel model)
    {
        // The syntactic name is authoritative here: the fixer runs on a document that may not
        // have a fully bound compilation, and the analyzer's own diagnostic already
        // established that this attribute is DllImport.
        var name = attribute.Name.ToString();
        return name.EndsWith("DllImport", StringComparison.Ordinal)
            || name.EndsWith("DllImportAttribute", StringComparison.Ordinal);
    }

    private static bool ArgumentsCarryOver(AttributeSyntax attribute)
    {
        foreach (var argument in attribute.ArgumentList!.Arguments)
        {
            // A positional argument is the library name, which LibraryImport takes in the
            // same position; named arguments must be ones both attributes share.
            if (argument.NameEquals is null)
            {
                continue;
            }

            if (!PortableArguments.Contains(argument.NameEquals.Name.Identifier.ValueText))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<Document> RewriteAsync(
        Document document,
        MethodDeclarationSyntax declaration,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        editor.ReplaceNode(
            attribute.Name,
            SyntaxFactory.ParseTypeName("global::System.Runtime.InteropServices.LibraryImport").WithTriviaFrom(attribute.Name));

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            editor.ReplaceNode(declaration, (node, _) =>
            {
                var method = (MethodDeclarationSyntax)node;
                return method.WithModifiers(method.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
            });
        }

        if (declaration.Parent is BaseTypeDeclarationSyntax { } type
            && !type.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            editor.ReplaceNode(type, (node, _) =>
            {
                var declarationType = (BaseTypeDeclarationSyntax)node;
                return declarationType.WithModifiers(declarationType.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
            });
        }

        return editor.GetChangedDocument();
    }
}

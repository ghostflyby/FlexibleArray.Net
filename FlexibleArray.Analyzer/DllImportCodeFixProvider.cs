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
/// carries over unchanged (EntryPoint, SetLastError) and the signature needs no explicit
/// marshalling that this fix cannot supply.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DllImportCodeFixProvider)), Shared]
public sealed class DllImportCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableHashSet<string> PortableArguments =
        ImmutableHashSet.Create("EntryPoint", "SetLastError");

    private const string DllImportAttributeName = "System.Runtime.InteropServices.DllImportAttribute";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [DiagnosticId];

    private const string DiagnosticId = "FA0004";

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
                || !ArgumentsCarryOver(attribute)
                || model.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method
                || !SignatureCarriesOver(method))
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
        // Binding resolves aliases and user attributes that merely share the suffix.
        return model.GetSymbolInfo(attribute).Symbol is IMethodSymbol
               {
                   ContainingType: { } attributeType
               }
               && attributeType.ToDisplayString() == DllImportAttributeName;
    }

    // The source generator refuses these without an explicit StringMarshalling/MarshalAs that
    // this fix cannot supply, so the rewrite would turn compiling code into a generator error.
    private static bool SignatureCarriesOver(IMethodSymbol method)
        => !NeedsExplicitMarshalling(method.ReturnType)
           && !method.Parameters.Any(parameter => NeedsExplicitMarshalling(parameter.Type));

    private static bool NeedsExplicitMarshalling(ITypeSymbol type) => NeedsExplicitMarshalling(type, []);

    private static bool NeedsExplicitMarshalling(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (type.SpecialType is SpecialType.System_String
            or SpecialType.System_Char
            or SpecialType.System_Boolean
            or SpecialType.System_Object
            or SpecialType.System_Decimal)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return NeedsExplicitMarshalling(array.ElementType, visited);
        }

        // Only fields of structs are inlined into the signature; a reference type never is.
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named || !visited.Add(named))
        {
            return false;
        }

        return named.GetMembers().OfType<IFieldSymbol>()
            .Any(field => !field.IsStatic && NeedsExplicitMarshalling(field.Type, visited));
    }

    private static bool ArgumentsCarryOver(AttributeSyntax attribute)
    {
        // A bare attribute binds no constructor and keeps its missing dll name after the
        // rewrite, so there is nothing to offer.
        if (attribute.ArgumentList is not { } argumentList)
        {
            return false;
        }

        foreach (var argument in argumentList.Arguments)
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
                // A partial method may not be extern: the source generator supplies the
                // implementation the extern modifier stood for.
                var modifiers = method.Modifiers.Where(token => !token.IsKind(SyntaxKind.ExternKeyword))
                    .Append(PartialToken());
                return method.WithModifiers(SyntaxFactory.TokenList(modifiers));
            });
        }

        // Every type in the nesting chain must be partial, or the generated implementation
        // declaration has no home.
        foreach (var typeDeclaration in declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>())
        {
            if (typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                continue;
            }

            editor.ReplaceNode(typeDeclaration, (node, _) =>
            {
                var declarationType = (BaseTypeDeclarationSyntax)node;
                return declarationType.WithModifiers(declarationType.Modifiers.Add(PartialToken()));
            });
        }

        return editor.GetChangedDocument();
    }

    // The trailing space keeps the following return type or keyword separated.
    private static SyntaxToken PartialToken()
        => SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space);
}

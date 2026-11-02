using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FlexibleArray.Analyzer;

/// <summary>
/// Checks flexible array header declarations and their usage sites against what the type
/// system cannot express. Every check is a fact closed within C#; nothing assumes a C-side
/// correspondence.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FlexibleArrayAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "FlexibleArray";

    private static readonly DiagnosticDescriptor GetFirstElementMustReferenceHeaderField = new(
        "FA0001",
        "GetFirstElement must return a reference to an instance field of the header type",
        "GetFirstElement must return 'ref' to an instance field of '{0}' typed '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor PlaceholderMustBeLastField = new(
        "FA0002",
        "The first-element placeholder must be the last instance field",
        "The field '{0}' returned by GetFirstElement must be the last declared instance field: under sequential layout the tail starts at the placeholder, so a later field would be overlapped",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AutoLayoutIsNotCCompatible = new(
        "FA0003",
        "LayoutKind.Auto produces no C-compatible layout",
        "LayoutKind.Auto lets the runtime reorder fields; declare Sequential or Explicit",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DllImportIgnoresMarshallers = new(
        "FA0004",
        "Runtime-based DllImport ignores the flexible array marshallers",
        "DllImport uses runtime marshalling and silently ignores custom marshallers; declare the import with [LibraryImport] so the flexible array types marshal by pointer",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor WalkLengthCannotImplementWriteBack = new(
        "FA0005",
        "A walk-derived tail length cannot implement the write-back contract",
        "GetTailElementCount reads the tail through GetFirstElement, so the length depends on tail content; a header like that must not implement IConstructibleFlexibleArrayHeader",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private const string HeaderContractName = "IFlexibleArrayHeader`2";
    private const string ConstructibleContractName = "IConstructibleFlexibleArrayHeader`2";
    private const string ContractNamespace = "FlexibleArray";

    private static readonly ImmutableHashSet<string> FlexibleTypeNames =
        ImmutableHashSet.Create("FlexibleSpan`2", "MutableFlexibleSpan`2", "FlexibleArray`2", "MutableFlexibleArray`2");

    /// <summary>Gets the descriptor for the DllImport diagnostic, for tooling that offers its fix.</summary>
    internal static DiagnosticDescriptor DllImportDescriptor => DllImportIgnoresMarshallers;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            GetFirstElementMustReferenceHeaderField,
            PlaceholderMustBeLastField,
            AutoLayoutIsNotCCompatible,
            DllImportIgnoresMarshallers,
            WalkLengthCannotImplementWriteBack);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeHeaderDeclaration, SymbolKind.NamedType);
        context.RegisterSymbolAction(AnalyzeImport, SymbolKind.Method);
        context.RegisterOperationBlockStartAction(AnalyzeContractBody);
    }

    private static void AnalyzeHeaderDeclaration(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Struct || FindInterface(type, HeaderContractName) is null)
        {
            return;
        }

        var layout = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (LayoutKindOf(layout) is int kind && kind == (int)LayoutKind.Auto)
        {
            context.ReportDiagnostic(Diagnostic.Create(AutoLayoutIsNotCCompatible, type.Locations[0]));
        }
    }

    // MetadataName carries no namespace, so the contract namespace is checked separately.
    private static INamedTypeSymbol? FindInterface(INamedTypeSymbol type, string metadataName)
        => type.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition.MetadataName == metadataName
            && i.OriginalDefinition.ContainingNamespace.ToDisplayString() == ContractNamespace);

    private static void AnalyzeContractBody(OperationBlockStartAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method
            || method.ContainingType.TypeKind != TypeKind.Struct
            || FindInterface(method.ContainingType, HeaderContractName) is not { } contract)
        {
            return;
        }

        var type = method.ContainingType;
        var element = contract.TypeArguments[1];

        if (method.Name == "GetFirstElement")
        {
            context.RegisterOperationAction(
                operationContext => AnalyzeFirstElementReturn(operationContext, method, type, element),
                OperationKind.Return);
        }
        else if (method.Name == "GetTailElementCount"
            && FindInterface(type, ConstructibleContractName) is not null)
        {
            context.RegisterOperationAction(
                operationContext =>
                {
                    if (operationContext.Operation is IInvocationOperation { TargetMethod.Name: "GetFirstElement" })
                    {
                        operationContext.ReportDiagnostic(Diagnostic.Create(
                            WalkLengthCannotImplementWriteBack, method.Locations[0]));
                    }
                },
                OperationKind.Invocation);
        }
    }

    private static void AnalyzeFirstElementReturn(
        OperationAnalysisContext context,
        IMethodSymbol method,
        INamedTypeSymbol type,
        ITypeSymbol element)
    {
        if (context.Operation is not IReturnOperation { ReturnedValue: { } returned })
        {
            return;
        }

        if (returned is not IFieldReferenceOperation field
            || !SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, type)
            || field.Field.IsStatic
            || !SymbolEqualityComparer.Default.Equals(field.Field.Type, element))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GetFirstElementMustReferenceHeaderField,
                method.Locations[0],
                type.ToDisplayString(),
                element.ToDisplayString()));
            return;
        }

        if (LayoutKindOf(StructLayoutAttribute(type)) is int layout && layout == (int)LayoutKind.Explicit)
        {
            return;
        }

        var lastField = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).LastOrDefault();
        if (lastField is not null && !SymbolEqualityComparer.Default.Equals(lastField, field.Field))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PlaceholderMustBeLastField,
                field.Field.Locations[0],
                field.Field.Name));
        }
    }

    private static AttributeData? StructLayoutAttribute(INamedTypeSymbol type)
        => type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");

    private static int? LayoutKindOf(AttributeData? attribute)
    {
        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var value = attribute.ConstructorArguments[0].Value;
        return value is int raw ? raw : (int)(LayoutKind)value!;
    }

    private static void AnalyzeImport(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        var usesDllImport = method.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.DllImportAttribute");
        if (!usesDllImport)
        {
            return;
        }

        if (SignatureTouchesFlexibleType(method.ReturnType)
            || method.Parameters.Any(p => SignatureTouchesFlexibleType(p.Type)))
        {
            context.ReportDiagnostic(Diagnostic.Create(DllImportIgnoresMarshallers, method.Locations[0]));
        }
    }

    private static bool SignatureTouchesFlexibleType(ITypeSymbol? type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return SignatureTouchesFlexibleType(array.ElementType);
            case INamedTypeSymbol named:
                if (FlexibleTypeNames.Contains(named.MetadataName)
                    && named.ContainingNamespace.ToDisplayString() == "FlexibleArray")
                {
                    return true;
                }

                return named.TypeArguments.Any(argument => SignatureTouchesFlexibleType(argument));
            default:
                return false;
        }
    }
}

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
        "The field '{0}' returned by GetFirstElement must be the last instance field: the tail starts at that field, so any field at or after it would be overlapped",
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

        if (LayoutKindOf(StructLayoutAttribute(type)) is int kind && kind == (int)LayoutKind.Auto)
        {
            context.ReportDiagnostic(Diagnostic.Create(AutoLayoutIsNotCCompatible, type.Locations[0]));
        }
    }

    // MetadataName carries no namespace, so the contract namespace is checked separately. A
    // type that implements the contract at two instantiations is resolved through its own
    // self argument, which fixes which tail element type the checks compare against.
    private static INamedTypeSymbol? FindInterface(INamedTypeSymbol type, string metadataName)
    {
        var candidates = type.AllInterfaces.Where(i => Matches(i, metadataName)).ToImmutableArray();
        return candidates.FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type))
            ?? candidates.FirstOrDefault();
    }

    private static bool Matches(INamedTypeSymbol interfaceType, string metadataName)
        => interfaceType.OriginalDefinition.MetadataName == metadataName
            && interfaceType.OriginalDefinition.ContainingNamespace.ToDisplayString() == ContractNamespace;

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

        if (method.Name == "GetFirstElement"
            && ContractImplementation(type, contract, "GetFirstElement") is { } placeholderMember
            && SymbolEqualityComparer.Default.Equals(placeholderMember, method))
        {
            context.RegisterOperationAction(
                operationContext => AnalyzeFirstElementReturn(operationContext, method, type, element),
                OperationKind.Return);
        }
        else if (method.Name == "GetTailElementCount"
            && FindInterface(type, ConstructibleContractName) is not null
            && ContractImplementation(type, contract, "GetTailElementCount") is { } countMember
            && SymbolEqualityComparer.Default.Equals(countMember, method))
        {
            // Only this type's own placeholder member counts: another header's same-named
            // method is unrelated, and reporting it would reject valid composing headers.
            var ownGetFirstElement = ContractImplementation(type, contract, "GetFirstElement");
            var reported = false;

            context.RegisterOperationAction(
                operationContext =>
                {
                    if (reported
                        || operationContext.Operation is not IInvocationOperation invocation
                        || !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, ownGetFirstElement))
                    {
                        return;
                    }

                    reported = true;
                    operationContext.ReportDiagnostic(Diagnostic.Create(
                        WalkLengthCannotImplementWriteBack, method.Locations[0]));
                },
                OperationKind.Invocation);
        }
    }

    private static IMethodSymbol? ContractImplementation(INamedTypeSymbol type, INamedTypeSymbol contract, string memberName)
        => contract.GetMembers(memberName).OfType<IMethodSymbol>().FirstOrDefault() is { } member
            ? type.FindImplementationForInterfaceMember(member) as IMethodSymbol
            : null;

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
            || !SymbolEqualityComparer.Default.Equals(field.Field.Type, element)
            || IsStaticStorage(field.Instance))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GetFirstElementMustReferenceHeaderField,
                method.Locations[0],
                type.ToDisplayString(),
                element.ToDisplayString()));
            return;
        }

        var layout = StructLayoutAttribute(type);
        switch (LayoutKindOf(layout))
        {
            case (int)LayoutKind.Auto:
                // Reordering makes the declared order meaningless; FA0003 reports the layout.
                return;
            case (int)LayoutKind.Explicit:
                if (OverlappingExplicitField(type, field.Field) is not null)
                {
                    ReportPlaceholderNotLast(context, field.Field);
                }

                return;
            default:
                var lastField = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).LastOrDefault();
                if (lastField is not null && !SymbolEqualityComparer.Default.Equals(lastField, field.Field))
                {
                    ReportPlaceholderNotLast(context, field.Field);
                }

                return;
        }
    }

    // A receiver that is itself a static field points into shared storage, not the caller's
    // header, so the measured tail offset would describe a different location.
    private static bool IsStaticStorage(IOperation? instance)
        => instance is IFieldReferenceOperation { Field.IsStatic: true };

    private static void ReportPlaceholderNotLast(OperationAnalysisContext context, IFieldSymbol placeholder)
        => context.ReportDiagnostic(Diagnostic.Create(
            PlaceholderMustBeLastField, placeholder.Locations[0], placeholder.Name));

    // The tail begins at the placeholder's offset, so an instance field at or above that
    // offset is overwritten by the first tail elements.
    private static IFieldSymbol? OverlappingExplicitField(INamedTypeSymbol type, IFieldSymbol placeholder)
    {
        if (FieldOffsetOf(placeholder) is not int placeholderOffset)
        {
            return null;
        }

        return type.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !SymbolEqualityComparer.Default.Equals(f, placeholder))
            .FirstOrDefault(f => FieldOffsetOf(f) is int offset && offset >= placeholderOffset);
    }

    private static int? FieldOffsetOf(IFieldSymbol field)
        => field.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is int offset
                ? offset
                : (int?)null)
            .FirstOrDefault();

    private static AttributeData? StructLayoutAttribute(INamedTypeSymbol type)
        => type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");

    private static int? LayoutKindOf(AttributeData? attribute)
        => attribute?.ConstructorArguments.FirstOrDefault().Value switch
        {
            int raw => raw,
            LayoutKind kind => (int)kind,
            _ => null,
        };

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

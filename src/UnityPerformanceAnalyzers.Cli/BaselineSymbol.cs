using Microsoft.CodeAnalysis;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// The type and member a diagnostic sits in. These narrow the window in which an equal
/// exchange — one baselined violation fixed, an identical one added — goes unnoticed, from
/// a whole file down to one member.
/// </summary>
internal readonly record struct EnclosingSymbol(string Type, string Member)
{
    public static readonly EnclosingSymbol None = new(string.Empty, string.Empty);
}

/// <summary>Derives <see cref="EnclosingSymbol"/> from the semantic model.</summary>
internal static class BaselineSymbol
{
    /// <summary>
    /// Where <paramref name="location"/> lives, as two independent fields. They stay separate
    /// so the pair is reversible: joined by a dot, nested type <c>Outer.Node</c> member
    /// <c>Tick</c> and type <c>Outer</c> explicitly implementing <c>Node.Tick</c> would read
    /// the same and pool their suppression quotas.
    /// </summary>
    public static EnclosingSymbol Of(Compilation compilation, Location location)
    {
        var tree = location.SourceTree;
        if (tree is null)
        {
            return EnclosingSymbol.None;
        }

        var symbol = compilation.GetSemanticModel(tree)
            .GetEnclosingSymbol(location.SourceSpan.Start);

        // A lambda or local function is not a member anyone renames deliberately, and its
        // synthesized name would change under edits that leave the source meaning alone.
        while (symbol is IMethodSymbol { MethodKind: MethodKind.LambdaMethod
                   or MethodKind.LocalFunction
                   or MethodKind.AnonymousFunction })
        {
            symbol = symbol.ContainingSymbol;
        }

        // An accessor reports as get_Health / set_Health / get_Item, which would both diverge
        // from the frozen member spelling and split one property's quota in two.
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associated })
        {
            symbol = associated;
        }

        // Top-level statements land on the synthesized entry point rather than on nothing,
        // which is why the symbol is taken from the model instead of from syntax: an empty
        // pair would pool every violation in such a file into one bucket.
        return symbol switch
        {
            null => EnclosingSymbol.None,
            INamedTypeSymbol type => new EnclosingSymbol(TypeChain(type), string.Empty),
            { ContainingType: { } containing } => new EnclosingSymbol(
                TypeChain(containing),
                MemberName(symbol)),
            _ => EnclosingSymbol.None,
        };
    }

    /// <summary>
    /// The containing types outermost first. Metadata names carry generic arity, so
    /// <c>C</c> and <c>C&lt;T&gt;</c> in one file are different types; namespaces are left
    /// out, because renaming one should not void a baseline.
    /// </summary>
    private static string TypeChain(INamedTypeSymbol type)
    {
        var names = new List<string>();
        for (INamedTypeSymbol? current = type; current is object; current = current.ContainingType)
        {
            names.Add(current.MetadataName);
        }

        names.Reverse();
        return string.Join(".", names);
    }

    /// <summary>
    /// The member name. Metadata spelling gives the stable forms for the members that have no
    /// source name of their own — <c>.ctor</c>, <c>.cctor</c>, <c>Finalize</c>,
    /// <c>op_Addition</c>, <c>Item</c>.
    /// </summary>
    private static string MemberName(ISymbol symbol)
    {
        var explicitlyImplemented = ExplicitInterfaceMember(symbol);
        if (explicitlyImplemented is object)
        {
            // The interface's simple name only. Two same-named interfaces from different
            // namespaces therefore share a key - an accepted collision, of a piece with the
            // signature and namespace omissions elsewhere in this key.
            return $"{explicitlyImplemented.ContainingType.Name}.{explicitlyImplemented.MetadataName}";
        }

        return symbol.MetadataName;
    }

    private static ISymbol? ExplicitInterfaceMember(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.ExplicitInterfaceImplementations.FirstOrDefault(),
        IPropertySymbol property => property.ExplicitInterfaceImplementations.FirstOrDefault(),
        IEventSymbol @event => @event.ExplicitInterfaceImplementations.FirstOrDefault(),
        _ => null,
    };
}

using System.Collections.Immutable;

namespace EventSourceExtensions
{
    internal interface ITypeMapping
    {
        ImmutableList<NamedExpression> Expressions { get; }

        ITypeMapping WithIndex(ref int index);
    }
}
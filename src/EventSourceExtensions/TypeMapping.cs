using System;
using System.Collections.Immutable;
using System.Linq;

namespace EventSourceExtensions
{
    public sealed class TypeMapping<T> : ITypeMapping
    {
        private readonly ImmutableList<NamedExpression> _expressions;

        ImmutableList<NamedExpression> ITypeMapping.Expressions => _expressions;

        ITypeMapping ITypeMapping.WithIndex(ref int index)
        {
            var localIndex = index;
            var mapping = new TypeMapping<T>(_expressions.Select(x => x.WithIndex(localIndex++)).ToImmutableList());
            index = localIndex;
            return mapping;
        }

        internal static TypeMapping<T> Empty { get; } =
            new TypeMapping<T>(ImmutableList<NamedExpression>.Empty);

        private TypeMapping(ImmutableList<NamedExpression> expressions)
        {
            _expressions = expressions;
        }

        public TypeMapping<T> Map<TValue>(Func<T, TValue> converter, string name = null)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            EventSourceHelpers.ValidateType(typeof(TValue));
            if (!_expressions.IsEmpty && (string.IsNullOrEmpty(_expressions[0].Name) || string.IsNullOrEmpty(name)))
            {
                throw new ArgumentException("Names cannot be empty when adding multiple mappings per type", nameof(name));
            }
            
            return new TypeMapping<T>(_expressions.Add(new NamedExpression(converter, name)));
        }
    }
}
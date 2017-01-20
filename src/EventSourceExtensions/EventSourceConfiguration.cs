using System;
using System.Collections.Immutable;
using System.Linq;

namespace EventSourceExtensions
{
    public sealed class EventSourceConfiguration
    {
        private readonly ImmutableDictionary<Type, ITypeMapping> _mappings;
        private readonly ImmutableList<NamedExpression> _parameters;

        public static EventSourceConfiguration Empty { get; } =
            new EventSourceConfiguration(ImmutableDictionary<Type, ITypeMapping>.Empty, ImmutableList<NamedExpression>.Empty);

        private EventSourceConfiguration(ImmutableDictionary<Type, ITypeMapping> mappings, ImmutableList<NamedExpression> parameters)
        {
            _mappings = mappings;
            _parameters = parameters;
        }

        public EventSourceConfiguration WithMapping<T>(Func<TypeMapping<T>, TypeMapping<T>> mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            return new EventSourceConfiguration(_mappings.SetItem(typeof(T), mapping(TypeMapping<T>.Empty)), _parameters);
        }

        public EventSourceConfiguration WithAdditionalParameter<T>(Func<T> valueFactory, string name)
        {
            if (valueFactory == null) throw new ArgumentNullException(nameof(valueFactory));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name == string.Empty) throw new ArgumentException("Name cannot be empty", nameof(name));

            return new EventSourceConfiguration(_mappings, _parameters.Add(new NamedExpression(valueFactory, name)));
        }

        public EventSourceConfiguration MergeWith(EventSourceConfiguration other)
        {
            return new EventSourceConfiguration(_mappings.SetItems(other._mappings), _parameters.AddRange(other._parameters));
        }

        internal EventSourceConfiguration Index()
        {
            // we index all the functions so they could be easily accessible
            // using an array index in the generated EventSource class

            var index = 0;
            return new EventSourceConfiguration(
                _mappings.ToImmutableDictionary(x => x.Key, x => x.Value.WithIndex(ref index)),
                _parameters.Select(x => x.WithIndex(index++)).ToImmutableList());
        }

        internal ImmutableList<NamedExpression> GetMappings(Type type)
        {
            return !_mappings.TryGetValue(type, out var mapping) ? ImmutableList<NamedExpression>.Empty : mapping.Expressions;
        }

        internal ImmutableList<NamedExpression> GetParameters()
        {
            return _parameters;
        }

        internal Delegate[] ToDelegateArray()
        {
            return _mappings.SelectMany(x => x.Value.Expressions)
                .Concat(_parameters)
                .OrderBy(x => x.Index)
                .Select(x => x.Func)
                .ToArray();
        }
    }
}
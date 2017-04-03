using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace EventSourceExtensions
{
    public sealed class EventSourceGenerator
    {
        private readonly Func<object, string> _fallbackConverter;
        private readonly ConcurrentDictionary<Type, Lazy<EventSource>> _instances;
        private readonly EventSourceTypeGenerator _typeGenerator;
        private readonly Delegate[] _configurationDelegates;
        private readonly EventSourceConfiguration _configuration;

        public EventSourceGenerator(EventSourceConfiguration configuration = null,
            Func<object, string> fallbackConverter = null,
            bool generateAutomaticEventIds = false)
            : this(configuration, fallbackConverter, generateAutomaticEventIds, false)
        {
        }

        internal EventSourceGenerator(EventSourceConfiguration configuration = null, Func<object, string> fallbackConverter = null, bool generateAutomaticEventIds = false, bool saveDebugAssembly = false)
        {
            _fallbackConverter = fallbackConverter;
            _instances = new ConcurrentDictionary<Type, Lazy<EventSource>>();
            _configuration = (configuration ?? EventSourceConfiguration.Empty).Index();
            _configurationDelegates = _configuration?.ToDelegateArray();
            _typeGenerator = new EventSourceTypeGenerator(_configuration, generateAutomaticEventIds, saveDebugAssembly);
        }

#if NET46
        internal void SaveAssembly()
        {
            _typeGenerator.SaveAssembly();
        }
#endif

        public T Get<T>(EventSourceConfiguration configurationOverride = null)
        {
            return (T)(object)Get(typeof(T), configurationOverride);
        }

        public EventSource Get(Type interfaceType, EventSourceConfiguration configurationOverride = null)
        {
            return _instances.GetOrAdd(interfaceType, _ => new Lazy<EventSource>(() =>
            {
                EventSourceConfiguration configuration;
                Delegate[] configurationDelegates;
                if (configurationOverride != null)
                {
                    configuration = _configuration.MergeWith(configurationOverride).Index();
                    configurationDelegates = configuration.ToDelegateArray();
                }
                else
                {
                    configuration = _configuration;
                    configurationDelegates = _configurationDelegates;
                }

                var generatedType = _typeGenerator.GenerateType(interfaceType, configuration);
                return (EventSource)Activator.CreateInstance(generatedType, _fallbackConverter, configurationDelegates);
            })).Value;
        }
    }
}
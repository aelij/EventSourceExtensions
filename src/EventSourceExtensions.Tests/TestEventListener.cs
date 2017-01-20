using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace EventSourceExtensions.Tests
{
    internal class TestEventListener : EventListener
    {
        private readonly List<EventWrittenEventArgs> _events = new List<EventWrittenEventArgs>();

        public IReadOnlyList<EventWrittenEventArgs> Events => _events.AsReadOnly();

        public IReadOnlyList<object> LastPayload => _events.LastOrDefault()?.Payload;

        public void Clear()
        {
            _events.Clear();
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            _events.Add(eventData);
        }

        public void EnableEvents(EventSource eventSource)
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
    }
}
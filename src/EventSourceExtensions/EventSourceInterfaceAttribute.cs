using System;

namespace EventSourceExtensions
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class EventSourceInterfaceAttribute : Attribute
    {
        public string Name { get; set; }
    }
}
using System;
using System.Reflection;

namespace EventSourceExtensions
{
    internal static class EventSourceHelpers
    {
        public static bool IsValidType(Type type)
        {
            if (type == typeof(bool) ||
                type == typeof(byte) ||
                type == typeof(char) ||
                type == typeof(ushort) ||
                type == typeof(uint) ||
                type == typeof(ulong) ||
                type == typeof(sbyte) ||
                type == typeof(short) ||
                type == typeof(int) ||
                type == typeof(long) ||
                type == typeof(string) ||
                type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(DateTime))
                return true;

            if (type.GetTypeInfo().IsEnum)
                return true;

            if (type == typeof(Guid) || type == typeof(IntPtr))
                return true;

            if ((type.IsArray || type.IsPointer) && type.GetElementType() == typeof(byte))
                return true;

            return false;
        }

        public static void ValidateType(Type type)
        {
            if (!IsValidType(type))
            {
                throw new InvalidOperationException($"Type incompatible with EventSource ({type})");
            }
        }
    }
}
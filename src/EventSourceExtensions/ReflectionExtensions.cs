using System;
using System.Linq;
using System.Reflection;

namespace EventSourceExtensions
{
    internal static class ReflectionExtensions
    {
        public static MethodInfo GetMethod(this Type type, string name, bool isStatic, params Type[] parameterTypes)
        {
            return type.GetTypeInfo().GetMethod(name, isStatic, parameterTypes);
        }

        public static MethodInfo GetMethod(this TypeInfo type, string name, bool isStatic, params Type[] parameterTypes)
        {
            return type.DeclaredMethods.FirstOrDefault(m =>
                ((isStatic && m.IsStatic) || (!isStatic && !m.IsStatic)) &&
                string.Equals(m.Name, name, StringComparison.Ordinal) &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes));
        }

        public static ConstructorInfo GetConstructor(this Type type, bool isStatic, params Type[] parameterTypes)
        {
            return type.GetTypeInfo().GetConstructor(isStatic, parameterTypes);
        }

        public static ConstructorInfo GetConstructor(this TypeInfo type, bool isStatic, params Type[] parameterTypes)
        {
            return type.DeclaredConstructors.FirstOrDefault(m =>
                ((isStatic && m.IsStatic) || (!isStatic && !m.IsStatic)) &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes));
        }
    }
}
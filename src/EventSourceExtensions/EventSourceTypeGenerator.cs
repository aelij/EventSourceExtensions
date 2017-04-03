using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace EventSourceExtensions
{
    internal sealed class EventSourceTypeGenerator
    {
        private const string AssemblyName = "GeneratedEventSources";

        private static readonly MethodInfo WriteEventObjectArrayMethodInfo = typeof(EventSource).GetMethod("WriteEvent", false, typeof(int), typeof(object[]));
        private static readonly MethodInfo WriteEventMethodInfo = typeof(EventSource).GetMethod("WriteEvent", false, typeof(int));
        private static readonly MethodInfo IsEnabledMethodInfo = typeof(EventSource).GetMethod("IsEnabled", false, typeof(EventLevel), typeof(EventKeywords));

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly AssemblyBuilder _assembly;
        private readonly ModuleBuilder _module;
        private readonly ConcurrentDictionary<Type, Type> _typeCache;
        private readonly EventSourceConfiguration _configuration;
        // ReSharper disable once NotAccessedField.Local
        private readonly bool _generateAutomaticEventIds;

        private readonly bool _saveDebugAssembly;

        public EventSourceTypeGenerator(EventSourceConfiguration configuration = null, bool generateAutomaticEventIds = false, bool saveDebugAssembly = false)
        {
            _generateAutomaticEventIds = generateAutomaticEventIds;
            _saveDebugAssembly = saveDebugAssembly;
            _configuration = configuration ?? EventSourceConfiguration.Empty;
            const string moduleName = AssemblyName + ".dll";
#if NET46
            _assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), saveDebugAssembly ? AssemblyBuilderAccess.RunAndSave : AssemblyBuilderAccess.Run);
            _module = saveDebugAssembly ? _assembly.DefineDynamicModule(moduleName, moduleName) : _assembly.DefineDynamicModule(moduleName);
#else
            _assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), AssemblyBuilderAccess.Run);
            _module = _assembly.DefineDynamicModule(moduleName);
#endif
            _typeCache = new ConcurrentDictionary<Type, Type>();
        }

#if NET46
        public void SaveAssembly()
        {
            if (_saveDebugAssembly)
            {
                _assembly.Save(_module.ScopeName);
            }
        }
#endif

        public Type GenerateType(Type interfaceType, EventSourceConfiguration configurationOverride = null)
        {
            return _typeCache.GetOrAdd(interfaceType, _ => new TypeGenerator(this, interfaceType, configurationOverride, _generateAutomaticEventIds).Generate());
        }

        private sealed class TypeGenerator
        {
            private const long EventKeywordsAll = -1;

            private static readonly Type[] EmptyTypes = new Type[0];
            private static readonly object[] EmptyArray = new object[0];

            private readonly EventSourceTypeGenerator _generator;
            private readonly Type _interfaceType;
            private readonly bool _generateAutomaticEventIds;
            private readonly EventSourceConfiguration _configuration;

            private TypeBuilder _type;
            private FieldBuilder _convertersField;
            private FieldBuilder _fallbackConverterField;
            private MethodBuilder _fallbackConverterGetter;

            public TypeGenerator(EventSourceTypeGenerator generator, Type interfaceType, EventSourceConfiguration configurationOverride, bool generateAutomaticEventIds)
            {
                _generator = generator;
                _interfaceType = interfaceType;
                _generateAutomaticEventIds = generateAutomaticEventIds;
                _configuration = configurationOverride ?? _generator._configuration;
            }

            public Type Generate()
            {
                var name = _interfaceType.Name;
                if (name.StartsWith("I", StringComparison.Ordinal)) name = name.Substring(1);

                _type = _generator._module.DefineType(_interfaceType.Namespace + "." + name, TypeAttributes.Public,
                    typeof(EventSource), new[] { _interfaceType });

                SetEventSourceAttribute();

                _convertersField = _type.DefineField("_converters", typeof(Delegate[]), FieldAttributes.Private | FieldAttributes.InitOnly);

                DefineFallbackConverter();

                GenerateConstructor();

                var ids = new HashSet<int>();
                var methodsToGenerate = new List<(MethodInfo method, EventAttribute attribute)>();

                foreach (var memberInfo in GetDeclaredMembers(_interfaceType))
                {
                    var methodInfo = memberInfo as MethodInfo;
                    if (methodInfo == null)
                    {
                        throw new InvalidOperationException($"Only methods can be defined ({memberInfo})");
                    }

                    if (methodInfo.ReturnType != typeof(void))
                    {
                        throw new InvalidOperationException($"Only void-returning methods are allowed ({methodInfo})");
                    }

                    var eventAttributeInstance = methodInfo.GetCustomAttribute<EventAttribute>();
                    if (eventAttributeInstance == null)
                    {
                        if (!_generateAutomaticEventIds)
                        {
                            throw new InvalidOperationException($"Missing Event attribute ({methodInfo})");
                        }
                    }
                    else if (!ids.Add(eventAttributeInstance.EventId))
                    {
                        throw new InvalidOperationException($"Duplicate event ID ({methodInfo})");
                    }

                    methodsToGenerate.Add((methodInfo, eventAttributeInstance));
                }

                var currentId = ids.Any() ? ids.Max() + 1 : 1;

                foreach (var method in methodsToGenerate)
                {
                    GenerateMethod(method.method, method.attribute ?? new EventAttribute(currentId++));
                }

                var generatedType = _type.CreateTypeInfo().AsType();

                return generatedType;
            }

            private static IEnumerable<MemberInfo> GetDeclaredMembers(Type type)
            {
                return type.GetTypeInfo().ImplementedInterfaces.Reverse()
                    .Except(new[] { typeof(IDisposable) })
                    .Concat(new[] { type })
                    .SelectMany(c => c.GetTypeInfo().DeclaredMembers);
            }

            private void DefineFallbackConverter()
            {
                _fallbackConverterField = _type.DefineField("_fallbackConverter", typeof(Func<object, string>),
                    FieldAttributes.Private | FieldAttributes.InitOnly);

                var converterProperty = _type.DefineProperty("FallbackConverter", PropertyAttributes.None,
                    _fallbackConverterField.FieldType, EmptyTypes);

                _fallbackConverterGetter = _type.DefineMethod("get_" + converterProperty.Name,
                    MethodAttributes.Private | MethodAttributes.SpecialName |
                    MethodAttributes.HideBySig, converterProperty.PropertyType, EmptyTypes);

                var il = _fallbackConverterGetter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _fallbackConverterField);
                var label = il.DefineLabel();
                il.Emit(OpCodes.Brtrue_S, label);
                il.Emit(OpCodes.Ldstr, "Fallback converter missing");
                // ReSharper disable once AssignNullToNotNullAttribute
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(false, typeof(string)));
                il.Emit(OpCodes.Throw);
                il.MarkLabel(label);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _fallbackConverterField);
                il.Emit(OpCodes.Ret);

                converterProperty.SetGetMethod(_fallbackConverterGetter);
            }

            private void GenerateConstructor()
            {
                var constructor = _type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
                    new[] { _fallbackConverterField.FieldType, _convertersField.FieldType });
                constructor.DefineParameter(1, ParameterAttributes.None, _fallbackConverterField.Name.TrimStart('_'));
                var il = constructor.GetILGenerator();

                il.Emit(OpCodes.Ldarg_0);
                // ReSharper disable once AssignNullToNotNullAttribute
                // ReSharper disable once PossibleNullReferenceException
                il.Emit(OpCodes.Call, _type.BaseType.GetConstructor(false, EmptyTypes));
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, _fallbackConverterField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stfld, _convertersField);
                il.Emit(OpCodes.Ret);
            }

            private void SetEventSourceAttribute()
            {
                var eventSourceAttribute = _interfaceType.GetTypeInfo().GetCustomAttribute<EventSourceInterfaceAttribute>();
                if (string.IsNullOrEmpty(eventSourceAttribute?.Name))
                {
                    _type.SetCustomAttribute(new CustomAttributeBuilder(
                        // ReSharper disable once AssignNullToNotNullAttribute
                        typeof(EventSourceAttribute).GetTypeInfo().GetConstructor(false, EmptyTypes),
                        EmptyArray));

                    return;
                }

                var name = eventSourceAttribute.Name;
                if (_interfaceType.GetTypeInfo().IsGenericType)
                {
                    name += "-" + string.Join("-", _interfaceType.GetTypeInfo().GenericTypeArguments.Select(x => x.Name));
                }

                _type.SetCustomAttribute(new CustomAttributeBuilder(
                    // ReSharper disable once AssignNullToNotNullAttribute
                    typeof(EventSourceAttribute).GetTypeInfo().GetConstructor(false, EmptyTypes), EmptyArray,
                    new[] { typeof(EventSourceAttribute).GetTypeInfo().GetDeclaredProperty(nameof(EventSourceAttribute.Name)) },
                    new object[] { name }));
            }

            private void GenerateMethod(MethodInfo sourceMethodInfo, EventAttribute eventAttributeInstance)
            {
                var parameterData = GetParameterData(sourceMethodInfo);
                var eventMethod = GenerateEventMethod(sourceMethodInfo, parameterData, eventAttributeInstance);
                GenerateInterfaceMethod(sourceMethodInfo, parameterData, eventMethod, eventAttributeInstance);
            }

            private List<ParameterData> GetParameterData(MethodInfo sourceMethodInfo)
            {
                var parameters = new List<ParameterData>();
                var addedParameters = new List<ParameterData>();
                foreach (var parameterInfo in sourceMethodInfo.GetParameters())
                {
                    var mappings = _configuration.GetMappings(parameterInfo.ParameterType);
                    if (mappings.IsEmpty)
                    {
                        parameters.Add(new ParameterData(parameterInfo, parameterInfo.Name, null, null));
                    }
                    else if (mappings.Count == 1) // in-place
                    {
                        parameters.Add(new ParameterData(parameterInfo, mappings[0].Name ?? parameterInfo.Name,
                            mappings[0].Index, mappings[0].Func));
                    }
                    else // at the end
                    {
                        addedParameters.AddRange(mappings.Select(mapper =>
                            new ParameterData(parameterInfo, mapper.Name, mapper.Index, mapper.Func)));
                    }
                }

                parameters.AddRange(addedParameters);
                parameters.AddRange(_configuration.GetParameters().Select(mapper =>
                    new ParameterData(null, mapper.Name, mapper.Index, mapper.Func)));

                return parameters;
            }

            private MethodBuilder GenerateEventMethod(MethodInfo sourceMethodInfo,
                List<ParameterData> parameterData, EventAttribute eventAttributeInstance)
            {
                var method = _type.DefineMethod(sourceMethodInfo.Name,
                    MethodAttributes.Private,
                    CallingConventions.HasThis, typeof(void), parameterData.Select(x => x.Type).ToArray());

                var position = 1;
                foreach (var parameterInfo in parameterData)
                {
                    method.DefineParameter(position++, ParameterAttributes.None, parameterInfo.Name);
                }

                var defaultAttribute = new EventAttribute(0);

                var attributeProperties =
                    (from property in typeof(EventAttribute).GetRuntimeProperties()
                     where property.CanRead && property.CanWrite
                     let value = property.GetValue(eventAttributeInstance)
                     where !Equals(value, property.GetValue(defaultAttribute))
                     select new { property, value }).ToArray();

                method.SetCustomAttribute(new CustomAttributeBuilder(
                    // ReSharper disable once AssignNullToNotNullAttribute
                    typeof(EventAttribute).GetTypeInfo().GetConstructor(false, typeof(int)),
                    new object[] { eventAttributeInstance.EventId },
                    attributeProperties.Select(x => x.property).ToArray(),
                    attributeProperties.Select(x => x.value).ToArray()));

                GenerateEventMethodBody(parameterData, method.GetILGenerator(), eventAttributeInstance);

                return method;
            }

            private void GenerateInterfaceMethod(MethodInfo sourceMethodInfo,
                List<ParameterData> parameterData, MethodBuilder eventMethod, EventAttribute eventAttribute)
            {
                var paramters = sourceMethodInfo.GetParameters().Select(x => x.ParameterType).ToArray();

                // ReSharper disable once PossibleNullReferenceException
                var methodName = GetTypeName(sourceMethodInfo.DeclaringType) + "." + sourceMethodInfo.Name;
                var method = _type.DefineMethod(methodName,
                    MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig |
                    MethodAttributes.NewSlot |
                    MethodAttributes.Virtual,
                    CallingConventions.HasThis, typeof(void), paramters);

                _type.DefineMethodOverride(method, sourceMethodInfo);

                var position = 1;
                foreach (var parameterInfo in sourceMethodInfo.GetParameters())
                {
                    method.DefineParameter(position++, ParameterAttributes.None, parameterInfo.Name);
                }

                SetNonEventAttribute(method);

                GenerateInterfaceMethodBody(parameterData, method.GetILGenerator(), eventMethod, eventAttribute);
            }

            private static string GetTypeName(Type type)
            {
                var name = type.Namespace + "." + type.Name;
                if (type.GetTypeInfo().IsGenericType)
                {
                    name = name.Substring(0, name.IndexOf('`')) +
                        "<" + string.Join(",", type.GetTypeInfo().GenericTypeArguments.Select(GetTypeName)) + ">";
                }
                return name;
            }

            private static void SetNonEventAttribute(MethodBuilder method)
            {
                method.SetCustomAttribute(new CustomAttributeBuilder(
                    // ReSharper disable once AssignNullToNotNullAttribute
                    typeof(NonEventAttribute).GetTypeInfo().GetConstructor(false, EmptyTypes),
                    EmptyArray));
            }

            private static void GenerateEventMethodBody(List<ParameterData> parameters, ILGenerator il,
                EventAttribute eventAttribute)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, eventAttribute.EventId);

                // WriteEvent(id, new object[] { p1, p2, ... })
                if (parameters.Count > 0)
                {
                    il.Emit(OpCodes.Ldc_I4, parameters.Count);
                    il.Emit(OpCodes.Newarr, typeof(object));

                    for (var i = 0; i < parameters.Count; ++i)
                    {
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Ldarg, i + 1);
                        if (parameters[i].Type.GetTypeInfo().IsValueType)
                        {
                            il.Emit(OpCodes.Box, parameters[i].Type);
                        }
                        il.Emit(OpCodes.Stelem, typeof(object));
                    }

                    il.Emit(OpCodes.Call, WriteEventObjectArrayMethodInfo);
                }
                // WriteEvent(id)
                else
                {
                    il.Emit(OpCodes.Call, WriteEventMethodInfo);
                }

                il.Emit(OpCodes.Ret);
            }

            private void GenerateInterfaceMethodBody(List<ParameterData> parameterData, ILGenerator il,
                MethodBuilder eventMethod, EventAttribute eventAttribute)
            {
                // if (!IsEnabled(level, keyword)) return
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, (int)eventAttribute.Level);
                il.Emit(OpCodes.Ldc_I8,
                    eventAttribute.Keywords == EventKeywords.None
                        ? EventKeywordsAll
                        : (long)eventAttribute.Keywords);
                il.Emit(OpCodes.Call, IsEnabledMethodInfo);
                var enabledLabel = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, enabledLabel);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(enabledLabel);

                il.Emit(OpCodes.Ldarg_0);
                foreach (var p in parameterData)
                {
                    // ((Func)_converters[index]).Invoke()
                    if (p.ConverterIndex != null)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, _convertersField);
                        il.Emit(OpCodes.Ldc_I4, p.ConverterIndex.Value);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Castclass, p.ConverterDelegate.GetType());
                        if (p.Parameter != null)
                        {
                            il.Emit(OpCodes.Ldarg, p.Parameter.Position + 1);
                        }
                        il.Emit(OpCodes.Callvirt, p.ConverterDelegate.GetType().GetRuntimeMethod("Invoke", p.Parameter != null ? new[] { p.Parameter.ParameterType } : EmptyTypes));
                    }
                    else
                    {
                        Debug.Assert(p.Parameter != null, "p.Parameter != null");
                        il.Emit(OpCodes.Ldarg, p.Parameter.Position + 1);
                    }
                    if (!p.IsValidType)
                    {
                        // FallbackConverter(value) ?? string.Empty
                        var local = il.DeclareLocal(p.OriginalType);
                        il.Emit(OpCodes.Stloc, local);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Call, _fallbackConverterGetter);
                        il.Emit(OpCodes.Ldloc, local);
                        if (local.LocalType.GetTypeInfo().IsValueType)
                        {
                            il.Emit(OpCodes.Box, local.LocalType);
                        }
                        il.Emit(OpCodes.Callvirt, _fallbackConverterGetter.ReturnType.GetRuntimeMethod("Invoke", new[] { typeof(object) }));
                        EmitStringNullCoalescing(il);
                    }
                    else if (p.Type == typeof(string))
                    {
                        EmitStringNullCoalescing(il);
                    }
                }
                il.Emit(OpCodes.Call, eventMethod);
                il.Emit(OpCodes.Ret);
            }

            private static void EmitStringNullCoalescing(ILGenerator il)
            {
                il.Emit(OpCodes.Dup);
                var label = il.DefineLabel();
                il.Emit(OpCodes.Brtrue_S, label);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, typeof(string).GetTypeInfo().GetDeclaredField(nameof(string.Empty)));
                il.MarkLabel(label);
            }
        }

        private struct ParameterData
        {
            public ParameterData(ParameterInfo parameter, string name, int? converterIndex, Delegate converterDelegate)
            {
                Parameter = parameter;
                Name = name;
                ConverterIndex = converterIndex;
                ConverterDelegate = converterDelegate;
                OriginalType = converterDelegate?.GetMethodInfo().ReturnType ?? Parameter.ParameterType;
                IsValidType = EventSourceHelpers.IsValidType(OriginalType);
                Type = IsValidType ? OriginalType : typeof(string);
            }

            public readonly ParameterInfo Parameter;
            public readonly string Name;
            public readonly int? ConverterIndex;
            public readonly Delegate ConverterDelegate;
            public readonly bool IsValidType;
            public readonly Type Type;
            public readonly Type OriginalType;
        }
    }
}
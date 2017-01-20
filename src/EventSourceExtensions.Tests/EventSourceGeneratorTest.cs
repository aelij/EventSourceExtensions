using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable IDE1006 // Naming Styles

namespace EventSourceExtensions.Tests
{
    [SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    [SuppressMessage("ReSharper", "ConsiderUsingConfigureAwait")]
    public class EventSourceGeneratorTest : IDisposable
    {
        private EventSourceGenerator _generator;

        [Theory]
        [MemberData(nameof(SimpleDataTypesParameters))]
        public async Task SupportSimpleDataTypes(Type type, object o, object expected)
        {
            _generator = CreateGenerator();
            var genericType = typeof(ITestLogData<>).MakeGenericType(type);
            using (var eventSource = _generator.Get(genericType))
            {
                var testMethod = genericType.GetTypeInfo().GetMethod("Test");
                testMethod.Invoke(eventSource, new[] { o });

                await eventSource.ListenAndAssertAsync(
                    () => testMethod.Invoke(eventSource, new[] { o }),
                    data =>
                    {
                        Assert.Equal(expected, data.PayloadValue(0));
                    });
            }
        }

        public static IEnumerable<object[]> SimpleDataTypesParameters
        {
            get
            {
                yield return new object[] { typeof(int), default(int), default(int) };
                yield return new object[] { typeof(string), default(string), string.Empty };
                yield return new object[] { typeof(string), string.Empty, string.Empty };
                yield return new object[] { typeof(string), "Test", "Test" };
                yield return new object[] { typeof(DayOfWeek), DayOfWeek.Sunday, (uint)DayOfWeek.Sunday };
                yield return new object[] { typeof(byte[]), new[] { (byte)1 }, new[] { (byte)1 } };
            }
        }

        [Fact]
        public void CustomDataNoFallback_Throws()
        {
            _generator = CreateGenerator();
            using (var eventSource = _generator.Get<ITestLogData<CustomData>>())
            using (var listener = new TestEventListener())
            {
                listener.EnableEvents((EventSource)eventSource);
                Assert.Throws<InvalidOperationException>(() => eventSource.Test(new CustomData()));
            }
        }

        [Fact]
        public async Task CustomDataToStringFallback()
        {
            _generator = CreateGenerator(useToStringFallback: true);
            using (var eventSource = _generator.Get<ITestLogData<CustomData>>())
            {
                await (eventSource as EventSource).ListenAndAssertAsync(
                    () => eventSource.Test(new CustomData()),
                    data => Assert.Equal(nameof(CustomData), data.PayloadValue(0)));
            }
        }

        [Fact]
        public async Task CustomDataToStringFallback_NullValueToEmptyString()
        {
            _generator = CreateGenerator(useToStringFallback: true);
            using (var eventSource = _generator.Get<ITestLogData<CustomData>>())
            {
                await (eventSource as EventSource).ListenAndAssertAsync(
                    () => eventSource.Test(null),
                    data => Assert.Equal(string.Empty, data.PayloadValue(0)));
            }
        }

        [Fact]
        public async Task CustomDataMapping_SingleAddedInPlace()
        {
            _generator = CreateGenerator(configuration: EventSourceConfiguration.Empty
                .WithMapping<CustomData>(m => m.Map(c => 1)));
            using (var eventSource = _generator.Get<ITestLogData<CustomData>>())
            {
                await (eventSource as EventSource).ListenAndAssertAsync(
                    () => eventSource.Test2(null, 0),
                    data => Assert.Equal(new object[] { 1, 0 }, data.AllValues()));
            }
        }

        [Fact]
        public void TypeMapping_DoubleMappingNoNameThrows()
        {
            Assert.Throws<ArgumentException>(() => TypeMapping<CustomData>.Empty.Map(c => 1).Map(c => 2));
        }

        [Fact]
        public async Task CustomDataMapping_DoubleAddedAtEnd()
        {
            _generator = CreateGenerator(configuration: EventSourceConfiguration.Empty
                .WithMapping<CustomData>(m => m.Map(c => 1, "a").Map(c => 2, "b")));
            using (var eventSource = _generator.Get<ITestLogData<CustomData>>())
            {
                await (eventSource as EventSource).ListenAndAssertAsync(
                    () => eventSource.Test2(null, 0),
                    data => Assert.Equal(new object[] { 0, 1, 2 }, data.AllValues()));
            }
        }

        [Fact]
        public async Task AdditionalParameter_AdddedAtEnd()
        {
            _generator = CreateGenerator(configuration: EventSourceConfiguration.Empty
                .WithAdditionalParameter(() => 1, "x"));
            using (var eventSource = _generator.Get<ITestLogData<int>>())
            {
                await (eventSource as EventSource).ListenAndAssertAsync(
                    () => eventSource.Test(0),
                    data => Assert.Equal(new object[] { 0, 1 }, data.AllValues()));
            }
        }

        private static EventSourceGenerator CreateGenerator(bool useToStringFallback = false, EventSourceConfiguration configuration = null)
        {
            return new EventSourceGenerator(
                configuration: configuration,
                fallbackConverter: useToStringFallback ? o => o?.ToString() : (Func<object, string>)null
#if SAVE_ASSEMBLIES
                , saveDebugAssembly: true
#endif
                );
        }

        [EventSourceInterface(Name = "TestLogData")]
        public interface ITestLogData<in T> : IDisposable
        {
            [Event(1)]
            void Test(T data);

            [Event(2)]
            void Test2(T data, int i);
        }

        public class CustomData
        {
            public override string ToString()
            {
                return nameof(CustomData);
            }
        }

        public void Dispose()
        {
#if NET46 && SAVE_ASSEMBLIES
            if (_generator != null)
            {
                _generator.SaveAssembly();
                System.IO.File.Move("GeneratedEventSources.dll", "GeneratedEventSources_" + Guid.NewGuid() + ".dll");
            }
#endif
        }
    }
}
# EventSourceExtensions

A .NET Standard (1.1) library for generating EventSource classes from interfaces at run-time


* Mapping custom parameter types to one or more other parameters
* Adding parameters
* Fallback converter

## NuGet

```powershell
Install-Package EventSourceExtensions
```

## Usage

```csharp

class Foo
{
   public int A { get ;}
   public Bar B { get; }
}

var configuration = EventSourceConfiguration.Empty
                        .WithMapping<Foo>(m => m.Map(foo => foo.A, "a").Map(foo => foo.B, "b"))
                        .WithAdditionalParameter(() => Trace.CorrelationManager.ActivityId, "correlationId"));
                        
var generator = new EventSourceGenerator(configuration,
                        fallbackConverter: o => JsonConvert.SerializeObject(o));

[EventSourceInterface(Name = "MyEventSource")]
interface IMyEventSource
{
   [Event(1)]
   void Log(Foo foo);
}

var fooSource = generator.Get<IMyEventSource>(); // optionally overrride global configuration here

fooSource.Log(new Foo());

```

The actual EventSource implementation would have this signature:

```csharp
[Event(1)]
void Log(int a, string b, Guid correlationId);
```

`b` is a string since no converter was specified for type `Bar`, and it's not a natively supported EventSource type.
So it would require specifying a `fallbackConverter`, which always returns a string.
In this example, it would attempt to convert `Bar` to JSON.

# Getting started

## Install the package

```bash
dotnet add package Sample.Library
```

## Greet someone

Create a [`Greeter`](cref:T:Sample.Library.Greeter) and call [Greet](cref:M:Sample.Library.Greeter.Greet):

```csharp
var greeter = new Greeter("World");
Console.WriteLine(greeter.Greet()); // Hello World!
```

> [!TIP]
> [`LoudGreeter`](cref:T:Sample.Library.LoudGreeter) inherits its documentation from `Greeter`.

namespace Sample.Library;

/// <summary>Creates greeting messages.</summary>
/// <remarks>Use <see cref="Greet()"/> or <see cref="Greet(string)"/>.</remarks>
public class Greeter
{
    /// <summary>Initializes a new instance of the <see cref="Greeter"/> class.</summary>
    /// <param name="name">The name to greet.</param>
    public Greeter(string name)
    {
        Name = name;
    }

    /// <summary>Gets the name to greet.</summary>
    public string Name { get; }

    /// <summary>Returns a greeting.</summary>
    /// <returns>The greeting, such as <c>Hello World!</c>.</returns>
    public string Greet() => Greet("Hello");

    /// <summary>Returns a greeting using a custom salutation.</summary>
    /// <param name="salutation">The salutation, such as <c>Hi</c>.</param>
    /// <returns>The greeting.</returns>
    public virtual string Greet(string salutation) => $"{salutation} {Name}!";

#if NET11_0_OR_GREATER
    /// <summary>Returns a greeting without allocating the salutation.</summary>
    /// <param name="salutation">The salutation.</param>
    /// <returns>The greeting.</returns>
    public string Greet(ReadOnlySpan<char> salutation) => $"{salutation} {Name}!";
#endif
}

/// <summary>A greeter that shouts.</summary>
public sealed class LoudGreeter : Greeter
{
    /// <inheritdoc />
    public LoudGreeter(string name)
        : base(name)
    {
    }

    /// <inheritdoc />
    public override string Greet(string salutation) => base.Greet(salutation).ToUpperInvariant();
}

/// <summary>Kinds of greetings.</summary>
public enum GreetingKind
{
    /// <summary>A formal greeting.</summary>
    Formal,

    /// <summary>A casual greeting.</summary>
    Casual,
}

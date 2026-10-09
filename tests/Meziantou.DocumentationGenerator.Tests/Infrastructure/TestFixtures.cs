namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Sources of fixture assemblies shared by the tests.</summary>
internal static class TestFixtures
{
    /// <summary>Covers every supported type and member category. Symbols guarded by NET8 only exist in the "net8.0" build.</summary>
    public const string KitchenSink = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        namespace Contoso.Library
        {
            /// <summary>Executes code when the assembly is loaded. Inspecting the package must never run it.</summary>
            internal static class Initializer
            {
                [ModuleInitializer]
                internal static void Initialize()
                {
                    Environment.SetEnvironmentVariable("DOCGEN_FIXTURE_EXECUTED", "1");
                    throw new InvalidOperationException("The fixture was executed");
                }
            }

            /// <summary>A client that sends <see cref="Message"/> instances.</summary>
            /// <remarks>Use <see cref="Send(string)"/> or <see cref="Send(Message, int)"/>.</remarks>
            public class Client : IDisposable, IEnumerable<Message>
            {
                /// <summary>The default port.</summary>
                public const int DefaultPort = 443;

                /// <summary>The shared instance.</summary>
                public static readonly Client Shared = new();

                /// <summary>Initializes a new instance of the <see cref="Client"/> class.</summary>
                public Client() { }

                /// <summary>Initializes a new instance with a name.</summary>
                /// <param name="name">The name of the client.</param>
                public Client(string name) { Name = name; }

                /// <summary>Gets or sets the name.</summary>
                public string? Name { get; set; }

                /// <summary>Gets the message at the specified index.</summary>
                /// <param name="index">The index.</param>
                public Message this[int index] => new();

                /// <summary>Gets the message with the specified key.</summary>
                /// <param name="key">The key.</param>
                public Message? this[string key] { get => null; set { } }

                /// <summary>Occurs when a message is sent.</summary>
                public event EventHandler<Message>? Sent;

                /// <summary>Sends a text message.</summary>
                /// <param name="text">The text.</param>
                /// <returns><see langword="true"/> when sent.</returns>
                public bool Send(string text) => true;

                /// <summary>Sends a message.</summary>
                /// <param name="message">The message.</param>
                /// <param name="retryCount">The retry count.</param>
                public void Send(Message message, int retryCount = 3) { }

        #if NET8
                /// <summary>Sends raw bytes.</summary>
                /// <param name="data">The data.</param>
                public void Send(ReadOnlySpan<byte> data) { }
        #endif

                /// <summary>Converts a value.</summary>
                /// <typeparam name="T">The type of the value.</typeparam>
                /// <param name="value">The value.</param>
                public T Convert<T>(object? value) where T : class, new() => new T();

                /// <summary>Called when a message is sent.</summary>
                protected virtual void OnSent(Message message) => Sent?.Invoke(this, message);

                /// <summary>Internal members are not documented.</summary>
                internal void Hidden() { }

                void IDisposable.Dispose() { }

                /// <summary>Returns an enumerator.</summary>
                public IEnumerator<Message> GetEnumerator() => throw new NotSupportedException();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

                /// <summary>Adds two clients.</summary>
                public static Client operator +(Client left, Client right) => left;

                /// <summary>Converts a name to a client.</summary>
                public static implicit operator Client(string name) => new(name);

                /// <summary>Converts a client to its name.</summary>
                public static explicit operator string?(Client client) => client.Name;
            }

            /// <summary>A message.</summary>
            /// <param name="Text">The text.</param>
            public record Message(string Text = "")
            {
                /// <summary>The priority.</summary>
                public int Priority { get; init; }
            }

            /// <summary>A point.</summary>
            public readonly record struct Point(int X, int Y);

            /// <summary>Message kinds.</summary>
            [Flags]
            public enum MessageKind
            {
                /// <summary>None.</summary>
                None = 0,
                /// <summary>Text.</summary>
                Text = 1,
                /// <summary>Binary.</summary>
                Binary = 2,
            }

            /// <summary>Handles a message.</summary>
            /// <param name="message">The message.</param>
            public delegate void MessageHandler(Message message);

            /// <summary>A channel.</summary>
            /// <typeparam name="T">The type of items.</typeparam>
            public interface IChannel<in T>
            {
                /// <summary>Writes an item.</summary>
                /// <param name="item">The item.</param>
                void Write(T item);
            }

            /// <summary>Extension methods.</summary>
            public static class ClientExtensions
            {
                /// <summary>Sends twice.</summary>
                /// <param name="client">The client.</param>
                /// <param name="text">The text.</param>
                public static void SendTwice(this Client client, string text) { }
            }

            /// <summary>A base type.</summary>
            public abstract class Base
            {
                /// <summary>Runs.</summary>
                public abstract void Run();

                /// <summary>Protected members of sealed types are not documented.</summary>
                protected virtual void Protected() { }
            }

            /// <summary>A sealed type.</summary>
            public sealed class Sealed : Base
            {
                /// <inheritdoc />
                public override void Run() { }

                /// <inheritdoc />
                protected override void Protected() { }
            }

            /// <summary>Obsolete type.</summary>
            [Obsolete("Use Client instead.")]
            public class Legacy { }

        #if NET8
            /// <summary>Only available in net8.0.</summary>
            public sealed class ModernOnly { }
        #endif
        }

        namespace Contoso.Library.Collections
        {
            /// <summary>A tree.</summary>
            /// <typeparam name="T">The type of values.</typeparam>
            public class Tree<T>
            {
                /// <summary>Adds a value.</summary>
                /// <param name="value">The value.</param>
                public void Add(T value) { }

                /// <summary>Maps the values.</summary>
                public Tree<TResult> Map<TResult>(Func<T, TResult> selector) => new();

                /// <summary>A node.</summary>
                public class Node
                {
                    /// <summary>The value.</summary>
                    public T? Value { get; set; }

                    /// <summary>A nested generic type.</summary>
                    /// <typeparam name="TKey">The key.</typeparam>
                    public class Child<TKey> where TKey : notnull
                    {
                    }
                }
            }
        }
        """;
}

namespace DxMessaging.Tests.Runtime.Core
{
    using DxMessaging.Core;
    using DxMessaging.Core.Attributes;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;

    // Types used for testing nested and internal support
    public partial class ContainerForNestedTypes
    {
        [DxTargetedMessage]
        [DxAutoConstructor]
        public readonly partial struct NestedMessage
        {
            public readonly int id;

            [DxOptionalParameter]
            public readonly int optional;
        }

        [DxAutoConstructor]
        public readonly partial struct NestedAutoOnly
        {
            public readonly int width;

            [DxOptionalParameter]
            public readonly int height;
        }

        [DxAutoConstructor]
        public readonly partial struct DefaultsCase
        {
            public readonly int a;

            [DxOptionalParameter(5)]
            public readonly int b;

            [DxOptionalParameter("hello")]
            public readonly string c;

            [DxOptionalParameter(Expression = "null")]
            public readonly string d;
        }
    }

    [DxUntargetedMessage]
    [DxAutoConstructor]
    internal readonly partial struct InternalUntargeted
    {
        public readonly int value;

        [DxOptionalParameter]
        public readonly int tag;
    }

    public sealed class SourceGeneratorNestedTests
    {
        [Test]
        public void NestedAutoConstructorWorks()
        {
            ContainerForNestedTypes.NestedAutoOnly a = new(10);
            Assert.AreEqual(10, a.width, "Width should be assigned by generated constructor");
            Assert.AreEqual(0, a.height, "Height should default via optional parameter");
        }

        [Test]
        public void NestedMessageImplementsInterfaces()
        {
            ContainerForNestedTypes.NestedMessage m = new(7);
            Assert.AreEqual(7, m.id, "Id should be assigned by generated constructor");
            Assert.AreEqual(0, m.optional, "Optional should default to 0");

            IMessage asIMessage = m;
            Assert.AreEqual(
                typeof(ContainerForNestedTypes.NestedMessage),
                asIMessage.MessageType,
                "MessageType should be the concrete nested type"
            );
            Assert.IsTrue(
                typeof(ITargetedMessage).IsAssignableFrom(
                    typeof(ContainerForNestedTypes.NestedMessage)
                ),
                "Nested message should implement ITargetedMessage"
            );
        }

        [Test]
        public void InternalTypesGenerateCorrectly()
        {
            InternalUntargeted msg = new(123);
            Assert.AreEqual(123, msg.value, "Value should be assigned by generated constructor");
            Assert.AreEqual(0, msg.tag, "Tag should default via optional parameter");

            IMessage asIMessage = msg;
            Assert.AreEqual(
                typeof(InternalUntargeted),
                asIMessage.MessageType,
                "MessageType should resolve for internal types"
            );
            Assert.IsTrue(
                typeof(IUntargetedMessage).IsAssignableFrom(typeof(InternalUntargeted)),
                "Internal type should implement IUntargetedMessage"
            );
        }

        [Test]
        public void OptionalDefaultValuesWork()
        {
            ContainerForNestedTypes.DefaultsCase d = new(1);
            Assert.AreEqual(1, d.a, "Explicit a should be set");
            Assert.AreEqual(5, d.b, "b should default to 5 via attribute");
            Assert.AreEqual("hello", d.c, "c should default to a string literal");
            Assert.IsNull(d.d, "d should default to null via Expression");
        }
    }
}

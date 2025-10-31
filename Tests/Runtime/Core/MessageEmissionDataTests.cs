namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Linq;
    using DxMessaging.Core;
    using DxMessaging.Core.Diagnostics;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;

    public sealed class MessageEmissionDataTests
    {
        [Test]
        public void StackTraceOmitsDxMessagingFrames()
        {
            MessageEmissionData data = CaptureMessageEmission();

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(data.stackTrace),
                "Stack trace should capture emission site."
            );

            string[] lines = data.stackTrace.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries
            );

            Assert.That(
                lines.All(line => !line.Contains("DxMessaging.Core.Diagnostics")),
                "Stack trace should omit DxMessaging internal frames."
            );

            Assert.That(
                lines.Any(line => line.Contains(nameof(StackTraceOmitsDxMessagingFrames))),
                "Stack trace should include calling test method for debugging context."
            );
        }

        [Test]
        public void ContextIsCapturedWhenProvided()
        {
            InstanceId expectedContext = new(12345);
            MessageEmissionData data = new(new TestUntargetedMessage(), expectedContext);

            Assert.That(
                data.context.HasValue,
                Is.True,
                "Context should be captured when supplied."
            );
            Assert.That(data.context.Value, Is.EqualTo(expectedContext));
        }

        private static MessageEmissionData CaptureMessageEmission()
        {
            return CreateEmissionData();

            static MessageEmissionData CreateEmissionData()
            {
                return new MessageEmissionData(new TestUntargetedMessage());
            }
        }

        private readonly struct TestUntargetedMessage : IUntargetedMessage { }
    }
}

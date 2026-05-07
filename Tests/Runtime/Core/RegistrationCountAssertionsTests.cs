#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using DxMessaging.Tests.Runtime;
    using NUnit.Framework;

    /// <summary>
    /// Self-tests for <see cref="RegistrationCountAssertions"/>. Confirms the
    /// helper:
    /// <list type="bullet">
    /// <item>returns silently when the bus matches the expected counts;</item>
    /// <item>fails with a message that names the diverging bucket(s),
    /// surfaces the per-bucket delta, includes the call site, and identifies
    /// the bus expression captured by <c>[CallerArgumentExpression]</c>;</item>
    /// <item>throws <see cref="ArgumentNullException"/> for a null bus;</item>
    /// <item>honors the <c>includeBucketingReminder</c> opt-out flag.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses a hand-rolled <see cref="StubCountingMessageBus"/> that returns
    /// hardcoded counts. The stub throws <see cref="NotImplementedException"/>
    /// for every method the helper does not exercise so any future drift in
    /// the helper that starts touching unrelated bus surface fails loudly.
    /// </remarks>
    public sealed class RegistrationCountAssertionsTests
    {
        [Test]
        public void HelperReturnsSilentlyWhenCountsMatch()
        {
            StubCountingMessageBus bus = new(untargeted: 3, targeted: 5, broadcast: 7);

            Assert.DoesNotThrow(() =>
                RegistrationCountAssertions.AssertRegistrationCounts(
                    bus,
                    untargeted: 3,
                    targeted: 5,
                    broadcast: 7,
                    context: "match path"
                )
            );
        }

        [Test]
        public void HelperFailsWithPerBucketDeltaCallSiteAndBusExpression()
        {
            StubCountingMessageBus expectedBusName = new(untargeted: 1, targeted: 4, broadcast: 3);

            AssertionException exception = Assert.Throws<AssertionException>(() =>
                RegistrationCountAssertions.AssertRegistrationCounts(
                    expectedBusName,
                    untargeted: 1,
                    targeted: 5,
                    broadcast: 3,
                    context: "second component disabled"
                )
            );

            string message = exception.Message;
            // Per-bucket delta line for the diverging bucket (Targeted), with
            // the explicit signed delta and the actual/expected pair.
            StringAssert.Contains("Targeted: expected 5, actual 4 (delta -1)", message);
            // Buckets that match must NOT appear in the per-bucket diff. Cheap
            // way to confirm: the matching buckets do not have a "(delta " suffix.
            StringAssert.DoesNotContain("Untargeted: expected 1, actual 1", message);
            StringAssert.DoesNotContain("Broadcast: expected 3, actual 3", message);
            // The full triple appears as tail-line context.
            StringAssert.Contains("Untargeted=1, Targeted=4, Broadcast=3", message);
            // Caller-supplied label propagates.
            StringAssert.Contains("second component disabled", message);
            // CallerArgumentExpression must surface the local name passed for
            // the bus parameter so multi-bus tests can attribute the failure.
            StringAssert.Contains("expectedBusName", message);
            // The call site is captured (file path + this test method name).
            StringAssert.Contains(
                nameof(HelperFailsWithPerBucketDeltaCallSiteAndBusExpression),
                message
            );
            StringAssert.Contains("RegistrationCountAssertionsTests.cs", message);
            // Bucketing reminder is on by default.
            StringAssert.Contains("Bucketing reminder", message);
        }

        [Test]
        public void HelperReportsAllDivergingBucketsWhenAllMismatch()
        {
            StubCountingMessageBus stub = new(untargeted: 2, targeted: 9, broadcast: 0);

            AssertionException exception = Assert.Throws<AssertionException>(() =>
                RegistrationCountAssertions.AssertRegistrationCounts(
                    stub,
                    untargeted: 1,
                    targeted: 5,
                    broadcast: 3,
                    context: "all buckets diverge"
                )
            );

            string message = exception.Message;
            StringAssert.Contains("Untargeted: expected 1, actual 2 (delta +1)", message);
            StringAssert.Contains("Targeted: expected 5, actual 9 (delta +4)", message);
            StringAssert.Contains("Broadcast: expected 3, actual 0 (delta -3)", message);
        }

        [Test]
        public void HelperOmitsBucketingReminderWhenOptedOut()
        {
            StubCountingMessageBus stub = new(untargeted: 0, targeted: 1, broadcast: 0);

            AssertionException exception = Assert.Throws<AssertionException>(() =>
                RegistrationCountAssertions.AssertRegistrationCounts(
                    stub,
                    untargeted: 0,
                    targeted: 0,
                    broadcast: 0,
                    context: "no reminder",
                    includeBucketingReminder: false
                )
            );

            StringAssert.DoesNotContain("Bucketing reminder", exception.Message);
            // The actionable per-bucket delta line is still present.
            StringAssert.Contains("Targeted: expected 0, actual 1 (delta +1)", exception.Message);
        }

        [Test]
        public void HelperThrowsArgumentNullExceptionForNullBus()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                RegistrationCountAssertions.AssertRegistrationCounts(
                    bus: null,
                    untargeted: 0,
                    targeted: 0,
                    broadcast: 0,
                    context: "null bus path"
                )
            );

            Assert.AreEqual("bus", exception.ParamName);
        }

        /// <summary>
        /// Minimal <see cref="IMessageBus"/> stub that returns hardcoded
        /// counter values. Throws <see cref="NotImplementedException"/> for
        /// every method the helper does not invoke so a future refactor that
        /// starts touching additional bus surface fails loudly instead of
        /// silently passing the wrong default value through.
        /// </summary>
        private sealed class StubCountingMessageBus : IMessageBus
        {
            public StubCountingMessageBus(int untargeted, int targeted, int broadcast)
            {
                RegisteredUntargeted = untargeted;
                RegisteredTargeted = targeted;
                RegisteredBroadcast = broadcast;
            }

            public int RegisteredUntargeted { get; }

            public int RegisteredTargeted { get; }

            public int RegisteredBroadcast { get; }

            public bool DiagnosticsMode => throw new NotImplementedException();

            public int RegisteredGlobalSequentialIndex => throw new NotImplementedException();

            public int OccupiedTypeSlots => throw new NotImplementedException();

            public int OccupiedTargetSlots => throw new NotImplementedException();

            public int RegisteredInterceptors => throw new NotImplementedException();

            public int RegisteredPostProcessors => throw new NotImplementedException();

            public int RegisteredGlobalAcceptAll => throw new NotImplementedException();

            public RegistrationLog Log => throw new NotImplementedException();

            public long EmissionId => throw new NotImplementedException();

            public IMessageBus.TrimResult Trim(bool force = false) =>
                throw new NotImplementedException();

            public Action RegisterUntargeted<T>(MessageHandler messageHandler, int priority = 0)
                where T : IUntargetedMessage => throw new NotImplementedException();

            public Action RegisterUntargetedPostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IUntargetedMessage => throw new NotImplementedException();

            public Action RegisterTargeted<T>(
                InstanceId target,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage => throw new NotImplementedException();

            public Action RegisterTargetedPostProcessor<T>(
                InstanceId target,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage => throw new NotImplementedException();

            public Action RegisterTargetedWithoutTargeting<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage => throw new NotImplementedException();

            public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage => throw new NotImplementedException();

            public Action RegisterSourcedBroadcast<T>(
                InstanceId source,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage => throw new NotImplementedException();

            public Action RegisterBroadcastPostProcessor<T>(
                InstanceId source,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage => throw new NotImplementedException();

            public Action RegisterSourcedBroadcastWithoutSource<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage => throw new NotImplementedException();

            public Action RegisterBroadcastWithoutSourcePostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage => throw new NotImplementedException();

            public Action RegisterGlobalAcceptAll(MessageHandler messageHandler) =>
                throw new NotImplementedException();

            public Action RegisterUntargetedInterceptor<T>(
                IMessageBus.UntargetedInterceptor<T> interceptor,
                int priority = 0
            )
                where T : IUntargetedMessage => throw new NotImplementedException();

            public Action RegisterTargetedInterceptor<T>(
                IMessageBus.TargetedInterceptor<T> interceptor,
                int priority = 0
            )
                where T : ITargetedMessage => throw new NotImplementedException();

            public Action RegisterBroadcastInterceptor<T>(
                IMessageBus.BroadcastInterceptor<T> interceptor,
                int priority = 0
            )
                where T : IBroadcastMessage => throw new NotImplementedException();

            public void UntypedUntargetedBroadcast(IUntargetedMessage typedMessage) =>
                throw new NotImplementedException();

            public void UntargetedBroadcast<TMessage>(ref TMessage typedMessage)
                where TMessage : IUntargetedMessage => throw new NotImplementedException();

            public void UntypedTargetedBroadcast(
                InstanceId target,
                ITargetedMessage typedMessage
            ) => throw new NotImplementedException();

            public void TargetedBroadcast<TMessage>(
                ref InstanceId target,
                ref TMessage typedMessage
            )
                where TMessage : ITargetedMessage => throw new NotImplementedException();

            public void UntypedSourcedBroadcast(
                InstanceId source,
                IBroadcastMessage typedMessage
            ) => throw new NotImplementedException();

            public void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage)
                where TMessage : IBroadcastMessage => throw new NotImplementedException();
        }
    }
}
#endif

namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;
    using GlobalMessageBus = DxMessaging.Core.MessageBus.MessageBus;

    [TestFixture]
    public sealed class MessageHandlerGlobalBusTests
    {
        private IMessageBus _originalBus;

        [SetUp]
        public void CaptureOriginalBus()
        {
            _originalBus = MessageHandler.MessageBus;
        }

        [TearDown]
        public void RestoreOriginalBus()
        {
            MessageHandler.SetGlobalMessageBus(_originalBus);
        }

        [Test]
        public void SetGlobalMessageBusReplacesGlobalInstance()
        {
            GlobalMessageBus customBus = new GlobalMessageBus();
            MessageHandler.SetGlobalMessageBus(customBus);

            Assert.AreSame(customBus, MessageHandler.MessageBus);
        }

        [Test]
        public void ResetGlobalMessageBusRestoresDefaultInstance()
        {
            MessageHandler.ResetGlobalMessageBus();
            IMessageBus expectedDefault = MessageHandler.MessageBus;

            GlobalMessageBus customBus = new GlobalMessageBus();
            MessageHandler.SetGlobalMessageBus(customBus);
            Assert.AreSame(customBus, MessageHandler.MessageBus);

            MessageHandler.ResetGlobalMessageBus();
            Assert.AreSame(expectedDefault, MessageHandler.MessageBus);
        }

        [Test]
        public void SetGlobalMessageBusAcceptsInterfaceImplementation()
        {
            WrapperMessageBus wrapper = new WrapperMessageBus(new GlobalMessageBus());
            MessageHandler.SetGlobalMessageBus(wrapper);
            Assert.AreSame(wrapper, MessageHandler.MessageBus);
        }

        [Test]
        public void TrimAllUsesCurrentGlobalMessageBus()
        {
            CountingTrimMessageBus wrapper = new CountingTrimMessageBus(new GlobalMessageBus());
            MessageHandler.SetGlobalMessageBus(wrapper);

            IMessageBus.TrimResult result = MessageHandler.TrimAll(force: true);

            Assert.AreEqual(1, wrapper.TrimCallCount);
            Assert.IsTrue(wrapper.LastForce);
            // The wrapped bus has no registrations, so its eviction-side fields are always zero.
            // PooledCollectionsEvicted is intentionally NOT asserted: Trim(force: true) drains
            // AppDomain-scoped static pools (DxPools / ContextHandlerByTargetDicts) shared with
            // other test fixtures, so its value is non-deterministic across test orderings.
            Assert.AreEqual(
                0,
                result.TypeSlotsEvicted,
                "TypeSlotsEvicted should be 0 on a fresh bus."
            );
            Assert.AreEqual(
                0,
                result.TargetSlotsEvicted,
                "TargetSlotsEvicted should be 0 on a fresh bus."
            );
            Assert.AreEqual(
                0,
                result.LiveTypeSlotsRemaining,
                "LiveTypeSlotsRemaining should be 0 on a fresh bus."
            );
        }

        [Test]
        public void TrimAllPropagatesInnerBusResultUnchanged()
        {
            IMessageBus.TrimResult sentinel = new IMessageBus.TrimResult(7, 11, 13, 17);
            SentinelTrimMessageBus wrapper = new SentinelTrimMessageBus(
                new GlobalMessageBus(),
                sentinel
            );
            MessageHandler.SetGlobalMessageBus(wrapper);

            IMessageBus.TrimResult result = MessageHandler.TrimAll(force: false);

            Assert.AreEqual(
                sentinel,
                result,
                "MessageHandler.TrimAll must return the inner bus's TrimResult unchanged. expected={0}, actual={1}",
                sentinel,
                result
            );
        }

        [Test]
        public void OverrideGlobalMessageBusScopeRestoresPreviousBus()
        {
            GlobalMessageBus primary = new GlobalMessageBus();
            MessageHandler.SetGlobalMessageBus(primary);
            WrapperMessageBus secondary = new WrapperMessageBus(new GlobalMessageBus());

            using (MessageHandler.OverrideGlobalMessageBus(secondary))
            {
                Assert.AreSame(secondary, MessageHandler.MessageBus);
            }

            Assert.AreSame(primary, MessageHandler.MessageBus);
        }

        private class WrapperMessageBus : IMessageBus
        {
            protected readonly IMessageBus _inner;

            public WrapperMessageBus(IMessageBus inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public bool DiagnosticsMode => _inner.DiagnosticsMode;

            public int RegisteredGlobalSequentialIndex => _inner.RegisteredGlobalSequentialIndex;

            public int OccupiedTypeSlots => _inner.OccupiedTypeSlots;

            public int OccupiedTargetSlots => _inner.OccupiedTargetSlots;

            public int RegisteredBroadcast => _inner.RegisteredBroadcast;

            public int RegisteredTargeted => _inner.RegisteredTargeted;

            public int RegisteredUntargeted => _inner.RegisteredUntargeted;

            public int RegisteredInterceptors => _inner.RegisteredInterceptors;

            public int RegisteredPostProcessors => _inner.RegisteredPostProcessors;

            public int RegisteredGlobalAcceptAll => _inner.RegisteredGlobalAcceptAll;

            public RegistrationLog Log => _inner.Log;

            public long EmissionId => _inner.EmissionId;

            public virtual IMessageBus.TrimResult Trim(bool force = false) => _inner.Trim(force);

            public Action RegisterUntargeted<T>(MessageHandler messageHandler, int priority = 0)
                where T : IUntargetedMessage =>
                _inner.RegisterUntargeted<T>(messageHandler, priority);

            public Action RegisterUntargetedPostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IUntargetedMessage =>
                _inner.RegisterUntargetedPostProcessor<T>(messageHandler, priority);

            public Action RegisterTargeted<T>(
                InstanceId target,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage =>
                _inner.RegisterTargeted<T>(target, messageHandler, priority);

            public Action RegisterTargetedPostProcessor<T>(
                InstanceId target,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage =>
                _inner.RegisterTargetedPostProcessor<T>(target, messageHandler, priority);

            public Action RegisterTargetedWithoutTargeting<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage =>
                _inner.RegisterTargetedWithoutTargeting<T>(messageHandler, priority);

            public Action RegisterTargetedWithoutTargetingPostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : ITargetedMessage =>
                _inner.RegisterTargetedWithoutTargetingPostProcessor<T>(messageHandler, priority);

            public Action RegisterBroadcastPostProcessor<T>(
                InstanceId source,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage =>
                _inner.RegisterBroadcastPostProcessor<T>(source, messageHandler, priority);

            public Action RegisterBroadcastWithoutSourcePostProcessor<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage =>
                _inner.RegisterBroadcastWithoutSourcePostProcessor<T>(messageHandler, priority);

            public Action RegisterSourcedBroadcast<T>(
                InstanceId source,
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage =>
                _inner.RegisterSourcedBroadcast<T>(source, messageHandler, priority);

            public Action RegisterSourcedBroadcastWithoutSource<T>(
                MessageHandler messageHandler,
                int priority = 0
            )
                where T : IBroadcastMessage =>
                _inner.RegisterSourcedBroadcastWithoutSource<T>(messageHandler, priority);

            public Action RegisterGlobalAcceptAll(MessageHandler messageHandler) =>
                _inner.RegisterGlobalAcceptAll(messageHandler);

            public Action RegisterUntargetedInterceptor<T>(
                IMessageBus.UntargetedInterceptor<T> interceptor,
                int priority = 0
            )
                where T : IUntargetedMessage =>
                _inner.RegisterUntargetedInterceptor(interceptor, priority);

            public Action RegisterTargetedInterceptor<T>(
                IMessageBus.TargetedInterceptor<T> interceptor,
                int priority = 0
            )
                where T : ITargetedMessage =>
                _inner.RegisterTargetedInterceptor(interceptor, priority);

            public Action RegisterBroadcastInterceptor<T>(
                IMessageBus.BroadcastInterceptor<T> interceptor,
                int priority = 0
            )
                where T : IBroadcastMessage =>
                _inner.RegisterBroadcastInterceptor(interceptor, priority);

            public void UntypedUntargetedBroadcast(IUntargetedMessage typedMessage) =>
                _inner.UntypedUntargetedBroadcast(typedMessage);

            public void UntargetedBroadcast<TMessage>(ref TMessage typedMessage)
                where TMessage : IUntargetedMessage => _inner.UntargetedBroadcast(ref typedMessage);

            public void UntypedTargetedBroadcast(
                InstanceId target,
                ITargetedMessage typedMessage
            ) => _inner.UntypedTargetedBroadcast(target, typedMessage);

            public void TargetedBroadcast<TMessage>(
                ref InstanceId target,
                ref TMessage typedMessage
            )
                where TMessage : ITargetedMessage =>
                _inner.TargetedBroadcast(ref target, ref typedMessage);

            public void UntypedSourcedBroadcast(
                InstanceId source,
                IBroadcastMessage typedMessage
            ) => _inner.UntypedSourcedBroadcast(source, typedMessage);

            public void SourcedBroadcast<TMessage>(ref InstanceId source, ref TMessage typedMessage)
                where TMessage : IBroadcastMessage =>
                _inner.SourcedBroadcast(ref source, ref typedMessage);
        }

        private sealed class CountingTrimMessageBus : WrapperMessageBus
        {
            public CountingTrimMessageBus(IMessageBus inner)
                : base(inner) { }

            public int TrimCallCount { get; private set; }

            public bool LastForce { get; private set; }

            public override IMessageBus.TrimResult Trim(bool force = false)
            {
                TrimCallCount++;
                LastForce = force;
                return base.Trim(force);
            }
        }

        /// <summary>
        /// Wrapper that returns a fixed sentinel <see cref="IMessageBus.TrimResult"/> so the test
        /// can assert field-by-field propagation through <see cref="MessageHandler.TrimAll"/>
        /// without depending on the real bus's pool/eviction state.
        /// </summary>
        private sealed class SentinelTrimMessageBus : WrapperMessageBus
        {
            private readonly IMessageBus.TrimResult _sentinel;

            public SentinelTrimMessageBus(IMessageBus inner, IMessageBus.TrimResult sentinel)
                : base(inner)
            {
                _sentinel = sentinel;
            }

            public override IMessageBus.TrimResult Trim(bool force = false) => _sentinel;
        }
    }
}

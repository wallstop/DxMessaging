#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.MemoryReclaim
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Configuration;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Core.Messages;
    using DxMessaging.Core.Pooling;
    using DxMessaging.Tests.Runtime;
    using DxMessaging.Tests.Runtime.Core;
    using NUnit.Framework;
    using UnityEngine;

    [TestFixture]
    [Category("MemoryReclaim")]
    public sealed class MemoryReclamationTests : MessagingTestBase
    {
        private const int DistinctTargetCount = 1024;
        private static readonly InstanceId HandlerOwner = new InstanceId(0x5A17_0001);
        private static readonly InstanceId DefaultContext = new InstanceId(0x5A17_0002);

        [Test]
        public void TrimEvictsEmptyTypeSlots(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock(), idleEvictionTicks: 0);
            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageRegistrationToken token = CreateEnabledToken(bus);
            int baseline = bus.OccupiedTypeSlots;

            MessageRegistrationHandle first = RegisterFirst(scenario, token, DefaultContext);
            MessageRegistrationHandle second = RegisterSecond(scenario, token, DefaultContext);
            MessageRegistrationHandle third = RegisterThird(scenario, token, DefaultContext);

            token.RemoveRegistration(first);
            token.RemoveRegistration(second);
            token.RemoveRegistration(third);

            Assert.GreaterOrEqual(
                bus.OccupiedTypeSlots,
                baseline + 3,
                "[{0}] deregistered distinct message types must remain occupied until trim.",
                scenario.Kind
            );

            IMessageBus.TrimResult result = bus.Trim(force: true);

            Assert.GreaterOrEqual(
                result.TypeSlotsEvicted,
                3,
                "[{0}] trim must evict every empty distinct type slot.",
                scenario.Kind
            );
            Assert.AreEqual(
                baseline,
                bus.OccupiedTypeSlots,
                "[{0}] occupied type slots must return to the pre-test baseline.",
                scenario.Kind
            );
        }

        [Test]
        public void TrimEvictsEmptyTargetSlots(
            [ValueSource(
                typeof(MessageScenarios),
                nameof(MessageScenarios.KindsWithComponentTarget)
            )]
                MessageScenario scenario
        )
        {
            MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock(), idleEvictionTicks: 0);
            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageRegistrationToken token = CreateEnabledToken(bus);

            List<MessageRegistrationHandle> handles = new List<MessageRegistrationHandle>(
                DistinctTargetCount
            );
            for (int i = 0; i < DistinctTargetCount; ++i)
            {
                handles.Add(RegisterFirst(scenario, token, new InstanceId(0x5A18_0000 + i)));
            }

            foreach (MessageRegistrationHandle handle in handles)
            {
                token.RemoveRegistration(handle);
            }

            Assert.GreaterOrEqual(
                bus.OccupiedTargetSlots,
                DistinctTargetCount,
                "[{0}] every deregistered context must remain visible until trim.",
                scenario.Kind
            );

            IMessageBus.TrimResult result = bus.Trim(force: true);

            Assert.GreaterOrEqual(result.TargetSlotsEvicted, DistinctTargetCount);
            Assert.AreEqual(
                0,
                bus.OccupiedTargetSlots,
                "[{0}] trim must reclaim every empty target/source slot.",
                scenario.Kind
            );
        }

        [Test]
        public void IdleEvictionFiresAfterInterval(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            FakeClock clock = new FakeClock();
            MessageBus bus = MessageBus.CreateForInternalUse(
                clock,
                idleEvictionTicks: 0,
                evictionTickIntervalSeconds: 1d,
                idleEvictionEnabled: true,
                trimApiEnabled: true
            );
            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageRegistrationToken token = CreateEnabledToken(bus);
            MessageRegistrationHandle handle = RegisterFirst(scenario, token, DefaultContext);
            token.RemoveRegistration(handle);
            EmitSweepProbe(bus);

            Assert.GreaterOrEqual(
                bus.OccupiedTypeSlots + bus.OccupiedTargetSlots,
                1,
                "[{0}] the fresh empty slot must not be reclaimed before cadence elapses.",
                scenario.Kind
            );

            clock.Advance(1d);
            EmitSweepProbe(bus);

            Assert.AreEqual(
                0,
                bus.OccupiedTypeSlots,
                "[{0}] idle sweep must reclaim empty type slots after cadence.",
                scenario.Kind
            );
            Assert.AreEqual(
                0,
                bus.OccupiedTargetSlots,
                "[{0}] idle sweep must reclaim empty target/source slots after cadence.",
                scenario.Kind
            );
        }

        [Test]
        public void IdleEvictionLeavesNonEmptySlotsAlone(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            FakeClock clock = new FakeClock();
            MessageBus bus = MessageBus.CreateForInternalUse(
                clock,
                idleEvictionTicks: 0,
                evictionTickIntervalSeconds: 0d,
                idleEvictionEnabled: true,
                trimApiEnabled: true
            );
            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageRegistrationToken token = CreateEnabledToken(bus);
            int calls = 0;

            MessageRegistrationHandle handle = RegisterCountingFirst(
                scenario,
                token,
                DefaultContext,
                () => calls++
            );
            try
            {
                clock.Advance(3600d);
                EmitSweepProbe(bus);
                EmitSweepProbe(bus);
                EmitFirst(scenario, bus, DefaultContext);

                Assert.AreEqual(
                    1,
                    calls,
                    "[{0}] idle sweep must not remove live registrations.",
                    scenario.Kind
                );
                Assert.GreaterOrEqual(bus.OccupiedTypeSlots, 1);
            }
            finally
            {
                token.RemoveRegistration(handle);
            }
        }

        [Test]
        public void TrimDoesNotDisturbActiveDispatch(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock(), idleEvictionTicks: 0);
            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageRegistrationToken token = CreateEnabledToken(bus);
            int trimmingHandlerCalls = 0;
            int trailingHandlerCalls = 0;

            MessageRegistrationHandle trimmingHandle = RegisterCountingFirst(
                scenario,
                token,
                DefaultContext,
                () =>
                {
                    trimmingHandlerCalls++;
                    _ = bus.Trim(force: true);
                },
                priority: 0
            );
            MessageRegistrationHandle trailingHandle = RegisterCountingFirst(
                scenario,
                token,
                DefaultContext,
                () => trailingHandlerCalls++,
                priority: 1
            );
            try
            {
                EmitFirst(scenario, bus, DefaultContext);

                Assert.AreEqual(1, trimmingHandlerCalls);
                Assert.AreEqual(
                    1,
                    trailingHandlerCalls,
                    "[{0}] trim during dispatch must not disturb the active snapshot.",
                    scenario.Kind
                );
            }
            finally
            {
                token.RemoveRegistration(trimmingHandle);
                token.RemoveRegistration(trailingHandle);
            }
        }

        [Test]
        public void RuntimeSettingsHotReloadAppliesCaps()
        {
            DxMessagingRuntimeSettings settings =
                ScriptableObject.CreateInstance<DxMessagingRuntimeSettings>();
            IDisposable overrideToken = null;
            try
            {
                settings._bufferMaxDistinctEntries = 4;
                settings._bufferUseLruEviction = true;
                overrideToken = DxMessagingRuntimeSettingsProvider.Override(settings);
                MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock());

                List<object> pooled = DxPools.ObjectLists.Rent();
                DxPools.ObjectLists.Return(pooled);
                Assert.Greater(DxPools.DescribeAll().ObjectLists.Cached, 0);

                settings._bufferMaxDistinctEntries = 0;
                settings._bufferUseLruEviction = false;
                DxMessagingRuntimeSettings.RaiseSettingsChanged(settings);

                Assert.AreEqual(0, DxPools.ObjectLists.MaxRetained);
                Assert.IsFalse(DxPools.ObjectLists.UseLru);
                Assert.AreEqual(0, DxPools.DescribeAll().ObjectLists.Cached);
                GC.KeepAlive(bus);
            }
            finally
            {
                overrideToken?.Dispose();
                UnityEngine.Object.DestroyImmediate(settings);
                _ = DxPools.TrimAll(force: true);
            }
        }

        [Test]
        public void RuntimeSettingsHotReloadUpdatesTrimAndIdleGates()
        {
            DxMessagingRuntimeSettings settings =
                ScriptableObject.CreateInstance<DxMessagingRuntimeSettings>();
            IDisposable overrideToken = null;
            try
            {
                settings._enableTrimApi = true;
                settings._evictionEnabled = false;
                settings._idleEvictionSeconds = 30f;
                settings._evictionTickIntervalSeconds = 60f;
                overrideToken = DxMessagingRuntimeSettingsProvider.Override(settings);
                FakeClock clock = new FakeClock();
                MessageBus bus = MessageBus.CreateForInternalUse(clock);
                using IDisposable cleanup = ForceTrimCleanup(bus);
                MessageRegistrationToken token = CreateEnabledToken(bus);
                MessageRegistrationHandle handle = RegisterFirst(
                    MessageScenario.Untargeted(),
                    token,
                    DefaultContext
                );
                token.RemoveRegistration(handle);

                clock.Advance(60d);
                EmitSweepProbe(bus);
                EmitSweepProbe(bus);
                Assert.GreaterOrEqual(
                    bus.OccupiedTypeSlots,
                    1,
                    "Initial disabled idle eviction setting must prevent emit-time sweep."
                );

                settings._enableTrimApi = false;
                settings._evictionEnabled = true;
                settings._idleEvictionSeconds = 0f;
                settings._evictionTickIntervalSeconds = 0f;
                DxMessagingRuntimeSettings.RaiseSettingsChanged(settings);
                Assert.AreEqual(default(IMessageBus.TrimResult), bus.Trim(force: true));

                EmitSweepProbe(bus);
                EmitSweepProbe(bus);
                Assert.AreEqual(
                    0,
                    bus.OccupiedTypeSlots,
                    "Hot-reloaded idle settings must enable emit-time reclamation on an existing bus."
                );
            }
            finally
            {
                overrideToken?.Dispose();
                UnityEngine.Object.DestroyImmediate(settings);
                _ = DxPools.TrimAll(force: true);
            }
        }

        [Test]
        public void TrimRespectsEnableTrimApiFlag()
        {
            DxMessagingRuntimeSettings settings =
                ScriptableObject.CreateInstance<DxMessagingRuntimeSettings>();
            IDisposable overrideToken = null;
            try
            {
                settings._enableTrimApi = false;
                settings._evictionEnabled = true;
                settings._bufferMaxDistinctEntries = 4;
                overrideToken = DxMessagingRuntimeSettingsProvider.Override(settings);
                MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock());
                MessageRegistrationToken token = CreateEnabledToken(bus);
                MessageRegistrationHandle handle = RegisterFirst(
                    MessageScenario.Untargeted(),
                    token,
                    DefaultContext
                );
                token.RemoveRegistration(handle);
                List<object> pooled = DxPools.ObjectLists.Rent();
                DxPools.ObjectLists.Return(pooled);
                int cachedBefore = DxPools.DescribeAll().ObjectLists.Cached;

                IMessageBus.TrimResult result = bus.Trim(force: true);

                Assert.AreEqual(default(IMessageBus.TrimResult), result);
                Assert.GreaterOrEqual(bus.OccupiedTypeSlots, 1);
                Assert.AreEqual(cachedBefore, DxPools.DescribeAll().ObjectLists.Cached);
            }
            finally
            {
                overrideToken?.Dispose();
                UnityEngine.Object.DestroyImmediate(settings);
                _ = DxPools.TrimAll(force: true);
            }
        }

        [Test]
        public void TrimAfterDeregisterReclaimsHandlerCache(
            [ValueSource(
                typeof(MessageScenarios),
                nameof(MessageScenarios.KindsWithComponentTarget)
            )]
                MessageScenario scenario
        )
        {
            DxMessagingRuntimeSettings settings =
                ScriptableObject.CreateInstance<DxMessagingRuntimeSettings>();
            IDisposable overrideToken = null;
            try
            {
                settings._bufferMaxDistinctEntries = 4;
                settings._bufferUseLruEviction = true;
                overrideToken = DxMessagingRuntimeSettingsProvider.Override(settings);
                _ = DxPools.TrimAll(force: true);
                MessageBus bus = MessageBus.CreateForInternalUse(
                    new FakeClock(),
                    idleEvictionTicks: 0
                );
                using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                    bus,
                    label: scenario.DisplayName
                );
                using IDisposable cleanup = ForceTrimCleanup(bus);
                MessageRegistrationToken token = CreateEnabledToken(bus);
                MessageRegistrationHandle handle = RegisterFirst(scenario, token, DefaultContext);
                token.RemoveRegistration(handle);
                EmitSweepProbe(bus);
                int contextDictionariesBefore = DxPools
                    .DescribeAll()
                    .TypedHandlerContextDicts.Cached;
                int priorityDictionariesBefore = DxPools
                    .DescribeAll()
                    .TypedHandlerPriorityDicts.Cached;

                IMessageBus.TrimResult result = bus.Trim(force: false);

                Assert.GreaterOrEqual(
                    result.TypeSlotsEvicted,
                    1,
                    "[{0}] non-force trim must reclaim idle empty typed-handler slots.",
                    scenario.Kind
                );
                Assert.Greater(
                    DxPools.DescribeAll().TypedHandlerContextDicts.Cached,
                    contextDictionariesBefore,
                    "[{0}] trim must return the typed-handler context dictionary to the pool.",
                    scenario.Kind
                );
                Assert.Greater(
                    DxPools.DescribeAll().TypedHandlerPriorityDicts.Cached,
                    priorityDictionariesBefore,
                    "[{0}] trim must return the typed-handler priority dictionary to the pool.",
                    scenario.Kind
                );
            }
            finally
            {
                overrideToken?.Dispose();
                UnityEngine.Object.DestroyImmediate(settings);
                _ = DxPools.TrimAll(force: true);
            }
        }

        [Test]
        public void ResetGenerationBumpInvalidatesPostEvictDeregister(
            [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
                MessageScenario scenario
        )
        {
            MessageBus bus = MessageBus.CreateForInternalUse(new FakeClock(), idleEvictionTicks: 0);
            using IDisposable cleanup = ForceTrimCleanup(bus);
            MessageHandler handler = CreateActiveHandler(bus);
            int currentCalls = 0;
            int staleCalls = 0;
            List<string> logs = new List<string>();
            Action<LogLevel, string> previousLogFunction = MessagingDebug.LogFunction;
            bool previousMessagingDebugEnabled = MessagingDebug.enabled;
            Action currentDeregister = null;

            using LeakWatcher watcher = LeakWatcher.WatchWithSlots(
                bus,
                label: scenario.DisplayName
            );
            try
            {
                Action staleDeregister = RegisterDirect(
                    scenario,
                    handler,
                    bus,
                    DefaultContext,
                    () => staleCalls++
                );
                staleDeregister();
                _ = bus.Trim(force: true);

                currentDeregister = RegisterDirect(
                    scenario,
                    handler,
                    bus,
                    DefaultContext,
                    () => currentCalls++
                );
                MessagingDebug.enabled = true;
                MessagingDebug.LogFunction = (_, message) => logs.Add(message);
                staleDeregister();
                EmitFirst(scenario, bus, DefaultContext);

                Assert.AreEqual(
                    0,
                    staleCalls,
                    "[{0}] stale deregistration must not revive or invoke the old handler.",
                    scenario.Kind
                );
                Assert.AreEqual(
                    1,
                    currentCalls,
                    "[{0}] stale deregistration must not remove a later registration.",
                    scenario.Kind
                );
                Assert.AreEqual(
                    0,
                    logs.Count,
                    "[{0}] stale deregistration must not log diagnostics.",
                    scenario.Kind
                );
                currentDeregister();
                currentDeregister = null;
                _ = bus.Trim(force: true);
            }
            finally
            {
                MessagingDebug.enabled = previousMessagingDebugEnabled;
                MessagingDebug.LogFunction = previousLogFunction;
                currentDeregister?.Invoke();
                _ = bus.Trim(force: true);
            }
        }

        private static MessageRegistrationToken CreateEnabledToken(MessageBus bus)
        {
            MessageHandler handler = CreateActiveHandler(bus);
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, bus);
            token.Enable();
            return token;
        }

        private static MessageHandler CreateActiveHandler(MessageBus bus)
        {
            return new MessageHandler(HandlerOwner, bus) { active = true };
        }

        private static IDisposable ForceTrimCleanup(MessageBus bus)
        {
            return new CleanupScope(() =>
            {
                _ = bus.Trim(force: true);
                _ = DxPools.TrimAll(force: true);
            });
        }

        private static MessageRegistrationHandle RegisterFirst(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId context
        )
        {
            return RegisterCountingFirst(scenario, token, context, () => { });
        }

        private static MessageRegistrationHandle RegisterSecond(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId context
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return token.RegisterUntargeted<UntargetedTwo>((ref UntargetedTwo _) => { });
                }
                case MessageKind.Targeted:
                {
                    return token.RegisterTargeted<TargetedTwo>(context, (ref TargetedTwo _) => { });
                }
                case MessageKind.Broadcast:
                {
                    return token.RegisterBroadcast<BroadcastTwo>(
                        context,
                        (ref BroadcastTwo _) => { }
                    );
                }
                default:
                {
                    throw UnsupportedScenario(scenario);
                }
            }
        }

        private static MessageRegistrationHandle RegisterThird(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId context
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return token.RegisterUntargeted<UntargetedThree>(
                        (ref UntargetedThree _) => { }
                    );
                }
                case MessageKind.Targeted:
                {
                    return token.RegisterTargeted<TargetedThree>(
                        context,
                        (ref TargetedThree _) => { }
                    );
                }
                case MessageKind.Broadcast:
                {
                    return token.RegisterBroadcast<BroadcastThree>(
                        context,
                        (ref BroadcastThree _) => { }
                    );
                }
                default:
                {
                    throw UnsupportedScenario(scenario);
                }
            }
        }

        private static MessageRegistrationHandle RegisterCountingFirst(
            MessageScenario scenario,
            MessageRegistrationToken token,
            InstanceId context,
            Action onMessage,
            int priority = 0
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    return token.RegisterUntargeted<UntargetedOne>(
                        (ref UntargetedOne _) => onMessage(),
                        priority: priority
                    );
                }
                case MessageKind.Targeted:
                {
                    return token.RegisterTargeted<TargetedOne>(
                        context,
                        (ref TargetedOne _) => onMessage(),
                        priority: priority
                    );
                }
                case MessageKind.Broadcast:
                {
                    return token.RegisterBroadcast<BroadcastOne>(
                        context,
                        (ref BroadcastOne _) => onMessage(),
                        priority: priority
                    );
                }
                default:
                {
                    throw UnsupportedScenario(scenario);
                }
            }
        }

        private static Action RegisterDirect(
            MessageScenario scenario,
            MessageHandler handler,
            MessageBus bus,
            InstanceId context,
            Action onMessage
        )
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    Action<UntargetedOne> callback = _ => onMessage();
                    return handler.RegisterUntargetedMessageHandler(
                        callback,
                        callback,
                        priority: 0,
                        messageBus: bus
                    );
                }
                case MessageKind.Targeted:
                {
                    Action<TargetedOne> callback = _ => onMessage();
                    return handler.RegisterTargetedMessageHandler(
                        context,
                        callback,
                        callback,
                        priority: 0,
                        messageBus: bus
                    );
                }
                case MessageKind.Broadcast:
                {
                    Action<BroadcastOne> callback = _ => onMessage();
                    return handler.RegisterSourcedBroadcastMessageHandler(
                        context,
                        callback,
                        callback,
                        priority: 0,
                        messageBus: bus
                    );
                }
                default:
                {
                    throw UnsupportedScenario(scenario);
                }
            }
        }

        private static void EmitFirst(MessageScenario scenario, MessageBus bus, InstanceId context)
        {
            switch (scenario.Kind)
            {
                case MessageKind.Untargeted:
                {
                    UntargetedOne message = new UntargetedOne();
                    bus.UntargetedBroadcast(ref message);
                    return;
                }
                case MessageKind.Targeted:
                {
                    TargetedOne message = new TargetedOne();
                    bus.TargetedBroadcast(ref context, ref message);
                    return;
                }
                case MessageKind.Broadcast:
                {
                    BroadcastOne message = new BroadcastOne();
                    bus.SourcedBroadcast(ref context, ref message);
                    return;
                }
                default:
                {
                    throw UnsupportedScenario(scenario);
                }
            }
        }

        private static void EmitSweepProbe(MessageBus bus)
        {
            SweepProbeMessage message = new SweepProbeMessage();
            bus.UntargetedBroadcast(ref message);
        }

        private static ArgumentOutOfRangeException UnsupportedScenario(MessageScenario scenario)
        {
            return new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario?.Kind,
                "Unsupported message kind."
            );
        }

        private sealed class CleanupScope : IDisposable
        {
            private readonly Action _cleanup;
            private bool _disposed;

            public CleanupScope(Action cleanup)
            {
                _cleanup = cleanup;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cleanup();
            }
        }

        private readonly struct UntargetedOne : IUntargetedMessage<UntargetedOne> { }

        private readonly struct UntargetedTwo : IUntargetedMessage<UntargetedTwo> { }

        private readonly struct UntargetedThree : IUntargetedMessage<UntargetedThree> { }

        private readonly struct TargetedOne : ITargetedMessage<TargetedOne> { }

        private readonly struct TargetedTwo : ITargetedMessage<TargetedTwo> { }

        private readonly struct TargetedThree : ITargetedMessage<TargetedThree> { }

        private readonly struct BroadcastOne : IBroadcastMessage<BroadcastOne> { }

        private readonly struct BroadcastTwo : IBroadcastMessage<BroadcastTwo> { }

        private readonly struct BroadcastThree : IBroadcastMessage<BroadcastThree> { }

        private readonly struct SweepProbeMessage : IUntargetedMessage<SweepProbeMessage> { }
    }
}
#endif

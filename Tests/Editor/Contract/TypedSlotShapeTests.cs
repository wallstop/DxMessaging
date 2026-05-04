namespace DxMessaging.Tests.Editor.Contract
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.Internal;
    using DxMessaging.Core.MessageBus.Internal;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;

    /// <summary>
    /// Contract guardrails for the typed-handler-side slot grid introduced in
    /// PLAN Phase P3.1: <see cref="IHandlerActionCache"/> (the non-generic
    /// erasure of the per-delegate-shape cache type),
    /// <see cref="TypedSlot{T}"/> (the per-message-type, per-<see cref="SlotKey"/>
    /// dispatch slot), and <see cref="TypedGlobalSlot"/> (the per-message-type
    /// accept-all slot).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These types are forward-compat plumbing in P3.1 -- no writer populates
    /// them yet -- but the eviction-driven monotonic <c>Reset()</c> contract
    /// (PLAN Risk Register R3) and the <see cref="IEvictableSlot"/> shape
    /// must be locked in before the storage migration in P3.2 / P3.3 starts
    /// reading them. Each test below pins one structural invariant so the
    /// migration can land without revisiting the per-slot lifecycle.
    /// </para>
    /// <para>
    /// The <see cref="IHandlerActionCacheInterfaceShape"/> test reflects over
    /// the interface's declared members and is the structural backstop for
    /// P3.2: when the storage migration retrofits
    /// <c>HandlerActionCache&lt;TDelegate&gt;</c> to implement
    /// <see cref="IHandlerActionCache"/>, any silent member add or remove
    /// here will fail this test until reviewers update both the interface
    /// and its expected-shape list in lockstep.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Contract")]
    public sealed class TypedSlotShapeTests
    {
        private readonly struct ProbeMessage : IUntargetedMessage { }

        /// <summary>
        /// Trivial in-test stub for <see cref="IHandlerActionCache"/>. Used so
        /// the slot tests can populate <see cref="TypedSlot{T}.byPriority"/>
        /// and <see cref="TypedSlot{T}.byContext"/> without depending on the
        /// real <c>HandlerActionCache&lt;TDelegate&gt;</c> implementation
        /// (which, in P3.1, does not yet implement the interface).
        /// </summary>
        private sealed class StubCache : IHandlerActionCache
        {
            public long Version { get; set; }

            public long LastSeenVersion { get; set; } = -1;

            public long LastSeenEmissionId { get; set; }

            public int PrefreezeInvocationCount { get; set; }

            public bool IsEmpty { get; set; } = true;

            public int ResetCallCount { get; private set; }

            public void Reset()
            {
                ++ResetCallCount;
            }
        }

        /// <summary>
        /// Probe stub for the drain-BEFORE-clear ordering tests (PLAN Risk
        /// Register R3). <see cref="ProbeOuterCount"/> is invoked from
        /// <see cref="Reset"/> and the observed value pinned in
        /// <see cref="ObservedOuterCountAtReset"/>; the slot tests set
        /// <see cref="ProbeOuterCount"/> to read the size of the outer
        /// container so a non-zero observation proves the inner drain ran
        /// while the outer dict still held the entry.
        /// </summary>
        private sealed class OrderingProbeCache : IHandlerActionCache
        {
            public Func<int> ProbeOuterCount = () => -1;
            public int ObservedOuterCountAtReset = -2;

            public long Version => 0;

            public long LastSeenVersion { get; set; }

            public long LastSeenEmissionId { get; set; }

            public int PrefreezeInvocationCount => 0;

            public bool IsEmpty => true;

            public void Reset()
            {
                ObservedOuterCountAtReset = ProbeOuterCount();
            }
        }

        [Test]
        public void TypedSlotConstructorRespectsRequiresContextFlag()
        {
            TypedSlot<ProbeMessage> contextless = new TypedSlot<ProbeMessage>(
                requiresContext: false
            );
            Assert.IsFalse(contextless.requiresContext);

            TypedSlot<ProbeMessage> withContext = new TypedSlot<ProbeMessage>(
                requiresContext: true
            );
            Assert.IsTrue(withContext.requiresContext);
        }

        [Test]
        public void TypedSlotIsEmptyWhenLiveCountZero()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            Assert.AreEqual(0, slot.liveCount);
            Assert.IsTrue(slot.IsEmpty);

            slot.liveCount = 1;
            Assert.IsFalse(slot.IsEmpty);

            slot.liveCount = 0;
            Assert.IsTrue(slot.IsEmpty);
        }

        [Test]
        public void TypedSlotResetBumpsVersionMonotonically()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            long previous = slot.version;
            for (int i = 0; i < 8; ++i)
            {
                slot.Reset();
                Assert.Greater(
                    slot.version,
                    previous,
                    "Reset() must bump version strictly monotonically (PLAN Risk R3)."
                );
                previous = slot.version;
            }
        }

        [Test]
        public void TypedSlotResetPreservesLastTouchTicks()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            slot.lastTouchTicks = 42;
            slot.Reset();
            Assert.AreEqual(
                42,
                slot.lastTouchTicks,
                "Reset() must preserve lastTouchTicks so the sweep can distinguish "
                    + "freshly-reset slots from never-touched slots."
            );
        }

        /// <summary>
        /// Pins the per-cache drain wired in P3.2: <see cref="TypedSlot{T}.Reset"/>
        /// must invoke <see cref="IHandlerActionCache.Reset"/> on every
        /// <see cref="IHandlerActionCache"/> held by
        /// <see cref="TypedSlot{T}.byPriority"/> BEFORE clearing the
        /// container. Drain order is load-bearing per PLAN Risk Register R3
        /// so that closures captured against the inner cache also detect
        /// invalidation. The earlier P3.1 placeholder pin asserted the
        /// inverse (<c>ResetCallCount == 0</c>) and was flipped here in
        /// lockstep with the wiring.
        /// </summary>
        [Test]
        public void TypedSlotResetDrainsHeldCachesViaIHandlerActionCache()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            StubCache child = new StubCache();
            slot.byPriority[0] = child;

            slot.Reset();

            Assert.AreEqual(
                1,
                child.ResetCallCount,
                "P3.2 wires Reset() to drain every IHandlerActionCache held by "
                    + "byPriority via IHandlerActionCache.Reset() BEFORE the structural "
                    + "clear (PLAN Risk Register R3). Re-check the xmldoc on "
                    + "TypedSlot<T>.Reset() if this assertion needs to change."
            );
        }

        /// <summary>
        /// Companion to <see cref="TypedSlotResetDrainsHeldCachesViaIHandlerActionCache"/>:
        /// pins that every inner cache held by
        /// <see cref="TypedSlot{T}.byContext"/> is also drained on
        /// <see cref="TypedSlot{T}.Reset"/>. Walks both axes of the flat
        /// 3-level <c>InstanceId -&gt; (priority -&gt; IHandlerActionCache)</c>
        /// shape committed in P3.2.
        /// </summary>
        [Test]
        public void TypedSlotResetDrainsByContextHeldCachesViaIHandlerActionCache()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: true);
            StubCache a = new StubCache();
            StubCache b = new StubCache();
            slot.byContext = new Dictionary<InstanceId, Dictionary<int, IHandlerActionCache>>
            {
                {
                    new InstanceId(1),
                    new Dictionary<int, IHandlerActionCache> { { 0, a } }
                },
                {
                    new InstanceId(2),
                    new Dictionary<int, IHandlerActionCache> { { 5, b } }
                },
            };

            slot.Reset();

            Assert.AreEqual(1, a.ResetCallCount);
            Assert.AreEqual(1, b.ResetCallCount);
        }

        /// <summary>
        /// Pins the drain-BEFORE-clear ordering on
        /// <see cref="TypedSlot{T}.byPriority"/> (PLAN Risk Register R3).
        /// The probe cache reads <c>byPriority.Count</c> at the moment its
        /// <see cref="IHandlerActionCache.Reset"/> fires; a value of 1
        /// proves the drain ran while the outer dict still held the entry.
        /// A value of 0 would indicate the outer clear ran first.
        /// </summary>
        [Test]
        public void TypedSlotResetDrainsBeforeClearingByPriority()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            OrderingProbeCache probe = new OrderingProbeCache();
            probe.ProbeOuterCount = () => slot.byPriority.Count;
            slot.byPriority[0] = probe;

            slot.Reset();

            Assert.AreEqual(
                1,
                probe.ObservedOuterCountAtReset,
                "Reset() must drain inner caches BEFORE clearing byPriority "
                    + "(PLAN Risk Register R3)."
            );
        }

        /// <summary>
        /// Companion to <see cref="TypedSlotResetDrainsBeforeClearingByPriority"/>:
        /// pins the drain-BEFORE-clear ordering on
        /// <see cref="TypedSlot{T}.byContext"/>. The probe reads
        /// <c>byContext.Count</c> from inside its
        /// <see cref="IHandlerActionCache.Reset"/>; a value of 1 proves the
        /// drain ran while the outer dict still held the entry.
        /// </summary>
        [Test]
        public void TypedSlotResetDrainsBeforeClearingByContext()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: true);
            OrderingProbeCache probe = new OrderingProbeCache();
            probe.ProbeOuterCount = () => slot.byContext.Count;
            slot.byContext = new Dictionary<InstanceId, Dictionary<int, IHandlerActionCache>>
            {
                {
                    new InstanceId(1),
                    new Dictionary<int, IHandlerActionCache> { { 0, probe } }
                },
            };

            slot.Reset();

            Assert.AreEqual(
                1,
                probe.ObservedOuterCountAtReset,
                "Reset() must drain inner caches BEFORE clearing byContext "
                    + "(PLAN Risk Register R3)."
            );
        }

        /// <summary>
        /// Pins that <c>MessageHandler.HandlerActionCache&lt;T&gt;</c> implements
        /// <see cref="IHandlerActionCache"/> after P3.2 (Task 1). The interface
        /// is implemented explicitly so the public-facing field shape on the
        /// nested cache type is unchanged; this test exercises the six
        /// interface members through an interface-typed reference to confirm
        /// they all dispatch without exception.
        /// </summary>
        [Test]
        public void HandlerActionCacheImplementsIHandlerActionCache()
        {
            System.Type nested = typeof(DxMessaging.Core.MessageHandler).GetNestedType(
                "HandlerActionCache`1",
                BindingFlags.NonPublic
            );
            Assert.IsNotNull(
                nested,
                "MessageHandler.HandlerActionCache<T> nested type must exist."
            );
            System.Type closed = nested.MakeGenericType(typeof(System.Action<int>));
            Assert.IsTrue(
                typeof(IHandlerActionCache).IsAssignableFrom(closed),
                "HandlerActionCache<T> must implement IHandlerActionCache after P3.2."
            );

            object instance = System.Activator.CreateInstance(closed, nonPublic: true);
            IHandlerActionCache view = (IHandlerActionCache)instance;

            // Exercise every interface member; failure indicates a misapplied
            // explicit-interface implementation or accidental shadowing.
            long _ = view.Version;
            view.LastSeenVersion = 7;
            Assert.AreEqual(7, view.LastSeenVersion);
            view.LastSeenEmissionId = 13;
            Assert.AreEqual(13, view.LastSeenEmissionId);
            int prefreeze = view.PrefreezeInvocationCount;
            Assert.AreEqual(0, prefreeze);
            Assert.IsTrue(
                view.IsEmpty,
                "Freshly-constructed HandlerActionCache<T> must report IsEmpty == true."
            );

            // Pre-populate entries + cache via reflection so Reset() has
            // observable inner state to drain. Both fields are public
            // readonly; reflection over the closed generic returns the same
            // collection instances the cache holds, so direct mutation
            // populates the cache.
            FieldInfo entriesField = closed.GetField(
                "entries",
                BindingFlags.Public | BindingFlags.Instance
            );
            FieldInfo cacheField = closed.GetField(
                "cache",
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.IsNotNull(entriesField, "HandlerActionCache<T>.entries must exist.");
            Assert.IsNotNull(cacheField, "HandlerActionCache<T>.cache must exist.");
            System.Collections.IDictionary entries = (System.Collections.IDictionary)
                entriesField.GetValue(instance);
            System.Collections.IList cacheList = (System.Collections.IList)
                cacheField.GetValue(instance);
            // Entry is a non-generic struct nested inside HandlerActionCache<T>;
            // GetNestedType on the closed generic returns the per-T concrete
            // Entry type directly (no further MakeGenericType needed).
            System.Type entryType = closed.GetNestedType("Entry", BindingFlags.NonPublic);
            Assert.IsNotNull(entryType, "HandlerActionCache<T>.Entry nested type must exist.");
            System.Action<int> handler = _ignored => { };
            object entry = System.Activator.CreateInstance(entryType, handler, 1);
            entries[handler] = entry;
            cacheList.Add(handler);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(1, cacheList.Count);
            Assert.IsFalse(
                view.IsEmpty,
                "After populating entries the cache must report IsEmpty == false."
            );

            long beforeReset = view.Version;
            view.Reset();
            Assert.Greater(
                view.Version,
                beforeReset,
                "Reset() must bump version monotonically (PLAN Risk Register R3)."
            );
            Assert.AreEqual(-1, view.LastSeenVersion, "Reset() must restore lastSeenVersion = -1.");
            Assert.AreEqual(0, view.LastSeenEmissionId);
            Assert.AreEqual(0, entries.Count, "Reset() must empty entries.");
            Assert.AreEqual(0, cacheList.Count, "Reset() must empty cache.");
            Assert.IsTrue(view.IsEmpty, "After Reset() the cache must report IsEmpty == true.");
        }

        /// <summary>
        /// Pins that <see cref="TypedSlot{T}.byContext"/> is the flat
        /// 3-level <c>Dictionary&lt;InstanceId, Dictionary&lt;int, IHandlerActionCache&gt;&gt;</c>
        /// shape committed in P3.2 (option (2) from the P3.1 enumeration).
        /// </summary>
        [Test]
        public void TypedSlotByContextShapeIsFlatThreeLevelDictionary()
        {
            FieldInfo field = typeof(TypedSlot<ProbeMessage>).GetField(
                "byContext",
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.IsNotNull(field, "TypedSlot<T>.byContext field must exist.");
            System.Type expected = typeof(Dictionary<
                InstanceId,
                Dictionary<int, IHandlerActionCache>
            >);
            Assert.AreEqual(
                expected,
                field.FieldType,
                "TypedSlot<T>.byContext must be the flat 3-level shape "
                    + "Dictionary<InstanceId, Dictionary<int, IHandlerActionCache>>."
            );
        }

        [Test]
        public void TypedSlotResetClearsByPriorityAndOrderedPriorities()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            slot.byPriority[0] = new StubCache();
            slot.byPriority[5] = new StubCache();
            slot.orderedPriorities.Add(0);
            slot.orderedPriorities.Add(5);

            slot.Reset();

            Assert.AreEqual(0, slot.byPriority.Count);
            Assert.AreEqual(0, slot.orderedPriorities.Count);
        }

        [Test]
        public void TypedSlotResetNullsOutByContext()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: true);
            slot.byContext = new Dictionary<InstanceId, Dictionary<int, IHandlerActionCache>>
            {
                {
                    new InstanceId(1),
                    new Dictionary<int, IHandlerActionCache> { { 0, new StubCache() } }
                },
                {
                    new InstanceId(2),
                    new Dictionary<int, IHandlerActionCache> { { 0, new StubCache() } }
                },
            };

            slot.Reset();

            Assert.IsNull(
                slot.byContext,
                "Reset() must null out byContext after returning typed-handler-side context "
                    + "dictionaries to DxPools."
            );
        }

        [Test]
        public void TypedSlotClearResetsVersionToZero()
        {
            TypedSlot<ProbeMessage> slot = new TypedSlot<ProbeMessage>(requiresContext: false);
            // Drive version above zero via repeated Reset() so the test does
            // not depend on internal field write access for setup.
            slot.Reset();
            slot.Reset();
            slot.Reset();
            Assert.Greater(slot.version, 0);

            slot.Clear();

            Assert.AreEqual(
                0,
                slot.version,
                "Clear() is the legacy 'full reset' semantic and must reset "
                    + "version to 0; eviction-driven monotonicity belongs to Reset()."
            );
            Assert.AreEqual(-1, slot.lastSeenVersion);
            Assert.AreEqual(0, slot.lastSeenEmissionId);
            Assert.AreEqual(0, slot.liveCount);
            Assert.AreEqual(0, slot.byPriority.Count);
            Assert.AreEqual(0, slot.orderedPriorities.Count);
            Assert.IsNull(slot.byContext);
        }

        [Test]
        public void TypedGlobalSlotResetBumpsVersionMonotonically()
        {
            TypedGlobalSlot slot = new TypedGlobalSlot();
            long previous = slot.version;
            for (int i = 0; i < 8; ++i)
            {
                slot.Reset();
                Assert.Greater(
                    slot.version,
                    previous,
                    "Reset() must bump version strictly monotonically (PLAN Risk R3)."
                );
                previous = slot.version;
            }
        }

        [Test]
        public void TypedGlobalSlotIsEmptyWhenLiveCountZero()
        {
            TypedGlobalSlot slot = new TypedGlobalSlot();
            Assert.AreEqual(0, slot.liveCount);
            Assert.IsTrue(slot.IsEmpty);

            slot.liveCount = 3;
            Assert.IsFalse(slot.IsEmpty);

            slot.liveCount = 0;
            Assert.IsTrue(slot.IsEmpty);
        }

        [Test]
        public void TypedGlobalSlotResetClearsCache()
        {
            TypedGlobalSlot slot = new TypedGlobalSlot();
            slot.cache = new StubCache();
            Assert.IsNotNull(slot.cache);

            slot.Reset();

            Assert.IsNull(slot.cache);
        }

        [Test]
        public void TypedGlobalSlotResetPreservesLastTouchTicks()
        {
            TypedGlobalSlot slot = new TypedGlobalSlot();
            slot.lastTouchTicks = 99;
            slot.Reset();
            Assert.AreEqual(99, slot.lastTouchTicks);
        }

        [Test]
        public void TypedGlobalSlotClearResetsVersionToZero()
        {
            TypedGlobalSlot slot = new TypedGlobalSlot();
            slot.Reset();
            slot.Reset();
            Assert.Greater(slot.version, 0);

            slot.Clear();

            Assert.AreEqual(0, slot.version);
            Assert.AreEqual(-1, slot.lastSeenVersion);
            Assert.AreEqual(0, slot.lastSeenEmissionId);
            Assert.AreEqual(0, slot.liveCount);
            Assert.IsNull(slot.cache);
        }

        [Test]
        public void TypedSlotImplementsIEvictableSlot()
        {
            Assert.IsTrue(
                typeof(IEvictableSlot).IsAssignableFrom(typeof(TypedSlot<ProbeMessage>)),
                "TypedSlot<T> must implement IEvictableSlot so the sweep can reclaim it."
            );
        }

        [Test]
        public void TypedGlobalSlotImplementsIEvictableSlot()
        {
            Assert.IsTrue(
                typeof(IEvictableSlot).IsAssignableFrom(typeof(TypedGlobalSlot)),
                "TypedGlobalSlot must implement IEvictableSlot so the sweep can reclaim it."
            );
        }

        /// <summary>
        /// Reflection-based shape pin for <see cref="IHandlerActionCache"/>.
        /// Asserts the interface declares exactly the six members the staged
        /// dispatch + eviction layers require: <see cref="IHandlerActionCache.Version"/>,
        /// <see cref="IHandlerActionCache.LastSeenVersion"/>,
        /// <see cref="IHandlerActionCache.LastSeenEmissionId"/>,
        /// <see cref="IHandlerActionCache.PrefreezeInvocationCount"/>,
        /// <see cref="IHandlerActionCache.IsEmpty"/>, and
        /// <see cref="IHandlerActionCache.Reset"/>. Adding or removing a
        /// member breaks this test until reviewers update the expected list,
        /// providing a structural backstop for P3.2 (where
        /// <c>HandlerActionCache&lt;TDelegate&gt;</c> retroactively implements
        /// the interface).
        /// </summary>
        [Test]
        public void IHandlerActionCacheInterfaceShape()
        {
            string[] expected =
            {
                "Version",
                "LastSeenVersion",
                "LastSeenEmissionId",
                "PrefreezeInvocationCount",
                "IsEmpty",
                "Reset",
            };

            // GetMembers on an interface reports declared members directly.
            // Property accessors and event accessors are filtered out by
            // selecting only properties + methods that are NOT special-name.
            string[] actual = typeof(IHandlerActionCache)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m =>
                    m.MemberType == MemberTypes.Property
                    || (m.MemberType == MemberTypes.Method && !((MethodInfo)m).IsSpecialName)
                )
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            string[] sortedExpected = expected.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            CollectionAssert.AreEqual(
                sortedExpected,
                actual,
                "IHandlerActionCache must expose exactly the documented member set. "
                    + "Adding or removing a member requires updating both the interface "
                    + "and this test in lockstep."
            );
        }
    }
}

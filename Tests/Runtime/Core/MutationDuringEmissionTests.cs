namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Tests that mutate registration state during emission and ensure snapshot semantics:
    /// - Current pass uses the listeners present when emission begins
    /// - Newly added listeners only run on subsequent emissions
    /// - Removing listeners during emission does not throw or cause message loss for the current pass
    /// </summary>
    public sealed class MutationDuringEmissionTests : MessagingTestBase
    {
        private const int ManyCount = 6; // Forces default iteration paths (>5)

        [UnityTest]
        public IEnumerator UntargetedAddLocalHandlerMany()
        {
            GameObject host = new(
                nameof(UntargetedAddLocalHandlerMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            // Register ManyCount handlers on a single MessageHandler to stress TypedHandler iteration
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                {
                    counts[idx]++;
                    if (!added && idx == 0)
                    {
                        added = true;
                        handles[ManyCount] = token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                            counts[ManyCount]++
                        );
                    }
                });
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            int expected = ManyCount;
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(expected, total, "All baseline handlers should run on first emission.");
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added handler must not run in the same emission."
            );

            msg.EmitUntargeted();
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                expected + ManyCount,
                total,
                "Baseline handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "New handler should run starting on the second emission."
            );

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] != default)
                {
                    token.RemoveRegistration(handles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedRemoveSelfMany()
        {
            GameObject host = new(
                nameof(UntargetedRemoveSelfMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                MessageRegistrationHandle h = default;
                h = token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                {
                    counts[idx]++;
                    token.RemoveRegistration(h);
                });
                handles[idx] = h;
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "Every baseline handler should run exactly once during the emission."
            );

            msg.EmitUntargeted();
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "No handler should run again after removing itself in the previous pass."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddHandlerAcrossHandlersMany()
        {
            // Many distinct MessageHandlers (bus-level list growth during iteration)
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"UntargetedBus_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle handle = listeners[i]
                    .token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                    {
                        counts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            GameObject extra = new(
                                "UntargetedBus_Extra",
                                typeof(EmptyMessageAwareComponent)
                            );
                            _spawned.Add(extra);
                            EmptyMessageAwareComponent extraComp =
                                extra.GetComponent<EmptyMessageAwareComponent>();
                            MessageRegistrationToken extraToken = GetToken(extraComp);
                            MessageRegistrationHandle extraHandle =
                                extraToken.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                                    counts[ManyCount]++
                                );
                            handles.Add((extraToken, extraHandle));
                        }
                    });
                handles.Add((listeners[i].token, handle));
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline handlers should run on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added MessageHandler must not run in the same emission."
            );

            msg.EmitUntargeted();
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "Newly added MessageHandler should run starting on the second emission."
            );

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddLocalHandlerMany()
        {
            GameObject host = new(
                nameof(TargetedAddLocalHandlerMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    host,
                    _ =>
                    {
                        counts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            handles[ManyCount] =
                                token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                                    host,
                                    _ => counts[ManyCount]++
                                );
                        }
                    }
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline targeted handlers should run on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added handler must not run in the same targeted emission."
            );

            msg.EmitGameObjectTargeted(host);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline targeted handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "New targeted handler should run starting on the second emission."
            );

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] != default)
                {
                    token.RemoveRegistration(handles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddLocalHandlerMany()
        {
            GameObject source = new(
                nameof(BroadcastAddLocalHandlerMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(source);
            EmptyMessageAwareComponent component =
                source.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                    source,
                    _ =>
                    {
                        counts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            handles[ManyCount] =
                                token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                                    source,
                                    _ => counts[ManyCount]++
                                );
                        }
                    }
                );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(source);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline broadcast handlers should run on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added broadcast handler must not run in the same emission."
            );

            msg.EmitGameObjectBroadcast(source);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline broadcast handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "New broadcast handler should run starting on the second emission."
            );

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] != default)
                {
                    token.RemoveRegistration(handles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingAddLocalHandlerMany()
        {
            GameObject host = new(
                nameof(TargetedWithoutTargetingAddLocalHandlerMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                    (_, _) =>
                    {
                        counts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            handles[ManyCount] =
                                token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                                    (_, _) => counts[ManyCount]++
                                );
                        }
                    }
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline targeted-without-targeting handlers should run on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added handler must not run in the same emission."
            );

            msg.EmitGameObjectTargeted(host);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "New handler should run starting on the second emission."
            );

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] != default)
                {
                    token.RemoveRegistration(handles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceAddLocalHandlerMany()
        {
            GameObject host = new(
                nameof(BroadcastWithoutSourceAddLocalHandlerMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                    (_, _) =>
                    {
                        counts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            handles[ManyCount] =
                                token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                                    (_, _) => counts[ManyCount]++
                                );
                        }
                    }
                );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(component);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline broadcast-without-source handlers should run on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "Newly added handler must not run in the same emission."
            );

            msg.EmitComponentBroadcast(component);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline handlers should run again on second emission."
            );
            Assert.AreEqual(
                1,
                counts[ManyCount],
                "New handler should run starting on the second emission."
            );

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i] != default)
                {
                    token.RemoveRegistration(handles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllAddDuringHandlerMany()
        {
            // Create several listeners that globally accept all; add one more during handling; ensure it runs next pass only
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"Global_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle h = listeners[i]
                    .token.RegisterGlobalAcceptAll(
                        _ => counts[idx]++,
                        (_, _) => counts[idx]++,
                        (_, _) => counts[idx]++
                    );
                handles.Add((listeners[i].token, h));
            }

            // Add a new global listener from inside a local untargeted handler on first pass
            MessageRegistrationHandle adderHandle = listeners[0]
                .token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                {
                    if (!added)
                    {
                        added = true;
                        GameObject extra = new("Global_Extra", typeof(EmptyMessageAwareComponent));
                        _spawned.Add(extra);
                        EmptyMessageAwareComponent extraComp =
                            extra.GetComponent<EmptyMessageAwareComponent>();
                        MessageRegistrationToken extraToken = GetToken(extraComp);
                        MessageRegistrationHandle globalHandle = extraToken.RegisterGlobalAcceptAll(
                            _ => counts[ManyCount]++,
                            (_, _) => counts[ManyCount]++,
                            (_, _) => counts[ManyCount]++
                        );
                        handles.Add((extraToken, globalHandle));
                    }
                });
            handles.Add((listeners[0].token, adderHandle));

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All global listeners should run once for the emitted category on first emission."
            );
            Assert.AreEqual(
                0,
                counts[ManyCount],
                "New global listener must not run in the same emission."
            );

            msg.EmitUntargeted();
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(
                ManyCount * 6,
                total,
                "Global listeners should run again on second emission."
            );
            Assert.AreEqual(
                3,
                counts[ManyCount],
                "New global listener should run on second emission for all categories."
            );

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddInterceptorDuringInterceptorDoesNotRunInSameEmission()
        {
            GameObject host = new("InterceptorHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int firstCount = 0;
            int secondCount = 0;
            MessageRegistrationHandle? second = null;

            MessageRegistrationHandle first = token.RegisterUntargetedInterceptor(
                (ref SimpleUntargetedMessage _) =>
                {
                    firstCount++;
                    if (second == null)
                    {
                        second = token.RegisterUntargetedInterceptor(
                            (ref SimpleUntargetedMessage __) =>
                            {
                                secondCount++;
                                return true;
                            }
                        );
                    }

                    return true;
                }
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(
                1,
                firstCount,
                "First interceptor should run exactly once in first emission."
            );
            Assert.AreEqual(0, secondCount, "New interceptor should not run in the same emission.");

            msg.EmitUntargeted();
            Assert.AreEqual(
                2,
                firstCount,
                "First interceptor should run again on second emission."
            );
            Assert.AreEqual(
                1,
                secondCount,
                "New interceptor should run starting on the second emission."
            );

            token.RemoveRegistration(first);
            if (second.HasValue)
            {
                token.RemoveRegistration(second.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(UntargetedAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] handlerCounts = new int[ManyCount];
            MessageRegistrationHandle[] handlerHandles = new MessageRegistrationHandle[ManyCount];
            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle ppHandle = default;
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handlerHandles[idx] = token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                {
                    handlerCounts[idx]++;
                    if (!added && idx == 0)
                    {
                        added = true;
                        ppHandle = token.RegisterUntargetedPostProcessor(
                            (ref SimpleUntargetedMessage _) => ppCounts[ManyCount]++
                        );
                    }
                });
            }

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                _ = token.RegisterUntargetedPostProcessor(
                    (ref SimpleUntargetedMessage _) => ppCounts[idx]++
                );
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            int handlerTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                handlerTotal += handlerCounts[i];
            Assert.AreEqual(
                ManyCount,
                handlerTotal,
                "All baseline handlers should run on first emission."
            );

            int ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(
                ManyCount,
                ppTotal,
                "All existing post-processors should run on first emission."
            );
            Assert.AreEqual(
                0,
                ppCounts[ManyCount],
                "Newly added post-processor must not run in the same emission."
            );

            msg.EmitUntargeted();
            ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(
                ManyCount * 2,
                ppTotal,
                "Baseline post-processors should run again on second emission."
            );
            Assert.AreEqual(
                1,
                ppCounts[ManyCount],
                "New post-processor should run starting on the second emission."
            );

            foreach (MessageRegistrationHandle h in handlerHandles)
            {
                token.RemoveRegistration(h);
            }
            if (ppHandle != default)
            {
                token.RemoveRegistration(ppHandle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(UntargetedAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle[] ppHandles = new MessageRegistrationHandle[ManyCount + 1];
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                ppHandles[idx] = token.RegisterUntargetedPostProcessor(
                    (ref SimpleUntargetedMessage _) =>
                    {
                        ppCounts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            ppHandles[ManyCount] = token.RegisterUntargetedPostProcessor(
                                (ref SimpleUntargetedMessage _) => ppCounts[ManyCount]++
                            );
                        }
                    }
                );
            }

            // Ensure there is at least one handler so post-processors will run
            MessageRegistrationHandle hdl = token.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) => { }
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(
                ManyCount,
                total,
                "All baseline post-processors should run on first emission."
            );
            Assert.AreEqual(
                0,
                ppCounts[ManyCount],
                "Newly added post-processor must not run in the same emission."
            );

            msg.EmitUntargeted();
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(
                ManyCount * 2,
                total,
                "Baseline post-processors should run again on second emission."
            );
            Assert.AreEqual(
                1,
                ppCounts[ManyCount],
                "New post-processor should run starting on the second emission."
            );

            token.RemoveRegistration(hdl);
            for (int i = 0; i < ppHandles.Length; i++)
            {
                if (ppHandles[i] != default)
                {
                    token.RemoveRegistration(ppHandles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddHandlerAcrossHandlersMany()
        {
            GameObject target = new("TargetedAddHandlerAcrossHandlersMany_Target");
            _spawned.Add(target);

            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"TargetedBus_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle handle = listeners[i]
                    .token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                        target,
                        _ =>
                        {
                            counts[idx]++;
                            if (!added && idx == 0)
                            {
                                added = true;
                                GameObject extra = new(
                                    "TargetedBus_Extra",
                                    typeof(EmptyMessageAwareComponent)
                                );
                                _spawned.Add(extra);
                                EmptyMessageAwareComponent extraComp =
                                    extra.GetComponent<EmptyMessageAwareComponent>();
                                MessageRegistrationToken extraToken = GetToken(extraComp);
                                MessageRegistrationHandle extraHandle =
                                    extraToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                                        target,
                                        _ => counts[ManyCount]++
                                    );
                                handles.Add((extraToken, extraHandle));
                            }
                        }
                    );
                handles.Add((listeners[i].token, handle));
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, counts[ManyCount]);

            msg.EmitGameObjectTargeted(target);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, counts[ManyCount]);

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddHandlerAcrossHandlersMany()
        {
            GameObject source = new(nameof(BroadcastAddHandlerAcrossHandlersMany));
            _spawned.Add(source);

            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"BroadcastBus_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle handle = listeners[i]
                    .token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                        source,
                        _ =>
                        {
                            counts[idx]++;
                            if (!added && idx == 0)
                            {
                                added = true;
                                GameObject extra = new(
                                    "BroadcastBus_Extra",
                                    typeof(EmptyMessageAwareComponent)
                                );
                                _spawned.Add(extra);
                                EmptyMessageAwareComponent extraComp =
                                    extra.GetComponent<EmptyMessageAwareComponent>();
                                MessageRegistrationToken extraToken = GetToken(extraComp);
                                MessageRegistrationHandle extraHandle =
                                    extraToken.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                                        source,
                                        _ => counts[ManyCount]++
                                    );
                                handles.Add((extraToken, extraHandle));
                            }
                        }
                    );
                handles.Add((listeners[i].token, handle));
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(source);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, counts[ManyCount]);

            msg.EmitGameObjectBroadcast(source);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, counts[ManyCount]);

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingAddHandlerAcrossHandlersMany()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"TWTBus_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            GameObject target = new("TWT_Target");
            _spawned.Add(target);

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle handle = listeners[i]
                    .token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                        (_, _) =>
                        {
                            counts[idx]++;
                            if (!added && idx == 0)
                            {
                                added = true;
                                GameObject extra = new(
                                    "TWTBus_Extra",
                                    typeof(EmptyMessageAwareComponent)
                                );
                                _spawned.Add(extra);
                                EmptyMessageAwareComponent extraComp =
                                    extra.GetComponent<EmptyMessageAwareComponent>();
                                MessageRegistrationToken extraToken = GetToken(extraComp);
                                MessageRegistrationHandle extraHandle =
                                    extraToken.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                                        (_, _) => counts[ManyCount]++
                                    );
                                handles.Add((extraToken, extraHandle));
                            }
                        }
                    );
                handles.Add((listeners[i].token, handle));
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, counts[ManyCount]);

            msg.EmitGameObjectTargeted(target);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, counts[ManyCount]);

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceAddHandlerAcrossHandlersMany()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"BWOBus_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            int[] counts = new int[ManyCount + 1];
            List<(MessageRegistrationToken token, MessageRegistrationHandle handle)> handles =
                new();
            bool added = false;

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                MessageRegistrationHandle handle = listeners[i]
                    .token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                        (_, _) =>
                        {
                            counts[idx]++;
                            if (!added && idx == 0)
                            {
                                added = true;
                                GameObject extra = new(
                                    "BWOBus_Extra",
                                    typeof(EmptyMessageAwareComponent)
                                );
                                _spawned.Add(extra);
                                EmptyMessageAwareComponent extraComp =
                                    extra.GetComponent<EmptyMessageAwareComponent>();
                                MessageRegistrationToken extraToken = GetToken(extraComp);
                                MessageRegistrationHandle extraHandle =
                                    extraToken.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                                        (_, _) => counts[ManyCount]++
                                    );
                                handles.Add((extraToken, extraHandle));
                            }
                        }
                    );
                handles.Add((listeners[i].token, handle));
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(listeners[0].comp);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, counts[ManyCount]);

            msg.EmitComponentBroadcast(listeners[0].comp);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += counts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, counts[ManyCount]);

            foreach (
                (MessageRegistrationToken token, MessageRegistrationHandle handle) entry in handles
            )
            {
                entry.token.RemoveRegistration(entry.handle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingRemoveOtherAcrossHandlersDuringEmissionMany()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"TWTBusRem_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];
            int[] counts = new int[ManyCount];

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                handles[idx] = listeners[i]
                    .token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                        (_, _) =>
                        {
                            counts[idx]++;
                            if (idx == 0)
                            {
                                listeners[1].token.RemoveRegistration(handles[1]);
                            }
                        }
                    );
            }

            GameObject target = new("TWT_Target_Rem");
            _spawned.Add(target);
            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(target);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectTargeted(target);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                listeners[i].token.RemoveRegistration(handles[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceRemoveOtherAcrossHandlersDuringEmissionMany()
        {
            List<(EmptyMessageAwareComponent comp, MessageRegistrationToken token)> listeners =
                new();
            for (int i = 0; i < ManyCount; i++)
            {
                GameObject go = new($"BWOBusRem_{i}", typeof(EmptyMessageAwareComponent));
                _spawned.Add(go);
                EmptyMessageAwareComponent c = go.GetComponent<EmptyMessageAwareComponent>();
                listeners.Add((c, GetToken(c)));
            }

            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];
            int[] counts = new int[ManyCount];

            for (int i = 0; i < listeners.Count; i++)
            {
                int idx = i;
                handles[idx] = listeners[i]
                    .token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                        (_, _) =>
                        {
                            counts[idx]++;
                            if (idx == 0)
                            {
                                listeners[1].token.RemoveRegistration(handles[1]);
                            }
                        }
                    );
            }

            // Emit from any component
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(listeners[0].comp);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitComponentBroadcast(listeners[0].comp);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                listeners[i].token.RemoveRegistration(handles[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedPostProcessorRemoveOtherDuringPostProcessingMany()
        {
            GameObject host = new("U_PP_Remove", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            // Ensure processing stage reached
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(_ => { });

            MessageRegistrationHandle[] pp = new MessageRegistrationHandle[ManyCount];
            int[] counts = new int[ManyCount];
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                pp[idx] = token.RegisterUntargetedPostProcessor(
                    (ref SimpleUntargetedMessage _) =>
                    {
                        counts[idx]++;
                        if (idx == 0)
                        {
                            token.RemoveRegistration(pp[1]);
                        }
                    }
                );
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitUntargeted();
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < pp.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(pp[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedPostProcessorRemoveOtherDuringPostProcessingMany()
        {
            GameObject host = new("T_PP_Remove", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            // Ensure processing stage reached
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(host, _ => { });

            MessageRegistrationHandle[] pp = new MessageRegistrationHandle[ManyCount];
            int[] counts = new int[ManyCount];
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                pp[idx] = token.RegisterGameObjectTargetedPostProcessor(
                    host,
                    (ref SimpleTargetedMessage _) =>
                    {
                        counts[idx]++;
                        if (idx == 0)
                        {
                            token.RemoveRegistration(pp[1]);
                        }
                    }
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < pp.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(pp[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastPostProcessorRemoveOtherDuringPostProcessingMany()
        {
            GameObject host = new("B_PP_Remove", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent comp = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            // Ensure processing stage reached
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(host, _ => { });

            MessageRegistrationHandle[] pp = new MessageRegistrationHandle[ManyCount];
            int[] counts = new int[ManyCount];
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                pp[idx] = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                    host,
                    _ =>
                    {
                        counts[idx]++;
                        if (idx == 0)
                        {
                            token.RemoveRegistration(pp[1]);
                        }
                    }
                );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < pp.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(pp[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedRemoveOtherLocalHandlerDuringEmissionMany()
        {
            GameObject host = new(
                nameof(UntargetedRemoveOtherLocalHandlerDuringEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                {
                    counts[idx]++;
                    if (idx == 0)
                    {
                        token.RemoveRegistration(handles[1]);
                    }
                });
            }

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitUntargeted();
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(handles[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedRemoveOtherLocalHandlerDuringEmissionMany()
        {
            GameObject host = new(
                nameof(TargetedRemoveOtherLocalHandlerDuringEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    host,
                    _ =>
                    {
                        counts[idx]++;
                        if (idx == 0)
                        {
                            token.RemoveRegistration(handles[1]);
                        }
                    }
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(handles[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastRemoveOtherLocalHandlerDuringEmissionMany()
        {
            GameObject host = new(
                nameof(BroadcastRemoveOtherLocalHandlerDuringEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] counts = new int[ManyCount];
            MessageRegistrationHandle[] handles = new MessageRegistrationHandle[ManyCount];

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handles[idx] = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                    host,
                    _ =>
                    {
                        counts[idx]++;
                        if (idx == 0)
                        {
                            token.RemoveRegistration(handles[1]);
                        }
                    }
                );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, counts[0]);
            Assert.AreEqual(1, counts[1]);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);

            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1)
                {
                    continue;
                }
                token.RemoveRegistration(handles[i]);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddInterceptorDuringInterceptorDoesNotRunInSameEmission()
        {
            GameObject host = new("TargetedInterceptorHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int firstCount = 0;
            int secondCount = 0;
            MessageRegistrationHandle? second = null;

            MessageRegistrationHandle first = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage __) =>
                {
                    firstCount++;
                    if (second == null)
                    {
                        second = token.RegisterTargetedInterceptor(
                            (ref InstanceId __1, ref SimpleTargetedMessage __2) =>
                            {
                                secondCount++;
                                return true;
                            }
                        );
                    }
                    return true;
                }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(1, firstCount);
            Assert.AreEqual(0, secondCount);

            msg.EmitGameObjectTargeted(host);
            Assert.AreEqual(2, firstCount);
            Assert.AreEqual(1, secondCount);

            token.RemoveRegistration(first);
            if (second.HasValue)
            {
                token.RemoveRegistration(second.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddInterceptorDuringInterceptorDoesNotRunInSameEmission()
        {
            GameObject host = new("BroadcastInterceptorHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int firstCount = 0;
            int secondCount = 0;
            MessageRegistrationHandle? second = null;

            MessageRegistrationHandle first = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                {
                    firstCount++;
                    if (second == null)
                    {
                        second = token.RegisterBroadcastInterceptor(
                            (ref InstanceId __1, ref SimpleBroadcastMessage __2) =>
                            {
                                secondCount++;
                                return true;
                            }
                        );
                    }
                    return true;
                }
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(1, firstCount);
            Assert.AreEqual(0, secondCount);

            msg.EmitGameObjectBroadcast(host);
            Assert.AreEqual(2, firstCount);
            Assert.AreEqual(1, secondCount);

            token.RemoveRegistration(first);
            if (second.HasValue)
            {
                token.RemoveRegistration(second.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddSameDelegateDuringEmissionDoesNotDuplicateInvocation()
        {
            GameObject host = new("SameDelegateHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int count = 0;
            MessageRegistrationHandle firstHandle = default;
            MessageRegistrationHandle? secondHandle = null;

            void Local(SimpleUntargetedMessage _)
            {
                count++;
                if (secondHandle == null)
                {
                    secondHandle = token.RegisterUntargeted<SimpleUntargetedMessage>(Local);
                }
            }

            firstHandle = token.RegisterUntargeted<SimpleUntargetedMessage>(Local);

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(1, count);

            msg.EmitUntargeted();
            Assert.AreEqual(2, count);

            token.RemoveRegistration(firstHandle);
            if (secondHandle.HasValue)
            {
                token.RemoveRegistration(secondHandle.Value);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedAddLowerPriorityDuringEmissionRespectsNextEmissionOrder()
        {
            GameObject host = new("PriorityHost", typeof(EmptyMessageAwareComponent));
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<int> order = new();
            MessageRegistrationHandle lowHandle = default;
            bool added = false;

            MessageRegistrationHandle highHandle =
                token.RegisterUntargeted<SimpleUntargetedMessage>(
                    _ =>
                    {
                        order.Add(1);
                        if (!added)
                        {
                            added = true;
                            lowHandle = token.RegisterUntargeted<SimpleUntargetedMessage>(
                                _ => order.Add(0),
                                priority: 0
                            );
                        }
                    },
                    priority: 1
                );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            CollectionAssert.AreEqual(new[] { 1 }, order);

            order.Clear();
            msg.EmitUntargeted();
            CollectionAssert.AreEqual(new[] { 0, 1 }, order);

            token.RemoveRegistration(highHandle);
            if (lowHandle != default)
            {
                token.RemoveRegistration(lowHandle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(TargetedAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] handlerCounts = new int[ManyCount];
            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handlerHandles = new MessageRegistrationHandle[ManyCount];
            MessageRegistrationHandle ppHandle = default;
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handlerHandles[idx] = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    host,
                    _ =>
                    {
                        handlerCounts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            ppHandle = token.RegisterGameObjectTargetedPostProcessor(
                                host,
                                (ref SimpleTargetedMessage _) => ppCounts[ManyCount]++
                            );
                        }
                    }
                );
                _ = token.RegisterGameObjectTargetedPostProcessor(
                    host,
                    (ref SimpleTargetedMessage _) => ppCounts[idx]++
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);

            int handlerTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                handlerTotal += handlerCounts[i];
            Assert.AreEqual(ManyCount, handlerTotal);

            int ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(ManyCount, ppTotal);
            Assert.AreEqual(0, ppCounts[ManyCount]);

            msg.EmitGameObjectTargeted(host);
            ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(ManyCount * 2, ppTotal);
            Assert.AreEqual(1, ppCounts[ManyCount]);

            foreach (MessageRegistrationHandle h in handlerHandles)
            {
                token.RemoveRegistration(h);
            }
            if (ppHandle != default)
            {
                token.RemoveRegistration(ppHandle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(TargetedAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle[] ppHandles = new MessageRegistrationHandle[ManyCount + 1];

            // Ensure there is a handler so post processing will run
            MessageRegistrationHandle hdl = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                host,
                _ => { }
            );

            bool added = false;
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                ppHandles[idx] = token.RegisterGameObjectTargetedPostProcessor(
                    host,
                    (ref SimpleTargetedMessage _) =>
                    {
                        ppCounts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            ppHandles[ManyCount] = token.RegisterGameObjectTargetedPostProcessor(
                                host,
                                (ref SimpleTargetedMessage __) => ppCounts[ManyCount]++
                            );
                        }
                    }
                );
            }

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(host);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, ppCounts[ManyCount]);

            msg.EmitGameObjectTargeted(host);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, ppCounts[ManyCount]);

            token.RemoveRegistration(hdl);
            for (int i = 0; i < ppHandles.Length; i++)
            {
                if (ppHandles[i] != default)
                {
                    token.RemoveRegistration(ppHandles[i]);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(BroadcastAddPostProcessorDuringHandlerDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] handlerCounts = new int[ManyCount];
            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle[] handlerHandles = new MessageRegistrationHandle[ManyCount];
            MessageRegistrationHandle ppHandle = default;
            bool added = false;

            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                handlerHandles[idx] = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                    host,
                    _ =>
                    {
                        handlerCounts[idx]++;
                        if (!added && idx == 0)
                        {
                            added = true;
                            ppHandle =
                                token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                                    host,
                                    _ => ppCounts[ManyCount]++
                                );
                        }
                    }
                );
                _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                    host,
                    _ => ppCounts[idx]++
                );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);

            int handlerTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                handlerTotal += handlerCounts[i];
            Assert.AreEqual(ManyCount, handlerTotal);

            int ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(ManyCount, ppTotal);
            Assert.AreEqual(0, ppCounts[ManyCount]);

            msg.EmitGameObjectBroadcast(host);
            ppTotal = 0;
            for (int i = 0; i < ManyCount; i++)
                ppTotal += ppCounts[i];
            Assert.AreEqual(ManyCount * 2, ppTotal);
            Assert.AreEqual(1, ppCounts[ManyCount]);

            foreach (MessageRegistrationHandle h in handlerHandles)
            {
                token.RemoveRegistration(h);
            }
            if (ppHandle != default)
            {
                token.RemoveRegistration(ppHandle);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany()
        {
            GameObject host = new(
                nameof(BroadcastAddPostProcessorDuringPostProcessorDoesNotRunInSameEmissionMany),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(host);
            EmptyMessageAwareComponent component = host.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            int[] ppCounts = new int[ManyCount + 1];
            MessageRegistrationHandle[] ppHandles = new MessageRegistrationHandle[ManyCount + 1];

            // Ensure at least one handler exists so post-processing runs
            MessageRegistrationHandle hdl =
                token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(host, _ => { });

            bool added = false;
            for (int i = 0; i < ManyCount; i++)
            {
                int idx = i;
                ppHandles[idx] =
                    token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                        host,
                        _ =>
                        {
                            ppCounts[idx]++;
                            if (!added && idx == 0)
                            {
                                added = true;
                                ppHandles[ManyCount] =
                                    token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                                        host,
                                        _ => ppCounts[ManyCount]++
                                    );
                            }
                        }
                    );
            }

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(host);
            int total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(ManyCount, total);
            Assert.AreEqual(0, ppCounts[ManyCount]);

            msg.EmitGameObjectBroadcast(host);
            total = 0;
            for (int i = 0; i < ManyCount; i++)
                total += ppCounts[i];
            Assert.AreEqual(ManyCount * 2, total);
            Assert.AreEqual(1, ppCounts[ManyCount]);

            token.RemoveRegistration(hdl);
            for (int i = 0; i < ppHandles.Length; i++)
            {
                if (ppHandles[i] != default)
                {
                    token.RemoveRegistration(ppHandles[i]);
                }
            }
            yield break;
        }
    }
}

namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class OrderingTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator UntargetedMixedFastThenActions()
        {
            GameObject go = new(
                nameof(UntargetedMixedFastThenActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => order.Add("F1"), 0);
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add("A1"), 0);
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add("A2"), 0);
            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator PipelineOrderingUntargeted()
        {
            GameObject go = new(
                nameof(PipelineOrderingUntargeted),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> stages = new();

            // Interceptors at different priorities
            _ = token.RegisterUntargetedInterceptor(
                (ref SimpleUntargetedMessage _) =>
                {
                    stages.Add("I0");
                    return true;
                },
                priority: 0
            );
            _ = token.RegisterUntargetedInterceptor(
                (ref SimpleUntargetedMessage _) =>
                {
                    stages.Add("I1");
                    return true;
                },
                priority: 1
            );

            // Global accept-all (untargeted only)
            _ = token.RegisterGlobalAcceptAll(_ => stages.Add("G"), (_, _) => { }, (_, _) => { });

            // Type handler
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => stages.Add("H"));

            // Post-processor
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => stages.Add("P")
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();

            Assert.AreEqual(
                new[] { "I0", "I1", "G", "H", "P" },
                stages.ToArray(),
                "Untargeted pipeline must be Interceptors -> Global -> Handlers -> Post-Processors."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PipelineOrderingTargetedGameObject()
        {
            GameObject go = new(
                nameof(PipelineOrderingTargetedGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            InstanceId target = go;

            List<string> stages = new();

            _ = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    stages.Add("I0");
                    return true;
                },
                0
            );
            _ = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    stages.Add("I1");
                    return true;
                },
                1
            );

            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => stages.Add("G"), (_, _) => { });

            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => stages.Add("H")
            );
            _ = token.RegisterGameObjectTargetedPostProcessor(
                go,
                (ref SimpleTargetedMessage _) => stages.Add("P")
            );

            SimpleTargetedMessage msg = new();
            msg.EmitTargeted(target);
            Assert.AreEqual(
                new[] { "I0", "I1", "G", "H", "P" },
                stages.ToArray(),
                "Targeted pipeline must be Interceptors -> Global -> Handlers -> Post-Processors."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PipelineOrderingBroadcastGameObject()
        {
            GameObject go = new(
                nameof(PipelineOrderingBroadcastGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> stages = new();

            _ = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    stages.Add("I0");
                    return true;
                },
                0
            );
            _ = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    stages.Add("I1");
                    return true;
                },
                1
            );

            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => { }, (_, _) => stages.Add("G"));

            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => stages.Add("H")
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor(
                go,
                (ref SimpleBroadcastMessage _) => stages.Add("P")
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { "I0", "I1", "G", "H", "P" },
                stages.ToArray(),
                "Broadcast pipeline must be Interceptors -> Global -> Handlers -> Post-Processors."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PostProcessorTargetedSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(PostProcessorTargetedSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectTargetedPostProcessor(
                go,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectTargetedPostProcessor(
                go,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectTargetedPostProcessor(
                go,
                (ref SimpleTargetedMessage _) => order.Add(3),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Targeted post-processors at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PostProcessorBroadcastSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(PostProcessorBroadcastSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectBroadcastPostProcessor(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Broadcast post-processors at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingHandlersSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingHandlersSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(3),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "TargetedWithoutTargeting handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingPostProcessorsSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(3),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "TargetedWithoutTargeting post-processors at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceHandlersSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourceHandlersSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "BroadcastWithoutSource handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourcePostProcessorsSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "BroadcastWithoutSource post-processors at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllUntargetedFastBeforeActions()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllUntargetedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            // Register action first, then fast — fast should still run first
            _ = token.RegisterGlobalAcceptAll(_ => order.Add("A"), (_, _) => { }, (_, _) => { });
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F"),
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(
                new[] { "F", "A" },
                order.ToArray(),
                "GlobalAcceptAll (Untargeted) should run fast handlers before action handlers at the same logical step."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllTargetedFastBeforeActions()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllTargetedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => order.Add("A"), (_, _) => { });
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => order.Add("F"),
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { "F", "A" },
                order.ToArray(),
                "GlobalAcceptAll (Targeted) should run fast handlers before action handlers at the same logical step."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllBroadcastFastBeforeActions()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllBroadcastFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => { }, (_, _) => order.Add("A"));
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => order.Add("F")
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { "F", "A" },
                order.ToArray(),
                "GlobalAcceptAll (Broadcast) should run fast handlers before action handlers at the same logical step."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingMixedFastBeforeActions()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingMixedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            // Register action then fast — fast should still be invoked first within the group
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add("F"),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceMixedFastBeforeActions()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourceMixedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add("F"),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator PipelineOrderingTargetedWithWithoutTargetingHandlersAndPostProcessors()
        {
            GameObject go = new(
                nameof(PipelineOrderingTargetedWithWithoutTargetingHandlersAndPostProcessors),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> stages = new();

            _ = token.RegisterTargetedInterceptor(
                (ref InstanceId _, ref SimpleTargetedMessage _) =>
                {
                    stages.Add("I");
                    return true;
                },
                0
            );
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => stages.Add("G"), (_, _) => { });
            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => stages.Add("Hspec")
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => stages.Add("Hall")
            );
            _ = token.RegisterGameObjectTargetedPostProcessor(
                go,
                (ref SimpleTargetedMessage _) => stages.Add("Pspec")
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => stages.Add("Pall")
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { "I", "G", "Hspec", "Hall", "Pspec", "Pall" },
                stages.ToArray(),
                "Targeted pipeline must include WithoutTargeting handlers between specific handlers and post-processors."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PipelineOrderingBroadcastWithWithoutSourceHandlersAndPostProcessors()
        {
            GameObject go = new(
                nameof(PipelineOrderingBroadcastWithWithoutSourceHandlersAndPostProcessors),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> stages = new();
            _ = token.RegisterBroadcastInterceptor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) =>
                {
                    stages.Add("I");
                    return true;
                },
                0
            );
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => { }, (_, _) => stages.Add("G"));
            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => stages.Add("Hspec")
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => stages.Add("Hall")
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor(
                go,
                (ref SimpleBroadcastMessage _) => stages.Add("Pspec")
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => stages.Add("Pall")
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { "I", "G", "Hspec", "Hall", "Pspec", "Pall" },
                stages.ToArray(),
                "Broadcast pipeline must include WithoutSource handlers between specific handlers and post-processors."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedSamePriorityActionsInRegistrationOrder()
        {
            GameObject go = new(
                nameof(UntargetedSamePriorityActionsInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add(1), 0);
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add(2), 0);
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add(3), 0);

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Untargeted action handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedSamePriorityFastInRegistrationOrder()
        {
            GameObject go = new(
                nameof(UntargetedSamePriorityFastInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => order.Add(1), 0);
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => order.Add(2), 0);
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => order.Add(3), 0);

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Untargeted fast handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedSamePriorityMixedFastBeforeActions()
        {
            GameObject go = new(
                nameof(UntargetedSamePriorityMixedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            // Register an action handler first (by-value)
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add("A1"), 0);
            // Then a fast handler (by-ref)
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => order.Add("F1"), 0);
            // Another action handler (by-value)
            _ = token.RegisterUntargeted((SimpleUntargetedMessage _) => order.Add("A2"), 0);

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            // Fast handlers run before action handlers at the same priority; within each group, registration order is preserved
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedSamePriorityActionsGameObjectInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedSamePriorityActionsGameObjectInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectTargeted(go, (SimpleTargetedMessage _) => order.Add(1), 0);
            _ = token.RegisterGameObjectTargeted(go, (SimpleTargetedMessage _) => order.Add(2), 0);
            _ = token.RegisterGameObjectTargeted(go, (SimpleTargetedMessage _) => order.Add(3), 0);

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Targeted action handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsFastOnlySamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourcePostProcessorsFastOnlySamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsFastOnlySamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(
                    TargetedWithoutTargetingPostProcessorsFastOnlySamePriorityInRegistrationOrder
                ),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(3),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedSamePriorityFastGameObjectInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedSamePriorityFastGameObjectInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => order.Add(3),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Targeted fast handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastSamePriorityActionsGameObjectInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastSamePriorityActionsGameObjectInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectBroadcast(
                go,
                (SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Broadcast action handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastSamePriorityFastGameObjectInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastSamePriorityFastGameObjectInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(3),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Broadcast fast handlers at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator PostProcessorUntargetedSamePriorityInRegistrationOrder()
        {
            GameObject go = new(
                nameof(PostProcessorUntargetedSamePriorityInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<int> order = new();
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => order.Add(3),
                0
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(
                new[] { 1, 2, 3 },
                order.ToArray(),
                "Untargeted post-processors at same priority should run by registration order."
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator DisposableRemovesRegistration()
        {
            GameObject go = new(
                nameof(DisposableRemovesRegistration),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            int count = 0;
            MessageRegistrationHandle handle = token.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) => ++count
            );
            using (token.AsDisposable(handle))
            {
                SimpleUntargetedMessage msg = new();
                msg.EmitUntargeted();
                Assert.AreEqual(1, count);
            }

            SimpleUntargetedMessage msg2 = new();
            msg2.EmitUntargeted();
            Assert.AreEqual(1, count, "Disposable should remove the registration when disposed.");
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedMixedFastBeforeActionsGameObject()
        {
            GameObject go = new(
                nameof(TargetedMixedFastBeforeActionsGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGameObjectTargeted(
                go,
                (SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterGameObjectTargeted(
                go,
                (ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterGameObjectTargeted(
                go,
                (SimpleTargetedMessage _) => order.Add("A2"),
                0
            );

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastMixedFastBeforeActionsGameObject()
        {
            GameObject go = new(
                nameof(BroadcastMixedFastBeforeActionsGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGameObjectBroadcast(
                go,
                (SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterGameObjectBroadcast(
                go,
                (SimpleBroadcastMessage _) => order.Add("A2"),
                0
            );

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedSamePriorityActionsComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedSamePriorityActionsComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentTargeted(comp, (SimpleTargetedMessage _) => order.Add(1), 0);
            _ = token.RegisterComponentTargeted(comp, (SimpleTargetedMessage _) => order.Add(2), 0);
            _ = token.RegisterComponentTargeted(comp, (SimpleTargetedMessage _) => order.Add(3), 0);
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedSamePriorityFastComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedSamePriorityFastComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentTargeted(
                comp,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (ref SimpleTargetedMessage _) => order.Add(3),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedMixedFastBeforeActionsComponent()
        {
            GameObject go = new(
                nameof(TargetedMixedFastBeforeActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentTargeted(
                comp,
                (SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (SimpleTargetedMessage _) => order.Add("A2"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastSamePriorityActionsComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastSamePriorityActionsComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add(3),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastSamePriorityFastComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastSamePriorityFastComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentBroadcast(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add(3),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastMixedFastBeforeActionsComponent()
        {
            GameObject go = new(
                nameof(BroadcastMixedFastBeforeActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add("A2"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingHandlersSamePriorityComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingHandlersSamePriorityComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(3),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingMixedFastBeforeActionsComponent()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingMixedFastBeforeActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A2"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsSamePriorityComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(
                    TargetedWithoutTargetingPostProcessorsSamePriorityComponentInRegistrationOrder
                ),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add(3),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsFastOnlyComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingPostProcessorsFastOnlyComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add(3),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceHandlersSamePriorityComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourceHandlersSamePriorityComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(3),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceMixedFastBeforeActionsComponent()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourceMixedFastBeforeActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A2"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsSamePriorityComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(
                    BroadcastWithoutSourcePostProcessorsSamePriorityComponentInRegistrationOrder
                ),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add(3),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsFastOnlyComponentInRegistrationOrder()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourcePostProcessorsFastOnlyComponentInRegistrationOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add(3),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { 1, 2, 3 }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator OnlyGlobalAcceptAllUntargetedInvoked()
        {
            GameObject go = new(
                nameof(OnlyGlobalAcceptAllUntargetedInvoked),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                _ => ++gUntargeted,
                (_, _) => ++gTargeted,
                (_, _) => ++gBroadcast
            );
            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(1, gUntargeted);
            Assert.AreEqual(0, gTargeted);
            Assert.AreEqual(0, gBroadcast);
            yield break;
        }

        [UnityTest]
        public IEnumerator OnlyGlobalAcceptAllTargetedInvoked()
        {
            GameObject go = new(
                nameof(OnlyGlobalAcceptAllTargetedInvoked),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                _ => ++gUntargeted,
                (_, _) => ++gTargeted,
                (_, _) => ++gBroadcast
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(0, gUntargeted);
            Assert.AreEqual(1, gTargeted);
            Assert.AreEqual(0, gBroadcast);
            yield break;
        }

        [UnityTest]
        public IEnumerator OnlyGlobalAcceptAllBroadcastInvoked()
        {
            GameObject go = new(
                nameof(OnlyGlobalAcceptAllBroadcastInvoked),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                _ => ++gUntargeted,
                (_, _) => ++gTargeted,
                (_, _) => ++gBroadcast
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(0, gUntargeted);
            Assert.AreEqual(0, gTargeted);
            Assert.AreEqual(1, gBroadcast);
            yield break;
        }

        [UnityTest]
        public IEnumerator NoRegistrationsNoInvocation()
        {
            GameObject go = new(
                nameof(NoRegistrationsNoInvocation),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            // No explicit registrations
            SimpleUntargetedMessage msg1 = new();
            msg1.EmitUntargeted();
            SimpleTargetedMessage msg2 = new();
            msg2.EmitComponentTargeted(comp);
            SimpleBroadcastMessage msg3 = new();
            msg3.EmitComponentBroadcast(comp);
            Assert.Pass();
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllUntargetedMultipleFastAndActionOrder()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllUntargetedMultipleFastAndActionOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            // Fast group (F1, F2)
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F1"),
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F2"),
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );
            // Action group (A1, A2)
            _ = token.RegisterGlobalAcceptAll(_ => order.Add("A1"), (_, _) => { }, (_, _) => { });
            _ = token.RegisterGlobalAcceptAll(_ => order.Add("A2"), (_, _) => { }, (_, _) => { });

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(new[] { "F1", "F2", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllTargetedMultipleFastAndActionOrder()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllTargetedMultipleFastAndActionOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => order.Add("F1"),
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => order.Add("F2"),
                (ref InstanceId _, ref IBroadcastMessage _) => { }
            );
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => order.Add("A1"), (_, _) => { });
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => order.Add("A2"), (_, _) => { });

            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(new[] { "F1", "F2", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator GlobalAcceptAllBroadcastMultipleFastAndActionOrder()
        {
            GameObject go = new(
                nameof(GlobalAcceptAllBroadcastMultipleFastAndActionOrder),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => order.Add("F1")
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage _) => { },
                (ref InstanceId _, ref IBroadcastMessage _) => order.Add("F2")
            );
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => { }, (_, _) => order.Add("A1"));
            _ = token.RegisterGlobalAcceptAll(_ => { }, (_, _) => { }, (_, _) => order.Add("A2"));

            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(new[] { "F1", "F2", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator UntargetedTwoPrioritiesFastBeforeActionWithinEachPriority()
        {
            GameObject go = new(
                nameof(UntargetedTwoPrioritiesFastBeforeActionWithinEachPriority),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();

            // Priority 0: fast then action
            _ = token.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) => order.Add("F0"),
                priority: 0
            );
            _ = token.RegisterUntargeted(
                (SimpleUntargetedMessage _) => order.Add("A0"),
                priority: 0
            );
            // Priority 1: fast then action
            _ = token.RegisterUntargeted(
                (ref SimpleUntargetedMessage _) => order.Add("F1"),
                priority: 1
            );
            _ = token.RegisterUntargeted(
                (SimpleUntargetedMessage _) => order.Add("A1"),
                priority: 1
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(new[] { "F0", "A0", "F1", "A1" }, order.ToArray());
            yield break;
        }

        // Mixed tests for GameObject "without targeting/source" groups (fast registered first)

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingMixedFastThenActionsGameObject()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingMixedFastThenActionsGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterTargetedWithoutTargeting(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A2"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitGameObjectTargeted(go);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourceMixedFastThenActionsGameObject()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourceMixedFastThenActionsGameObject),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSource(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A2"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitGameObjectBroadcast(go);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        // Mixed tests for post-processors in "without targeting/source" groups (fast & action)

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsMixedFastBeforeActions()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingPostProcessorsMixedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add("F"),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedWithoutTargetingPostProcessorsMixedFastThenActions()
        {
            GameObject go = new(
                nameof(TargetedWithoutTargetingPostProcessorsMixedFastThenActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (InstanceId _, SimpleTargetedMessage _) => order.Add("A"),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor(
                (ref InstanceId _, ref SimpleTargetedMessage _) => order.Add("F"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsMixedFastBeforeActions()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourcePostProcessorsMixedFastBeforeActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add("F"),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastWithoutSourcePostProcessorsMixedFastThenActions()
        {
            GameObject go = new(
                nameof(BroadcastWithoutSourcePostProcessorsMixedFastThenActions),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (InstanceId _, SimpleBroadcastMessage _) => order.Add("A"),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor(
                (ref InstanceId _, ref SimpleBroadcastMessage _) => order.Add("F"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { "F", "A" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator TargetedMixedFastThenActionsComponent()
        {
            GameObject go = new(
                nameof(TargetedMixedFastThenActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentTargeted(
                comp,
                (ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentTargeted(
                comp,
                (SimpleTargetedMessage _) => order.Add("A2"),
                0
            );
            SimpleTargetedMessage msg = new();
            msg.EmitComponentTargeted(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }

        [UnityTest]
        public IEnumerator BroadcastMixedFastThenActionsComponent()
        {
            GameObject go = new(
                nameof(BroadcastMixedFastThenActionsComponent),
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(go);
            EmptyMessageAwareComponent comp = go.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentBroadcast(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentBroadcast(
                comp,
                (SimpleBroadcastMessage _) => order.Add("A2"),
                0
            );
            SimpleBroadcastMessage msg = new();
            msg.EmitComponentBroadcast(comp);
            Assert.AreEqual(new[] { "F1", "A1", "A2" }, order.ToArray());
            yield break;
        }
    }
}

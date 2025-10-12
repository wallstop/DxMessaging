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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A2"),
                0
            );
            var msg = new SimpleUntargetedMessage();
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
            _ = token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) =>
                {
                    stages.Add("I0");
                    return true;
                },
                priority: 0
            );
            _ = token.RegisterUntargetedInterceptor<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) =>
                {
                    stages.Add("I1");
                    return true;
                },
                priority: 1
            );

            // Global accept-all (untargeted only)
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => stages.Add("G"),
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => { }
            );

            // Type handler
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => stages.Add("H")
            );

            // Post-processor
            _ = token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
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

            _ = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) =>
                {
                    stages.Add("I0");
                    return true;
                },
                0
            );
            _ = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) =>
                {
                    stages.Add("I1");
                    return true;
                },
                1
            );

            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => stages.Add("G"),
                (InstanceId _, IBroadcastMessage __) => { }
            );

            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage __) => stages.Add("H")
            );
            _ = token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage __) => stages.Add("P")
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

            _ = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                {
                    stages.Add("I0");
                    return true;
                },
                0
            );
            _ = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                {
                    stages.Add("I1");
                    return true;
                },
                1
            );

            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => stages.Add("G")
            );

            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage __) => stages.Add("H")
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage __) => stages.Add("P")
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
            _ = token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
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
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
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
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(3),
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
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(3),
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
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(3),
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
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(3),
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
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => order.Add("A"),
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F"),
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => { }
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
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => order.Add("A"),
                (InstanceId _, IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => order.Add("F"),
                (ref InstanceId _, ref IBroadcastMessage __) => { }
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
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => order.Add("A")
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => order.Add("F")
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
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add("A"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add("F"),
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
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add("A"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add("F"),
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

            _ = token.RegisterTargetedInterceptor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) =>
                {
                    stages.Add("I");
                    return true;
                },
                0
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => stages.Add("G"),
                (InstanceId _, IBroadcastMessage __) => { }
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage __) => stages.Add("Hspec")
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => stages.Add("Hall")
            );
            _ = token.RegisterGameObjectTargetedPostProcessor<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage __) => stages.Add("Pspec")
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => stages.Add("Pall")
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
            _ = token.RegisterBroadcastInterceptor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) =>
                {
                    stages.Add("I");
                    return true;
                },
                0
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => stages.Add("G")
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage __) => stages.Add("Hspec")
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => stages.Add("Hall")
            );
            _ = token.RegisterGameObjectBroadcastPostProcessor<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage __) => stages.Add("Pspec")
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => stages.Add("Pall")
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
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add(3),
                0
            );

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
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add(3),
                0
            );

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
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A1"),
                0
            );
            // Then a fast handler (by-ref)
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add("F1"),
                0
            );
            // Another action handler (by-value)
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A2"),
                0
            );

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
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (SimpleTargetedMessage _) => order.Add(3),
                0
            );

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
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(3),
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
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(3),
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
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
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
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
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
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
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
            _ = token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterUntargetedPostProcessor<SimpleUntargetedMessage>(
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
            MessageRegistrationHandle handle = token.RegisterUntargeted<SimpleUntargetedMessage>(
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
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                go,
                (ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterGameObjectTargeted<SimpleTargetedMessage>(
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
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
                go,
                (ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (SimpleTargetedMessage _) => order.Add(3),
                0
            );
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (ref SimpleTargetedMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (ref SimpleTargetedMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (SimpleTargetedMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
                comp,
                (ref SimpleTargetedMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentTargeted<SimpleTargetedMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add(1),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add(2),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (SimpleBroadcastMessage _) => order.Add("A1"),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
                comp,
                (ref SimpleBroadcastMessage _) => order.Add("F1"),
                0
            );
            _ = token.RegisterComponentBroadcast<SimpleBroadcastMessage>(
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add("A1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add("F1"),
                0
            );
            _ = token.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add("A2"),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (InstanceId _, SimpleTargetedMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterTargetedWithoutTargetingPostProcessor<SimpleTargetedMessage>(
                (ref InstanceId _, ref SimpleTargetedMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add("A1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add("F1"),
                0
            );
            _ = token.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add("A2"),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (InstanceId _, SimpleBroadcastMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<int> order = new();
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(1),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(2),
                0
            );
            _ = token.RegisterBroadcastWithoutSourcePostProcessor<SimpleBroadcastMessage>(
                (ref InstanceId _, ref SimpleBroadcastMessage __) => order.Add(3),
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => ++gUntargeted,
                (InstanceId _, ITargetedMessage __) => ++gTargeted,
                (InstanceId _, IBroadcastMessage __) => ++gBroadcast
            );
            var msg = new SimpleUntargetedMessage();
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => ++gUntargeted,
                (InstanceId _, ITargetedMessage __) => ++gTargeted,
                (InstanceId _, IBroadcastMessage __) => ++gBroadcast
            );
            var msg = new SimpleTargetedMessage();
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            int gUntargeted = 0,
                gTargeted = 0,
                gBroadcast = 0;
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => ++gUntargeted,
                (InstanceId _, ITargetedMessage __) => ++gTargeted,
                (InstanceId _, IBroadcastMessage __) => ++gBroadcast
            );
            var msg = new SimpleBroadcastMessage();
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            // No explicit registrations
            var msg1 = new SimpleUntargetedMessage();
            msg1.EmitUntargeted();
            var msg2 = new SimpleTargetedMessage();
            msg2.EmitComponentTargeted(comp);
            var msg3 = new SimpleBroadcastMessage();
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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);

            List<string> order = new();
            // Fast group (F1, F2)
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F1"),
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => order.Add("F2"),
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => { }
            );
            // Action group (A1, A2)
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => order.Add("A1"),
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => order.Add("A2"),
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => { }
            );

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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => order.Add("F1"),
                (ref InstanceId _, ref IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => order.Add("F2"),
                (ref InstanceId _, ref IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => order.Add("A1"),
                (InstanceId _, IBroadcastMessage __) => { }
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => order.Add("A2"),
                (InstanceId _, IBroadcastMessage __) => { }
            );

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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);

            List<string> order = new();
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => order.Add("F1")
            );
            _ = token.RegisterGlobalAcceptAll(
                (ref IUntargetedMessage _) => { },
                (ref InstanceId _, ref ITargetedMessage __) => { },
                (ref InstanceId _, ref IBroadcastMessage __) => order.Add("F2")
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => order.Add("A1")
            );
            _ = token.RegisterGlobalAcceptAll(
                (IUntargetedMessage _) => { },
                (InstanceId _, ITargetedMessage __) => { },
                (InstanceId _, IBroadcastMessage __) => order.Add("A2")
            );

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
            var comp = go.GetComponent<EmptyMessageAwareComponent>();
            var token = GetToken(comp);
            List<string> order = new();

            // Priority 0: fast then action
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add("F0"),
                priority: 0
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A0"),
                priority: 0
            );
            // Priority 1: fast then action
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (ref SimpleUntargetedMessage _) => order.Add("F1"),
                priority: 1
            );
            _ = token.RegisterUntargeted<SimpleUntargetedMessage>(
                (SimpleUntargetedMessage _) => order.Add("A1"),
                priority: 1
            );

            SimpleUntargetedMessage msg = new();
            msg.EmitUntargeted();
            Assert.AreEqual(new[] { "F0", "A0", "F1", "A1" }, order.ToArray());
            yield break;
        }
    }
}

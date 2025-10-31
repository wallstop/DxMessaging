#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class ReflexiveErrorTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator UnknownMethodDoesNotThrowOrInvoke()
        {
            GameObject host = new(
                nameof(UnknownMethodDoesNotThrowOrInvoke),
                typeof(SimpleMessageAwareComponent)
            );
            _spawned.Add(host);
            SimpleMessageAwareComponent comp = host.GetComponent<SimpleMessageAwareComponent>();

            int twoArgCount = 0;
            int threeArgCount = 0;
            comp.reflexiveTwoArgumentHandler = () => ++twoArgCount;
            comp.reflexiveThreeArgumentHandler = () => ++threeArgCount;

            // Use a method name that does not exist
            ReflexiveMessage bad = new("NoSuchMethodOnComponent", ReflexiveSendMode.Flat, 1, 2, 3);
            InstanceId hostId = host;
            bad.EmitTargeted(hostId);

            // Ensure nothing was called
            Assert.AreEqual(0, twoArgCount);
            Assert.AreEqual(0, threeArgCount);
            yield break;
        }
    }
}

#endif

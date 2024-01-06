namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class RegistrationTests : MessagingTestBase
    {

        [UnityTest]
        public IEnumerator UntargetedNormal()
        {
            GameObject test = new(nameof(UntargetedNormal), typeof(EmptyMessageAwareComponent));
            _spawned.Add(test);
            EmptyMessageAwareComponent component = test.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken token = GetToken(component);

            List<MessageRegistrationHandle> handles = new(_numRegistrations);
            int count = 0;

            void Handle(SimpleUntargetedMessage message)
            {
                ++count;
            }

            for (int i = 0; i < _numRegistrations; ++i)
            {
                var handle = token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);
                Assert.IsFalse(handles.Contains(handle));
            }

            yield break;
        }
    }
}

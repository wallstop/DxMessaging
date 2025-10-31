#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Components;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class AlternateBusTests : MessagingTestBase
    {
        [UnityTest]
        public IEnumerator CustomMessageBusIsolatedFromGlobalBus()
        {
            GameObject globalObject = new(
                nameof(CustomMessageBusIsolatedFromGlobalBus) + "_Global",
                typeof(EmptyMessageAwareComponent)
            );
            _spawned.Add(globalObject);
            EmptyMessageAwareComponent globalComponent =
                globalObject.GetComponent<EmptyMessageAwareComponent>();
            MessageRegistrationToken globalToken = GetToken(globalComponent);

            int globalUntargetedCount = 0;
            MessageRegistrationHandle globalHandle =
                globalToken.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                    ++globalUntargetedCount
                );

            GameObject customObject = new(
                nameof(CustomMessageBusIsolatedFromGlobalBus) + "_Custom"
            );
            _spawned.Add(customObject);
            MessageHandler customHandler = new(customObject) { active = true };
            MessageBus customBus = new();
            MessageRegistrationToken customToken = MessageRegistrationToken.Create(
                customHandler,
                customBus
            );
            customToken.Enable();

            int customUntargetedCount = 0;
            MessageRegistrationHandle customUntargetedHandle =
                customToken.RegisterUntargeted<SimpleUntargetedMessage>(_ =>
                    ++customUntargetedCount
                );

            SimpleUntargetedMessage untargetedMessage = new();
            untargetedMessage.EmitUntargeted(customBus);
            Assert.AreEqual(1, customUntargetedCount);
            Assert.AreEqual(0, globalUntargetedCount);

            untargetedMessage.EmitUntargeted();
            Assert.AreEqual(1, customUntargetedCount);
            Assert.AreEqual(1, globalUntargetedCount);

            int customTargetedCount = 0;
            MessageRegistrationHandle customTargetedHandle =
                customToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(
                    customObject,
                    _ => ++customTargetedCount
                );
            SimpleTargetedMessage targetedMessage = new();
            targetedMessage.EmitGameObjectTargeted(customObject, customBus);
            Assert.AreEqual(1, customTargetedCount);

            targetedMessage.EmitGameObjectTargeted(globalObject);
            Assert.AreEqual(1, customTargetedCount);

            customToken.RemoveRegistration(customTargetedHandle);
            customToken.RemoveRegistration(customUntargetedHandle);
            customToken.UnregisterAll();
            customHandler.active = false;

            globalToken.RemoveRegistration(globalHandle);
            yield break;
        }
    }
}

#endif

namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using DxMessaging.Core;
    using DxMessaging.Core.Helper;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;

    public sealed class UntargetedPrefreezeTests
    {
        [Test]
        public void PrefreezeRunsOncePerEmission()
        {
            MessageHandler handler = new(new InstanceId(123)) { active = true };
            MessageBus messageBus = new();
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, messageBus);

            int postProcessCount = 0;
            _ = token.RegisterUntargeted((ref SimpleUntargetedMessage _) => { });
            _ = token.RegisterUntargetedPostProcessor(
                (ref SimpleUntargetedMessage _) => postProcessCount++,
                priority: 0
            );

            token.Enable();

            object cache = GetUntargetedPostProcessingFastCache(
                handler,
                messageBus,
                typeof(SimpleUntargetedMessage),
                priority: 0
            );

            SimpleUntargetedMessage message = new();
            messageBus.UntargetedBroadcast(ref message);
            Assert.AreEqual(1, postProcessCount);
            Assert.AreEqual(1, GetPrefreezeInvocationCount(cache));

            messageBus.UntargetedBroadcast(ref message);
            Assert.AreEqual(2, postProcessCount);
            Assert.AreEqual(2, GetPrefreezeInvocationCount(cache));

            token.Disable();
        }

        private static object GetUntargetedPostProcessingFastCache(
            MessageHandler handler,
            MessageBus messageBus,
            Type messageType,
            int priority
        )
        {
            FieldInfo handlersField = typeof(MessageHandler).GetField(
                "_handlersByTypeByMessageBus",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            Assert.IsNotNull(handlersField);
            var handlersByBus = (List<MessageCache<object>>)handlersField.GetValue(handler);
            Assert.IsNotNull(handlersByBus);

            MessageCache<object> cacheByType = handlersByBus[
                messageBus.RegisteredGlobalSequentialIndex
            ];
            MethodInfo getOrAddMethod = typeof(MessageCache<object>)
                .GetMethod(nameof(MessageCache<object>.GetOrAdd))
                ?.MakeGenericMethod(messageType);
            Assert.IsNotNull(getOrAddMethod);

            object typedHandler = getOrAddMethod.Invoke(cacheByType, null);
            Assert.IsNotNull(typedHandler);

            FieldInfo fastHandlersField = typedHandler
                .GetType()
                .GetField(
                    "_untargetedPostProcessingFastHandlers",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
            Assert.IsNotNull(fastHandlersField);

            object fastHandlers = fastHandlersField.GetValue(typedHandler);
            Assert.IsNotNull(fastHandlers);

            return GetDictionaryValue(fastHandlers, priority);
        }

        private static object GetDictionaryValue(object dictionary, int key)
        {
            MethodInfo tryGetValue = dictionary
                .GetType()
                .GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(tryGetValue);

            object[] args = { key, null };
            bool found = (bool)tryGetValue.Invoke(dictionary, args);
            Assert.IsTrue(found, $"Failed to locate cache for priority {key}.");
            return args[1];
        }

        private static int GetPrefreezeInvocationCount(object handlerCache)
        {
            FieldInfo countField = handlerCache
                .GetType()
                .GetField(
                    "prefreezeInvocationCount",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
            Assert.IsNotNull(countField);
            return (int)countField.GetValue(handlerCache);
        }
    }
}

namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using NUnit.Framework;
    using Unity;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;
    using Random = System.Random;

    public abstract class MessagingTestBase
    {
        protected int _numRegistrations;
        protected readonly List<GameObject> _spawned = new();
        protected readonly Random _random = new();

        protected virtual bool MessagingDebugEnabled => true;

        [SetUp]
        public virtual void Setup()
        {
            MessagingDebug.enabled = MessagingDebugEnabled;
            MessagingDebug.LogFunction = (level, message) =>
            {
                switch (level)
                {
                    case LogLevel.Debug:
                    case LogLevel.Info:
                        Debug.Log(message);
                        return;
                    case LogLevel.Warn:
                        Debug.LogWarning(message);
                        return;
                    case LogLevel.Error:
                        Debug.LogError(message);
                        return;
                }
            };
            MessageBus messageBus = MessageHandler.MessageBus;
            Assert.IsNotNull(messageBus);
            messageBus.Log.Enabled = true;
            _numRegistrations = 150;

            LogMessageBusStatus();
        }

        protected void LogMessageBusStatus()
        {
            MessageBus messageBus = MessageHandler.MessageBus;
            Debug.Log(
                $"Untargeted registrations: {messageBus.RegisteredUntargeted}, "
                    + $"targeted registrations: {messageBus.RegisteredTargeted}, "
                    + $"broadcast registrations: {messageBus.RegisteredBroadcast}."
            );
        }

        [TearDown]
        public virtual void Cleanup()
        {
            foreach (GameObject spawned in _spawned)
            {
                if (spawned == null)
                {
                    continue;
                }

                Object.Destroy(spawned);
            }

            _spawned.Clear();
        }

        [UnitySetUp]
        public virtual IEnumerator UnitySetup()
        {
            return WaitUntilMessageHandlerIsFresh();
        }

        protected void Run(
            Func<IEnumerable<MessageRegistrationHandle>> register,
            Action emit,
            Action assert,
            Action finalAssert,
            MessageRegistrationToken token,
            bool synchronizeDeregistrations = false
        )
        {
            HashSet<MessageRegistrationHandle> handles = new();
            try
            {
                List<List<MessageRegistrationHandle>> indexedRegistrations = new(_numRegistrations);
                for (int i = 0; i < _numRegistrations; ++i)
                {
                    List<MessageRegistrationHandle> registrations = register().ToList();
                    foreach (MessageRegistrationHandle handle in registrations)
                    {
                        handles.Add(handle);
                    }

                    indexedRegistrations.Add(registrations);
                }

                if (synchronizeDeregistrations)
                {
                    foreach (
                        int index in Enumerable
                            .Range(0, indexedRegistrations.Count)
                            .OrderBy(_ => _random.Next())
                    )
                    {
                        emit();
                        assert();
                        foreach (MessageRegistrationHandle handle in indexedRegistrations[index])
                        {
                            handles.Remove(handle);
                            token.RemoveRegistration(handle);
                        }
                    }
                }
                else
                {
                    foreach (
                        MessageRegistrationHandle handle in handles
                            .OrderBy(_ => _random.Next())
                            .ToList()
                    )
                    {
                        emit();
                        assert();
                        handles.Remove(handle);
                        token.RemoveRegistration(handle);
                    }
                }

                emit();
                finalAssert();
                emit();
                finalAssert();
            }
            finally
            {
                foreach (MessageRegistrationHandle handle in handles)
                {
                    token.RemoveRegistration(handle);
                }
            }
        }

        protected static MessageRegistrationToken GetToken(MessageAwareComponent component)
        {
            return component.Token;
        }

        protected static IEnumerator WaitUntilMessageHandlerIsFresh()
        {
            MessageBus messageBus = MessageHandler.MessageBus;
            Assert.IsNotNull(messageBus);

            Stopwatch timer = Stopwatch.StartNew();

            while (IsStale() && timer.Elapsed < TimeSpan.FromSeconds(1.25))
            {
                yield return null;
            }

            Assert.IsFalse(
                IsStale(),
                "MessageHandler had {0} Untargeted registrations, {1} Targeted registrations, {2} Broadcast registrations. Registration log: {3}.",
                messageBus.RegisteredUntargeted,
                messageBus.RegisteredTargeted,
                messageBus.RegisteredBroadcast,
                messageBus.Log
            );
            yield break;

            bool IsStale()
            {
                return messageBus.RegisteredUntargeted != 0
                    || messageBus.RegisteredTargeted != 0
                    || messageBus.RegisteredBroadcast != 0;
            }
        }
    }
}

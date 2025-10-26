namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using DxMessaging.Core;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Tests.Runtime.Scripts.Messages;
    using NUnit.Framework;

    public sealed class MessageRegistrationBuilderTests
    {
        private sealed class PassthroughMessageBusProvider : IMessageBusProvider
        {
            private readonly IMessageBus _messageBus;

            public PassthroughMessageBusProvider(IMessageBus messageBus)
            {
                _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            }

            public IMessageBus Resolve()
            {
                return _messageBus;
            }
        }

        private MessageRegistrationBuilder _builder;
        private IMessageBus _defaultBus;

        [SetUp]
        public void SetUp()
        {
            _defaultBus = new MessageBus();
            IMessageBusProvider provider = new PassthroughMessageBusProvider(_defaultBus);
            _builder = new MessageRegistrationBuilder(provider);
        }

        [TearDown]
        public void TearDown()
        {
            _builder = null;
            _defaultBus = null;
        }

        [Test]
        public void BuildUsesResolvedBusWhenNoOverrideProvided()
        {
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions();
            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.AreSame(_defaultBus, lease.MessageBus);
                Assert.IsNotNull(lease.Token);
                Assert.IsFalse(lease.Token.Enabled);
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void ActivateOnBuildEnablesTokenAndLifecycle()
        {
            bool buildInvoked = false;
            bool activateInvoked = false;
            bool deactivateInvoked = false;
            bool disposeInvoked = false;
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
            {
                ActivateOnBuild = true,
                Configure = token =>
                {
                    _ = token.RegisterUntargeted<SimpleUntargetedMessage>(OnSimpleMessage);
                },
                Lifecycle = new MessageRegistrationLifecycle(
                    token =>
                    {
                        buildInvoked = true;
                    },
                    token =>
                    {
                        activateInvoked = true;
                    },
                    token =>
                    {
                        deactivateInvoked = true;
                    },
                    token =>
                    {
                        disposeInvoked = true;
                    }
                ),
            };

            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.IsTrue(buildInvoked);
                Assert.IsTrue(lease.Token.Enabled);
                Assert.IsTrue(lease.IsActive);
                Assert.IsTrue(activateInvoked);

                lease.Deactivate();
                Assert.IsFalse(lease.IsActive);
                Assert.IsTrue(deactivateInvoked);
            }
            finally
            {
                lease.Dispose();
            }

            Assert.IsTrue(disposeInvoked);
        }

        [Test]
        public void PreferredMessageBusOverridesProvider()
        {
            MessageBus preferredBus = new MessageBus();
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
            {
                PreferredMessageBus = preferredBus,
            };

            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.AreSame(preferredBus, lease.MessageBus);
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void SyntheticOwnerGeneratedWhenOwnerMissing()
        {
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions();
            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.AreNotEqual(InstanceId.EmptyId, lease.Owner);
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void HandlerStartsInactiveWhenConfigured()
        {
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
            {
                HandlerStartsActive = false,
            };

            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.IsFalse(lease.Handler.active);
                lease.Activate();
                Assert.IsTrue(lease.IsActive);
                Assert.IsTrue(lease.Handler.active);
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void DiagnosticsModeRespectedWhenEnabled()
        {
            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
            {
                EnableDiagnostics = true,
                Configure = token =>
                {
                    _ = token.RegisterUntargeted<SimpleUntargetedMessage>(OnSimpleMessage);
                },
            };

            MessageRegistrationLease lease = _builder.Build(options);
            try
            {
                Assert.IsTrue(
                    lease.Token.DiagnosticMode,
                    "Diagnostics flag should propagate to the token."
                );
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void DisposingActiveLeaseInvokesDeactivateLifecycle()
        {
            bool deactivateInvoked = false;
            bool disposeInvoked = false;

            MessageRegistrationBuildOptions options = new MessageRegistrationBuildOptions
            {
                ActivateOnBuild = true,
                Lifecycle = new MessageRegistrationLifecycle(
                    null,
                    null,
                    token => deactivateInvoked = true,
                    token => disposeInvoked = true
                ),
            };

            MessageRegistrationLease lease = _builder.Build(options);
            lease.Dispose();

            Assert.IsTrue(
                deactivateInvoked,
                "Disposing an active lease should trigger OnDeactivate before tear-down."
            );
            Assert.IsTrue(disposeInvoked, "Disposing a lease should trigger OnDispose callbacks.");
        }

        private static void OnSimpleMessage(ref SimpleUntargetedMessage message) { }
    }
}

#if VCONTAINER_PRESENT
namespace DxMessaging.Samples.DI.VContainer
{
    using System;
    using DxMessaging.Core.Attributes;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using DxMessaging.Unity.Integrations.VContainer;
    using UnityEngine;
    using VContainer;
    using VContainer.Unity;

    /// <summary>
    /// Sample lifetime scope showing DI-friendly registration via IMessageRegistrationBuilder.
    /// Requires the VCONTAINER_PRESENT scripting define and VContainer package.
    /// </summary>
    public sealed class SampleLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<MessageBus.MessageBus>(Lifetime.Singleton).As<IMessageBus>();
            builder.RegisterMessageRegistrationBuilder();

            builder.RegisterEntryPoint<ScoreboardService>(Lifetime.Singleton);
        }

        [DxUntargetedMessage]
        [DxAutoConstructor]
        private readonly partial struct ScoreUpdated
        {
            public readonly int value;

            public int Value => value;
        }

        private sealed class ScoreboardService : IStartable, ITickable, IDisposable
        {
            private readonly MessageRegistrationLease lease;
            private int observedScores;

            public ScoreboardService(IMessageRegistrationBuilder registrationBuilder)
            {
                lease = registrationBuilder.Build(
                    new MessageRegistrationBuildOptions
                    {
                        Configure = token =>
                        {
                            _ = token.RegisterUntargeted<ScoreUpdated>(OnScoreUpdated);
                        },
                    }
                );
            }

            public void Start()
            {
                lease.Activate();
            }

            public void Tick()
            {
                // Emit periodically for demo purposes
                ScoreUpdated message = new ScoreUpdated(UnityEngine.Random.Range(0, 100));
                message.Emit();
            }

            public void Dispose()
            {
                lease.Dispose();
            }

            private void OnScoreUpdated(ref ScoreUpdated message)
            {
                observedScores = message.Value;
                Debug.Log($"Score observed: {observedScores}");
            }
        }
    }
}
#endif

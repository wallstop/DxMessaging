#if ZENJECT_PRESENT
namespace DxMessaging.Samples.DI.Zenject
{
    using System;
    using DxMessaging.Core.Attributes;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;
    using Zenject;

    /// <summary>
    /// Sample scene installer demonstrating how to bridge the registration builder into Zenject services.
    /// Requires the DxMessaging Zenject registration shim and the ZENJECT_PRESENT scripting define.
    /// </summary>
    public sealed class SampleInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            // Ensure the builder is available (provided by DxMessagingRegistrationInstaller).
            Container.BindInterfacesTo<PlayerSpawnTracker>().AsSingle();
        }

        [DxUntargetedMessage]
        [DxAutoConstructor]
        private readonly partial struct PlayerSpawned
        {
            public readonly string playerName;

            public string PlayerName => playerName;
        }

        private sealed class PlayerSpawnTracker : IInitializable, IDisposable
        {
            private readonly MessageRegistrationLease lease;

            public PlayerSpawnTracker(IMessageRegistrationBuilder builder)
            {
                lease = builder.Build(
                    new MessageRegistrationBuildOptions
                    {
                        Configure = token =>
                        {
                            _ = token.RegisterUntargeted<PlayerSpawned>(OnPlayerSpawned);
                        },
                    }
                );
            }

            public void Initialize()
            {
                lease.Activate();
            }

            public void Dispose()
            {
                lease.Dispose();
            }

            private static void OnPlayerSpawned(ref PlayerSpawned message)
            {
                Debug.Log($"Player spawned: {message.PlayerName}");
            }
        }
    }
}
#endif

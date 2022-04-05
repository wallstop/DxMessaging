namespace DxMessaging.Unity.Networking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Core;
    using Core.Extensions;
    using Core.Messages;
    using global::Core.Extension;
    using global::Core.Helper;
    using global::Core.Serialization;
    using global::Networking.Utils;
    using global::Unity.Collections;
    using global::Unity.Netcode;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [RequireComponent(typeof(NetworkObject))]
    public class NetworkMessagingManager : NetworkMessageAwareComponent
    {
        [Serializable]
        private struct SerializedMessage
        {
            public IMessage Message;
            public NetworkObjectReference? AssociatedObjectReference;
        }

        private const string NamedMessage = "NetworkMessage";
        private HashSet<Type> _networkMessageTypes;

        private readonly Lazy<CustomMessagingManager> _customMessagingManager = new(() => NetworkManager.Singleton.CustomMessagingManager);

        protected override void Awake()
        {
            base.Awake();

            DontDestroyOnLoad(transform.parent);

            _networkMessageTypes = Assembly.GetAssembly(typeof(NetworkMessagingManager))
                .GetTypes()
                .Where(type => type.IsDefined(typeof(NetworkMessageAttribute), true))
                .ToHashSet();

            // Don't want to be enabled on clients. Wait until we've been Network Spawned
            enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (NetUtils.IsHost())
            {
                enabled = true;
            }

            _customMessagingManager.Value.RegisterNamedMessageHandler(NamedMessage, OnMessageReceivedOverNetwork);
        }

        protected override void RegisterMessageHandlers()
        {
            _messageRegistrationToken.RegisterGlobalAcceptAll(AcceptAllUntargeted, AcceptAllTargeted,
                AcceptAllBroadcast);
        }

        private void AcceptAllBroadcast(InstanceId source, IBroadcastMessage message)
        {
            if (!IsNetworkedMessage(message.GetType()))
            {
                return;
            }

            NetworkObjectReference? sourceReference = TryGetNetworkObjectReferenceFromGameObject(source.Object);
            if (!sourceReference.HasValue)
            {
                this.LogError("Trying to send networked broadcasted message on a non-networked object");
                return;
            }

            SendSerializedMessage(message, sourceReference);
        }

        private void AcceptAllTargeted(InstanceId target, ITargetedMessage message)
        {
            if (!IsNetworkedMessage(message.GetType()))
            {
                return;
            }

            NetworkObjectReference? targetReference = TryGetNetworkObjectReferenceFromGameObject(target.Object);
            if (!targetReference.HasValue)
            {
                this.LogError("Trying to send networked targeted message on a non-networked object");
                return;
            }

            SendSerializedMessage(message, targetReference);
        }

        private void AcceptAllUntargeted(IUntargetedMessage message)
        {
            if (!IsNetworkedMessage(message.GetType()))
            {
                return;
            }

            SendSerializedMessage(message);
        }

        private void SendSerializedMessage(IMessage message, NetworkObjectReference? reference = null)
        {
            SerializedMessage serializedMessage = new() { Message = message, AssociatedObjectReference = reference};
            byte[] bytes = Serializer.BinarySerialize(serializedMessage);

            using var writer = new FastBufferWriter(1000, Allocator.Temp, 100000);
            writer.WriteValueSafe(bytes);

            foreach ((ulong clientId, NetworkClient _) in NetworkManager.ConnectedClients)
            {
                if (clientId != NetworkManager.Singleton.LocalClientId)
                {
                    _customMessagingManager.Value.SendNamedMessage(NamedMessage, clientId, writer, NetworkDelivery.Reliable);
                }
            }
        }

        private void OnMessageReceivedOverNetwork(ulong _, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] data);
            SerializedMessage serializedMessage = Serializer.BinaryDeserialize<SerializedMessage>(data);

            IMessage message = serializedMessage.Message;

            NetworkObject networkObject = null;
            if (serializedMessage.AssociatedObjectReference != null)
            {
                if (!serializedMessage.AssociatedObjectReference.Value.TryGet(out networkObject))
                {
                    this.LogWarn("Unable to find associated object reference for message");
                    return;
                }
            }

            switch (message)
            {
                case ITargetedMessage targetedMessage:
                    targetedMessage.EmitGameObjectTargeted(networkObject!.gameObject);
                    break;
                case IBroadcastMessage broadcastMessage:
                    broadcastMessage.EmitGameObjectBroadcast(networkObject!.gameObject);
                    break;
                case IUntargetedMessage untargetedMessage:
                    untargetedMessage.EmitUntargeted();
                    break;
            }
        }

        private static NetworkObjectReference? TryGetNetworkObjectReferenceFromGameObject(Object go)
        {
            if (go.TryGetComponent(out NetworkObject networkObject))
            {
                return new NetworkObjectReference(networkObject);
            }

            return null;
        }

        private bool IsNetworkedMessage(Type messageType) => _networkMessageTypes.Contains(messageType);
    }
}

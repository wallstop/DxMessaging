using System;
using System.Collections.Generic;
using DxMessaging.Core.MessageBus;

namespace DxMessaging.Core
{
    /**
         <summary>
             Abstraction layer for immediate-mode Message passing. An instance of this handles all
             kinds of types to trigger functions that are registered with it.
         </summary>
     */

    [Serializable]
    public sealed class MessageHandler
    {
        public static readonly IMessageBus MessageBus = GlobalMessageBus.Instance;

        private readonly Dictionary<Type, object> _handlersByType;

        /**
            <summary>
                Whether or not this MessageHandler will process messages.
            </summary>
        */
        public bool Active;

        public InstanceId Owner { get; private set; }

        public MessageHandler(InstanceId owner)
        {
            Owner = owner;
            _handlersByType = new Dictionary<Type, object>();
        }

        public void HandleUntargetedMessage<T>(T message) where T : AbstractMessage
        {
            ActuallyHandleMessage<T>(typedHandler => typedHandler.HandleUntargeted(message));
        }

        public void HandleTargetedMessage<T>(T message) where T : TargetedMessage
        {
            ActuallyHandleMessage<T>(typedHandler => typedHandler.HandleTargeted(message));
        }

        public void HandleGlobalMessage<T>(T message) where T : AbstractMessage
        {
            // Use the "AbstractMessage" explicitly to indicate global messages, allowing us to multi-purpose a single dictionary
            HandleUntargetedMessage<AbstractMessage>(message);
        }

        private void ActuallyHandleMessage<T>(Action<TypedHandler<T>> handle) where T : AbstractMessage
        {
            if (!Active)
            {
                return;
            }
            TypedHandler<T> handlerForType = GetHandlerForType<T>();
            if (!ReferenceEquals(handlerForType, null))
            {
                handle(handlerForType);
            }
        }

        public Action RegisterGlobalAcceptAll(Action<AbstractMessage> messageHandler)
        {
            Action messageBusDeregistration = MessageBus.RegisterGlobalAcceptAll(this);
            TypedHandler<AbstractMessage> typedHandler = GetOrCreateHandlerForType<AbstractMessage>();
            if (ReferenceEquals(typedHandler.UntargetedDeregister, null))
            {
                typedHandler.UntargetedDeregister = messageBusDeregistration;
            }
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        public Action RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterTargetedWithoutTargeting<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            if (ReferenceEquals(typedHandler.UntargetedDeregister, null))
            {
                typedHandler.UntargetedDeregister = messageBusDeregistration;
            }
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        public Action RegisterTargetedMessageHandler<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterTargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            if (ReferenceEquals(typedHandler.TargetedDeregister, null))
            {
                typedHandler.TargetedDeregister = messageBusDeregistration;
            }
            return typedHandler.AddTargetedHandler(messageHandler);
        }

        public Action RegisterUntargetedMessageHandler<T>(Action<T> messageHandler) where T : UntargetedMessage
        {
            Action messageBusDeregistration = MessageBus.RegisterUntargeted<T>(this);
            TypedHandler<T> typedHandler = GetOrCreateHandlerForType<T>();
            if (ReferenceEquals(typedHandler.UntargetedDeregister, null))
            {
                typedHandler.UntargetedDeregister = messageBusDeregistration;
            }
            return typedHandler.AddUntargetedHandler(messageHandler);
        }

        private TypedHandler<T> GetOrCreateHandlerForType<T>() where T : AbstractMessage
        {
            object existingTypedHandler;
            Type type = typeof (T);
            if (_handlersByType.TryGetValue(type, out existingTypedHandler))
            {
                return (TypedHandler<T>) existingTypedHandler;
            }

            TypedHandler<T> newTypedHandler = new TypedHandler<T>();
            _handlersByType.Add(type, newTypedHandler);
            return newTypedHandler;
        }

        private TypedHandler<T> GetHandlerForType<T>() where T : AbstractMessage
        {
            object existingTypedHandler;
            Type type = typeof (T);
            if (_handlersByType.TryGetValue(type, out existingTypedHandler))
            {
                return (TypedHandler<T>) existingTypedHandler;
            }
            return null;
        }

        [Serializable]
        private sealed class TypedHandler<T> where T : AbstractMessage
        {
            private readonly List<Action<T>> _targetedHandlers;
            private readonly List<Action<T>> _untargetedHandlers;
            public Action TargetedDeregister;
            public Action UntargetedDeregister;

            public TypedHandler()
            {
                _targetedHandlers = new List<Action<T>>();
                _untargetedHandlers = new List<Action<T>>();
            }

            public void HandleUntargeted(T message)
            {
                for (int i = 0; i < _untargetedHandlers.Count; ++i)
                {
                    _untargetedHandlers[i](message);
                }
            }

            public void HandleTargeted(T message)
            {
                for (int i = 0; i < _targetedHandlers.Count; ++i)
                {
                    _targetedHandlers[i](message);
                }
            }

            public Action AddTargetedHandler(Action<T> handler)
            {
                return AddHandlerAndGenerateDeregister(handler, _targetedHandlers, TargetedDeregister);
            }

            public Action AddUntargetedHandler(Action<T> handler)
            {
                return AddHandlerAndGenerateDeregister(handler, _untargetedHandlers, UntargetedDeregister);
            }

            private static Action AddHandlerAndGenerateDeregister(Action<T> handler, List<Action<T>> handlers,
                Action deregistration)
            {
                handlers.Add(handler);
                return () =>
                {
                    handlers.Remove(handler);
                    if (handlers.Count != 0 && deregistration != null)
                    {
                        deregistration.Invoke();
                    }
                };
            }
        }
    }

    public static class TargetedMessageExtensions
    {
        public static void EmitTargeted<T>(this T message) where T : TargetedMessage
        {
            MessageHandler.MessageBus.TargetedBroadcast(message);
        }

        public static void EmitUntargeted<T>(this T message) where T : UntargetedMessage
        {
            MessageHandler.MessageBus.UntargetedBroadcast(message);
        }

        public static void EmitUntyped(this AbstractMessage message)
        {
            MessageHandler.MessageBus.Broadcast(message);
        }
    }
}

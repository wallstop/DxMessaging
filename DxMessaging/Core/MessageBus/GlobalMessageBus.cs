using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DxMessaging.Core.MessageBus
{
    // TODO: Concurrency
    public sealed class GlobalMessageBus : IMessageBus
    {
        public static readonly GlobalMessageBus Instance = new GlobalMessageBus();
        private readonly RegistrationLog _log;

        public bool Debug = false;

        private GlobalMessageBus()
        {
            _log = new RegistrationLog();
        }

        private static readonly HashSet<MessageHandler> GlobalSinks;

        static GlobalMessageBus()
        {
            GlobalSinks = new HashSet<MessageHandler>();
        }

        public Action RegisterTargeted<T>(MessageHandler messageHandler) where T : TargetedMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(string.Format("Cannot register a null {0}", typeof(MessageHandler)));
            }
            InstanceId handlerOwnerId = messageHandler.Owner;

            Dictionary<InstanceId, MessageHandler> targetedHandlers = TargetedHandlers<T>();
            MessageHandler existingHandler;
            if (!targetedHandlers.TryGetValue(handlerOwnerId, out existingHandler))
            {
                targetedHandlers.Add(handlerOwnerId, messageHandler);
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Add,
                        MessageType.Targeted));
                }
            }
            else if (!ReferenceEquals(existingHandler, messageHandler) && Debug)
            {
                MessagingDebug.Log("Ignoring double registration of {0} with different handlers (is this intentional? Likely a bug)", handlerOwnerId);
            }
            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Remove,
                        MessageType.Targeted));
                }
                targetedHandlers.Remove(handlerOwnerId);
            };
        }

        public Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : TargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, MessageType.TargetedWithoutTargeting);
        }

        public Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : UntargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, MessageType.Untargeted);
        }

        public Action InternalRegisterUntargeted<T>(MessageHandler messageHandler, MessageType messageType)
            where T : AbstractMessage
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(string.Format("Cannot register a null {0}", typeof(MessageHandler)));
            }
            InstanceId handlerOwnerId = messageHandler.Owner;

            HashSet<MessageHandler> handlersForType = UntargetedHandlers<T>();
            bool newRegistration = handlersForType.Add(messageHandler);
            if (Debug)
            {
                if (!newRegistration)
                {
                    MessagingDebug.Log("Received double registration of {0} for {1}", typeof(T), handlerOwnerId);
                }
                _log.Log(new MessagingRegistration(handlerOwnerId, typeof(T), RegistrationType.Add, messageType));
            }

            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Remove,
                        messageType));
                }
                handlersForType.Remove(messageHandler);
            };
        }

        public Action RegisterGlobalAcceptAll(MessageHandler messageHandler)
        {
            if (Debug)
            {
                _log.Log(new MessagingRegistration(messageHandler.Owner, typeof (AbstractMessage), RegistrationType.Add,
                    MessageType.GlobalAcceptAll));
            }
            GlobalSinks.Add(messageHandler);
            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(messageHandler.Owner, typeof (AbstractMessage),
                        RegistrationType.Remove, MessageType.GlobalAcceptAll));
                }
                GlobalSinks.Remove(messageHandler);
            };
        }

        public void TargetedBroadcast<T>(T message) where T : TargetedMessage
        {
            BroadcastGlobal(message);
            TargetedBroadcast(message.Target, message, Debug);
            // Also broadcast it to any handler that registered "TargetedWithoutTargeting"
            foreach (MessageHandler handler in UntargetedHandlers<T>().ToArray())
            {
                handler.HandleUntargetedMessage(message);
            }
        }

        public void UntargetedBroadcast<T>(T message) where T : UntargetedMessage
        {
            BroadcastGlobal(message);
            InternalUntargetedBroadcast(message, Debug);
        }

        private static void BroadcastGlobal<T>(T typedMessage) where T : AbstractMessage
        {
            foreach (MessageHandler handler in GlobalSinks.ToArray())
            {
                handler.HandleGlobalMessage(typedMessage);
            }
        }

        private static void InternalUntargetedBroadcast<T>(T message, bool debug) where T : UntargetedMessage
        {
            MessageHandler[] untargedHandlers = UntargetedHandlers<T>().ToArray();
            foreach (MessageHandler handler in untargedHandlers)
            {
                handler.HandleUntargetedMessage(message);
            }

            if (untargedHandlers.Length == 0 && debug)
            {
                MessagingDebug.Log("Could not find a matching handler for Message: {0}", message);
                MessagingDebug.Log(Instance._log.ToString());
            }
        }

        private static void TargetedBroadcast<T>(InstanceId target, T typedAndTargetedMessage, bool debug) where T : TargetedMessage
        {
            MessageHandler handler;
            if (TargetedHandler<T>(target, out handler))
            {
                handler.HandleTargetedMessage(typedAndTargetedMessage);
                return;
            }
            if (target == InstanceId.InvalidId)
            {
                if (debug)
                {
                    MessagingDebug.Log("Invalid Id as target of {0}, ignoring.", typedAndTargetedMessage);
                }
                return;
            }

            if (debug)
            {
                MessagingDebug.Log("Could not find a matching handler for Id: {0}, Message: {1}", target,
                    typedAndTargetedMessage);
                MessagingDebug.Log(Instance._log.ToString());
            }
        }

        private static bool TargetedHandler<T>(InstanceId target, out MessageHandler handler) where T : AbstractMessage
        {
            return SpecializedHandler<T>.TargetedSinks.TryGetValue(target, out handler);
        }

        private static Dictionary<InstanceId, MessageHandler> TargetedHandlers<T>() where T : AbstractMessage
        {
            return SpecializedHandler<T>.TargetedSinks;
        }

        private static HashSet<MessageHandler> UntargetedHandlers<T>() where T : AbstractMessage
        {
            return SpecializedHandler<T>.Sinks;
        }

        public void Broadcast(AbstractMessage message)
        {
            {
                TargetedMessage maybeTargeted = message as TargetedMessage;
                if (!ReferenceEquals(maybeTargeted, null))
                {
                    MethodInfo targetedBroadcast = typeof (GlobalMessageBus).GetMethod("TargetedBroadcast")
                        .MakeGenericMethod(maybeTargeted.GetType());
                    targetedBroadcast.Invoke(maybeTargeted, null);
                    return;
                }
            }
            {
                UntargetedMessage maybeUntargeted = message as UntargetedMessage;
                if (!ReferenceEquals(maybeUntargeted, null))
                {
                    MethodInfo untargetedBroadcast = typeof (GlobalMessageBus).GetMethod("UntargetedBroadcast")
                        .MakeGenericMethod(maybeUntargeted.GetType());
                    untargetedBroadcast.Invoke(maybeUntargeted, null);
                    return;
                }
            }
            throw new ArgumentException(string.Format("Cannot route MessageType {0}: {1}.", message.GetType(), message));
        }

        private static class SpecializedHandler<T> where T : AbstractMessage
        {
            public static readonly HashSet<MessageHandler> Sinks;

            public static readonly Dictionary<InstanceId, MessageHandler> TargetedSinks;

            static SpecializedHandler()
            {
                Sinks = new HashSet<MessageHandler>();
                TargetedSinks = new Dictionary<InstanceId, MessageHandler>();
            }
        }
    }
}

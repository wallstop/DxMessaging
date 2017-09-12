using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DxMessaging.Core.MessageBus
{
    /// <inheritdoc />
    /// <summary>
    /// Singleton-style MessageBus for maximum efficiency.
    /// </summary>
    /// <note>
    /// This is not thread safe in the slightest.
    /// </note>
    public sealed class GlobalMessageBus : IMessageBus
    {
        public static readonly GlobalMessageBus Instance = new GlobalMessageBus();
        private readonly RegistrationLog _log;

        /// <summary>
        /// Turning this on will cause messages to be piped to MessagingDebug, and registrations logged to the RegistrationLog.
        /// These are generally only in error cases, and include relevant information.
        /// </summary>
        /// <note>
        /// This is completely separate from the MessagingDebug. MessagingDebug can be turned off, and this on, which will cause registrations to be logged.
        /// </note>
        public bool Debug = false;

        private GlobalMessageBus()
        {
            _log = new RegistrationLog();
        }

        /// <summary>
        /// Accept-all MessageHandlers.
        /// </summary>
        private static readonly HashSet<MessageHandler> GlobalSinks;

        static GlobalMessageBus()
        {
            GlobalSinks = new HashSet<MessageHandler>();
        }
        
        /// <inheritdoc />
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
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Register,
                        RegistrationMethod.Targeted));
                }
            }
            else if (!ReferenceEquals(existingHandler, messageHandler) && Debug)
            {
                /*
                    Possible bug - on the double register, we still send a valid deregistration action. 
                    While deregistration is idempotent, if the registration isn't "successful", in the sense 
                    that it's probably a programming bug, should we send a no-op deregistration? Not sure.
                */
                MessagingDebug.Log("Ignoring double registration of {0} with different handlers (is this intentional? Likely a bug)", handlerOwnerId);
            }

            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Deregister,
                        RegistrationMethod.Targeted));
                }
                // Deregistration is simply removing ourselves from the targetedHandlers set
                targetedHandlers.Remove(handlerOwnerId);
            };
        }

        /// <inheritdoc />
        public Action RegisterTargetedWithoutTargeting<T>(MessageHandler messageHandler) where T : TargetedMessage
        {
            // We treat TargetedWithoutTargeting as "Untargeted" internally
            return InternalRegisterUntargeted<T>(messageHandler, RegistrationMethod.TargetedWithoutTargeting);
        }

        /// <inheritdoc />
        public Action RegisterUntargeted<T>(MessageHandler messageHandler) where T : UntargetedMessage
        {
            return InternalRegisterUntargeted<T>(messageHandler, RegistrationMethod.Untargeted);
        }

        /// <summary>
        /// Common code between TargetedWithoutTargeting and Untargeted registration process.
        /// </summary>
        /// <typeparam name="T">Type of message being signed up for.</typeparam>
        /// <param name="messageHandler">MessageHandler to register.</param>
        /// <param name="registrationMethod">Method of registration.</param>
        /// <returns>The deregistration action. Should be invoked when the handler no longer wants to receive messages.</returns>
        private Action InternalRegisterUntargeted<T>(MessageHandler messageHandler, RegistrationMethod registrationMethod)
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
                    // Similar possible bug to RegisterTargeted WRT double registration deregistration action.
                    MessagingDebug.Log("Received double registration of {0} for {1}", typeof(T), handlerOwnerId);
                }
                _log.Log(new MessagingRegistration(handlerOwnerId, typeof(T), RegistrationType.Register, registrationMethod));
            }

            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(handlerOwnerId, typeof (T), RegistrationType.Deregister,
                        registrationMethod));
                }
                // Deregistration is simply removing ourselves from the handlersForType set.
                handlersForType.Remove(messageHandler);
            };
        }

        /// <inheritdoc />
        public Action RegisterGlobalAcceptAll(MessageHandler messageHandler)
        {
            if (Debug)
            {
                _log.Log(new MessagingRegistration(messageHandler.Owner, typeof (AbstractMessage), RegistrationType.Register,
                    RegistrationMethod.GlobalAcceptAll));
            }
            GlobalSinks.Add(messageHandler);
            return () =>
            {
                if (Debug)
                {
                    _log.Log(new MessagingRegistration(messageHandler.Owner, typeof (AbstractMessage),
                        RegistrationType.Deregister, RegistrationMethod.GlobalAcceptAll));
                }
                GlobalSinks.Remove(messageHandler);
            };
        }

        /// <inheritdoc />
        public void TargetedBroadcast<T>(T message) where T : TargetedMessage
        {
            BroadcastGlobal(message);
            TargetedBroadcast(message.Target, message, Debug);
            /*
                Also broadcast it to any handler that registered "TargetedWithoutTargeting".
                We take a copy to avoid invalidating our iteration - the mere act of broadcasting
                a message may register new message handlers. We could avoid this with a 
                double buffering technique + dirty flag, or staging the actions, or something even
                smarter, but this is good enough for now.
            */
            MessageHandler[] untargetedHandlers = UntargetedHandlers<T>().ToArray();
            // ReSharper disable once ForCanBeConvertedToForeach
            for(int i = 0; i < untargetedHandlers.Length; ++i) 
            {
                MessageHandler handler = untargetedHandlers[i];
                handler.HandleUntargetedMessage(message);
            }
        }

        /// <inheritdoc />
        public void UntargetedBroadcast<T>(T message) where T : UntargetedMessage
        {
            BroadcastGlobal(message);
            InternalUntargetedBroadcast(message, Debug);
        }

        /// <summary>
        /// Broadcasts the message to all global handlers (accept everything).
        /// </summary>
        /// <typeparam name="T">Type of message being broadcast.</typeparam>
        /// <param name="typedMessage">Exact message being broadcast.</param>
        private static void BroadcastGlobal<T>(T typedMessage) where T : AbstractMessage
        {
            /*
                We take a copy to avoid invalidating our iteration - the mere act of broadcasting
                a message may register new message handlers. We could avoid this with a 
                double buffering technique + dirty flag, or staging the actions, or something even
                smarter, but this is good enough for now.
            */
            MessageHandler[] globalHandlers = GlobalSinks.ToArray();
            // ReSharper disable once ForCanBeConvertedToForeach
            for(int i = 0;i < globalHandlers.Length; ++i) 
            {
                MessageHandler handler = globalHandlers[i];
                handler.HandleGlobalMessage(typedMessage);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <param name="debug"></param>
        private static void InternalUntargetedBroadcast<T>(T message, bool debug) where T : UntargetedMessage
        {
            /*
                We take a copy to avoid invalidating our iteration - the mere act of broadcasting
                a message may register new message handlers. We could avoid this with a 
                double buffering technique + dirty flag, or staging the actions, or something even
                smarter, but this is good enough for now.
            */
            MessageHandler[] untargedHandlers = UntargetedHandlers<T>().ToArray();
            // ReSharper disable once ForCanBeConvertedToForeach
            for(int i = 0; i < untargedHandlers.Length; ++i) 
            {
                MessageHandler handler = untargedHandlers[i];
                handler.HandleUntargetedMessage(message);
            }

            if (untargedHandlers.Length == 0 && debug)
            {
                MessagingDebug.Log("Could not find a matching handler for Message: {0}", message);
                MessagingDebug.Log(Instance._log.ToString());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="typedAndTargetedMessage"></param>
        /// <param name="debug"></param>
        private static void TargetedBroadcast<T>(InstanceId target, T typedAndTargetedMessage, bool debug) where T : TargetedMessage
        {
            MessageHandler handler;
            if (TargetedHandler<T>(target, out handler))
            {
                handler.HandleTargetedMessage(typedAndTargetedMessage);
                return;
            }
            if (!debug)
            {
                return;
            }

            if (target == InstanceId.InvalidId)
            {
                MessagingDebug.Log("Invalid Id as target of {0}, ignoring.", typedAndTargetedMessage);
                return;
            }
            MessagingDebug.Log("Could not find a matching handler for Id: {0}, Message: {1}", target,
                typedAndTargetedMessage);
            MessagingDebug.Log(Instance._log.ToString());
        }

        /// <summary>
        /// Retrieves a handler that handles the given type of message for the target, if any exist.
        /// </summary>
        /// <typeparam name="T">Type of Message.</typeparam>
        /// <param name="target">Target of the Message.</param>
        /// <param name="handler">Existing handler.</param>
        /// <returns>True if a handler was found, false otherwise (handler will be null in this case).</returns>
        private static bool TargetedHandler<T>(InstanceId target, out MessageHandler handler) where T : TargetedMessage
        {
            return SpecializedHandler<T>.TargetedSinks.TryGetValue(target, out handler);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private static Dictionary<InstanceId, MessageHandler> TargetedHandlers<T>() where T : AbstractMessage
        {
            return SpecializedHandler<T>.TargetedSinks;
        }

        /// <summary>
        /// Helper method for getting all Untargeted Handlers for the specific Message Type
        /// </summary>
        /// <typeparam name="T">Specific message type.</typeparam>
        /// <returns></returns>
        private static HashSet<MessageHandler> UntargetedHandlers<T>() where T : AbstractMessage
        {
            return SpecializedHandler<T>.Sinks;
        }

        /// <inheritdoc />
        public void Broadcast(AbstractMessage message)
        {
            // Since we don't know the type of the message, we need to use reflection to figure out the proper thing to do
            // TODO: When Unity has better C# support, switch to dynamic
            {
                // Maybe it's a targeted message?
                TargetedMessage maybeTargeted = message as TargetedMessage;
                if (!ReferenceEquals(maybeTargeted, null))
                {
                    MethodInfo targetedBroadcast = typeof (GlobalMessageBus).GetMethod("TargetedBroadcast")
                        .MakeGenericMethod(maybeTargeted.GetType());
                    targetedBroadcast.Invoke(maybeTargeted, null);
                    return;
                }
                // Maybe not...
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

        /// <summary>
        /// Specifically typed message handler. While we don't explicitly use the generic type,
        /// the static generic means that we'll have a single instance of these per Message type,
        /// which is exactly what we want.
        /// </summary>
        /// <typeparam name="T">Specific message type.</typeparam>
        // ReSharper disable once UnusedTypeParameter
        private static class SpecializedHandler<T> where T : AbstractMessage
        {
            /// <summary>
            /// MessageHandlers that care about the specific MessageType, without any targeting
            /// </summary>
            public static readonly HashSet<MessageHandler> Sinks;

            /// <summary>
            /// Target (InstanceId) to handler mapping, allowing us to target things
            /// </summary>
            public static readonly Dictionary<InstanceId, MessageHandler> TargetedSinks;

            static SpecializedHandler()
            {
                Sinks = new HashSet<MessageHandler>();
                TargetedSinks = new Dictionary<InstanceId, MessageHandler>();
            }
        }
    }
}

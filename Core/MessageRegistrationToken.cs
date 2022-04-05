namespace DxMessaging.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using MessageBus;
    using Messages;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public readonly struct MessageRegistrationHandle : IEquatable<MessageRegistrationHandle>, IComparable<MessageRegistrationHandle>
    {
        private readonly Guid _handle;

        public static MessageRegistrationHandle CreateMessageRegistrationHandle()
        {
            return new MessageRegistrationHandle(Guid.NewGuid());
        }

        private MessageRegistrationHandle(Guid handle)
        {
            _handle = handle;
        }

        public override int GetHashCode()
        {
            return _handle.GetHashCode();
        }

        public override bool Equals(object other)
        {
            return other is MessageRegistrationHandle handle && Equals(handle);
        }

        public bool Equals(MessageRegistrationHandle other)
        {
            return _handle.Equals(other._handle);
        }

        public int CompareTo(MessageRegistrationHandle other)
        {
            return _handle.CompareTo(other._handle);
        }
    }

    /// <summary>
    /// Maintains all of the [de]registration logic for MessagingComponents. Wraps registrations up for lazy registration, which are executed on Enable() call.
    /// </summary>
    /// <note>
    /// General usage should be to create one of these on awake or start (probably start), and bind all messaging functions there.
    /// Then, on OnEnable(), call .Enable(), OnDisable(), call .Disable()
    /// </note>
    [Serializable]
    public sealed class MessageRegistrationToken
    {
        public bool Enabled { get; private set; }

        private readonly MessageHandler _messageHandler;

        private readonly Dictionary<MessageRegistrationHandle, Action> _registrations = new Dictionary<MessageRegistrationHandle, Action>();
        private readonly Dictionary<MessageRegistrationHandle, Action> _deregistrations = new Dictionary<MessageRegistrationHandle, Action>();

        private readonly IMessageBus _messageBus;

        private MessageRegistrationToken(MessageHandler messageHandler, IMessageBus messageBus)
        {
            Enabled = false;
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _messageBus = messageBus;
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards it.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="targetedHandler">Actual handler functionality</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectTargeted<T>(Action<T> targetedHandler) where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(targetedHandler, () => _messageHandler.RegisterTargetedMessageHandler(targetedHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectTargeted<T>(GameObject target, Action<T> targetedHandler)
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(targetedHandler, () => _messageHandler.RegisterTargetedMessageHandler(target, targetedHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentTargeted<T>(Component target, Action<T> targetedHandler)
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(targetedHandler, () => _messageHandler.RegisterTargetedMessageHandler(target, targetedHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="messageHandler">Actual handler functionality</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler) where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(messageHandler, () => _messageHandler.RegisterTargetedWithoutTargeting(messageHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept UntargetedMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="untargetedHandler">Actual handler functionality</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterUntargeted<T>(Action<T> untargetedHandler) where T : IUntargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(untargetedHandler, () => _messageHandler.RegisterUntargetedMessageHandler(untargetedHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="source">Id of the source for BroadcastMessages to listen for.</param>
        /// <param name="broadcastHandler">Actual handler functionality.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectBroadcast<T>(GameObject source, Action<T> broadcastHandler) where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(broadcastHandler, () => _messageHandler.RegisterSourcedBroadcastMessageHandler(source, broadcastHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="source">The component source for BroadcastMessages to listen for.</param>
        /// <param name="broadcastHandler">Actual handler functionality.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentBroadcast<T>(Component source, Action<T> broadcastHandler) where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(broadcastHandler, () => _messageHandler.RegisterSourcedBroadcastMessageHandler(source, broadcastHandler, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="broadcastHandler">Action handler functionality.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastWithoutSource<T>(Action<T> broadcastHandler)
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(broadcastHandler, () => _messageHandler.RegisterSourcedBroadcastWithoutSource(broadcastHandler, _messageBus));
        }


        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept every message that is broadcast.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <param name="acceptAllUntargeted">Action handler functionality for UntargetedMessages.</param>
        /// <param name="acceptAllTargeted">Action handler functionality for TargetedMessages.</param>
        /// <param name="acceptAllBroadcast">Action handler functionality for BroadcastMessages.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGlobalAcceptAll(Action<IUntargetedMessage> acceptAllUntargeted, Action<InstanceId, ITargetedMessage> acceptAllTargeted, Action<InstanceId, IBroadcastMessage> acceptAllBroadcast)
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister<IMessage>(() => _messageHandler.RegisterGlobalAcceptAll(acceptAllUntargeted, acceptAllTargeted, acceptAllBroadcast, _messageBus));
        }

        /// <summary>
        /// Stages a registration of the provided intercept transformation function for every message of the specified type that is sent.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the transformer accepts.</typeparam>
        /// <param name="transformer">Actual transformer functionality.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterIntercept<T>(Func<T, T> transformer) where T: IMessage
        {
            MessageRegistrationHandle handle = MessageRegistrationHandle.CreateMessageRegistrationHandle();
            if (_messageHandler == null) // Unity has a bug
            {
                return handle;
            }
            return InternalRegister<T>(() => _messageHandler.RegisterIntercept(transformer, _messageBus));
        }

        /// <summary>
        /// Handles the actual [de]registration wrapping and (potential) lazy execution.
        /// </summary>
        /// <typeparam name="T">Type of message being registered.</typeparam>
        /// <param name="handler">Handler being registered (mainly used for type info).</param>
        /// <param name="registerAndGetDeregistration">Proxied registration function that returns a de-registration function.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        private MessageRegistrationHandle InternalRegister<T>(Action<T> handler, Func<Action> registerAndGetDeregistration)
            where T : IMessage
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return InternalRegister<T>(registerAndGetDeregistration);
        }

        /// <summary>
        /// Handles the actual [de]registration wrapping and (potential) lazy execution.
        /// </summary>
        /// <typeparam name="T">Type of message being registered.</typeparam>
        /// <param name="registerAndGetDeregistration">Proxied registration function that returns a de-registration function.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        private MessageRegistrationHandle InternalRegister<T>(Func<Action> registerAndGetDeregistration)
        {
            MessageRegistrationHandle handle = MessageRegistrationHandle.CreateMessageRegistrationHandle();

            // We don't want to actually register at this time (might not be awake/enabled) - so we wrap that shit up, to lazy register when we're enabled.
            Action registration = () =>
            {
                Action actualDeregistration = registerAndGetDeregistration();
                _deregistrations[handle] = actualDeregistration;
            };

            _registrations[handle] = registration;

            // Generally, registrations should take place before all calls to enable. Just in case, though...
            if (Enabled)
            {
                registration();
            }

            return handle;
        }

        /// <summary>
        /// Enables the token if not already enabled. Executes all staged registrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
        public void Enable()
        {
            if (Enabled)
            {
                return;
            }

            foreach (Action registrationAction in _registrations?.Values ?? Enumerable.Empty<Action>())
            {
                registrationAction();
            }

            Enabled = true;
        }

        /// <summary>
        /// Disables the token if not already disabled. Executes all staged de-registrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
        public void Disable()
        {
            if (!Enabled)
            {
                return;
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            foreach (Action deregistrationAction in _deregistrations?.Values ?? Enumerable.Empty<Action>())
            {
                deregistrationAction();
            }

            Enabled = false;
        }

        /// <summary>
        /// Disables the token and clears all registrations + de-registrations
        /// </summary>
        public void UnregisterAll()
        {
            if (Enabled)
            {
                foreach (Action deregistrationAction in _deregistrations?.Values ?? Enumerable.Empty<Action>())
                {
                    deregistrationAction();
                }
            }

            Enabled = false;
            _registrations?.Clear();
            _deregistrations?.Clear();
        }

        public void RemoveRegistration(MessageRegistrationHandle handle)
        {
            if (_deregistrations != null && _deregistrations.TryGetValue(handle, out Action deregistrationAction))
            {
                deregistrationAction();
            }

            _ = _registrations?.Remove(handle);
            _ = _deregistrations?.Remove(handle);
        }

        /// <summary>
        /// Creates a MessagingRegistrationToken that operates on the given handler.
        /// </summary>
        /// <param name="messageHandler">Message handler to register handlers to.</param>
        /// <param name="messageBus">MessageBus to use for this MessageRegistrationToken. Uses the GlobalMessageBus if left null.</param>
        /// <returns>MessagingRegistrationToken bound to the MessageHandler.</returns>
        public static MessageRegistrationToken Create(MessageHandler messageHandler, IMessageBus messageBus = null)
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            return new MessageRegistrationToken(messageHandler, messageBus);
        }
    }
}

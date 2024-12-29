namespace DxMessaging.Core
{
    using System;
    using System.Collections.Generic;
    using MessageBus;
    using Messages;

    /// <summary>
    /// Maintains all the [de]registration logic for MessagingComponents. Wraps registrations up for lazy registration, which are executed on Enable() call.
    /// </summary>
    /// <note>
    /// General usage should be to create one of these on awake or start (probably start), and bind all messaging functions there.
    /// Then, on OnEnable(), call .Enable(), OnDisable(), call .Disable()
    /// </note>
    public sealed class MessageRegistrationToken
    {
        public bool Enabled => _enabled;

        private readonly MessageHandler _messageHandler;

        private readonly Dictionary<MessageRegistrationHandle, Action> _registrations = new();
        private readonly Dictionary<MessageRegistrationHandle, Action> _deregistrations = new();

        private readonly IMessageBus _messageBus;
        private bool _enabled;

        private MessageRegistrationToken(MessageHandler messageHandler, IMessageBus messageBus)
        {
            _enabled = false;
            _messageHandler =
                messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _messageBus = messageBus;
        }

        private MessageRegistrationHandle RegisterTargetedInternal<T>(
            InstanceId target,
            Action<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedMessageHandler(
                        target,
                        targetedHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        private MessageRegistrationHandle RegisterTargetedInternal<T>(
            InstanceId target,
            MessageHandler.FastHandler<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedMessageHandler(
                        target,
                        targetedHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

#if UNITY_2017_1_OR_NEWER
        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectTargeted<T>(
            UnityEngine.GameObject target,
            Action<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectTargeted<T>(
            UnityEngine.GameObject target,
            MessageHandler.FastHandler<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentTargeted<T>(
            UnityEngine.Component target,
            Action<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentTargeted<T>(
            UnityEngine.Component target,
            MessageHandler.FastHandler<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process TargetedMessages of the given type for the provided target.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target to post process messages for.</param>
        /// <param name="targetedPostProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectTargetedPostProcessor<T>(
            UnityEngine.GameObject target,
            MessageHandler.FastHandler<T> targetedPostProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedPostProcessor(
                        target,
                        targetedPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process TargetedMessages of the given type for the provided target.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target to post process messages for.</param>
        /// <param name="targetedPostProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentTargetedPostProcessor<T>(
            UnityEngine.Component target,
            MessageHandler.FastHandler<T> targetedPostProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedPostProcessor(
                        target,
                        targetedPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }
#else

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargeted<T>(
            InstanceId target,
            Action<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards the provided target.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target of the TargetedMessages to consume.</param>
        /// <param name="targetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargeted<T>(
            InstanceId target,
            MessageHandler.FastHandler<T> targetedHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return RegisterTargetedInternal(target, targetedHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process TargetedMessages of the given type for the provided target.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target to post process messages for.</param>
        /// <param name="targetedPostProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedPostProcessor<T>(
            InstanceId target,
            MessageHandler.FastHandler<T> targetedPostProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedPostProcessor(
                        target,
                        targetedPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process TargetedMessages of the given type for the provided target.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="target">Target to post process messages for.</param>
        /// <param name="targetedPostProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedPostProcessor<T>(
            InstanceId target,
            Action<T> targetedPostProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedPostProcessor(
                        target,
                        targetedPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }
#endif

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="messageHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedWithoutTargeting<T>(
            Action<InstanceId, T> messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedWithoutTargeting(
                        messageHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="messageHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedWithoutTargeting<T>(
            MessageHandler.FastHandlerWithContext<T> messageHandler,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedWithoutTargeting(
                        messageHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to post process TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="postProcessor">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedWithoutTargetingPostProcessor<T>(
            Action<InstanceId, T> postProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedWithoutTargetingPostProcessor(
                        postProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to post process TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="postProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterTargetedWithoutTargetingPostProcessor<T>(
            MessageHandler.FastHandlerWithContext<T> postProcessor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterTargetedWithoutTargetingPostProcessor(
                        postProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept UntargetedMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="untargetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterUntargeted<T>(
            Action<T> untargetedHandler,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterUntargetedMessageHandler(
                        untargetedHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept UntargetedMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="untargetedHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterUntargeted<T>(
            MessageHandler.FastHandler<T> untargetedHandler,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterUntargetedMessageHandler(
                        untargetedHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process UntargetedMessages of the given type.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="untargetedPostProcessor">Actual post processor functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterUntargetedPostProcessor<T>(
            MessageHandler.FastHandler<T> untargetedPostProcessor,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterUntargetedPostProcessor(
                        untargetedPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        private MessageRegistrationHandle RegisterBroadcastInternal<T>(
            InstanceId source,
            Action<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastMessageHandler(
                        source,
                        broadcastHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        private MessageRegistrationHandle RegisterBroadcastInternal<T>(
            InstanceId source,
            MessageHandler.FastHandler<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastMessageHandler(
                        source,
                        broadcastHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        private MessageRegistrationHandle RegisterBroadcastPostProcessorInternal<T>(
            InstanceId source,
            Action<T> broadcastPostProcessor,
            int priority
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastPostProcessor(
                        source,
                        broadcastPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

        private MessageRegistrationHandle RegisterBroadcastPostProcessorInternal<T>(
            InstanceId source,
            MessageHandler.FastHandler<T> broadcastPostProcessor,
            int priority
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastPostProcessor(
                        source,
                        broadcastPostProcessor,
                        priority,
                        _messageBus
                    )
            );
        }

#if UNITY_2017_1_OR_NEWER
        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="source">Id of the source for BroadcastMessages to listen for.</param>
        /// <param name="broadcastHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectBroadcast<T>(
            UnityEngine.GameObject source,
            Action<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority: priority);
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
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectBroadcast<T>(
            UnityEngine.GameObject source,
            MessageHandler.FastHandler<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given GameObject.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectBroadcastPostProcessor<T>(
            UnityEngine.GameObject source,
            Action<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastPostProcessorInternal(source, broadcastPostProcessor, priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given GameObject.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterGameObjectBroadcastPostProcessor<T>(
            UnityEngine.GameObject source,
            MessageHandler.FastHandler<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastPostProcessorInternal(source, broadcastPostProcessor, priority);
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
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentBroadcast<T>(
            UnityEngine.Component source,
            Action<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority);
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
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentBroadcast<T>(
            UnityEngine.Component source,
            MessageHandler.FastHandler<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given component.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentBroadcastPostProcessor<T>(
            UnityEngine.Component source,
            Action<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastPostProcessor(
                        source,
                        broadcastPostProcessor,
                        priority: priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given component.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterComponentBroadcastPostProcessor<T>(
            UnityEngine.Component source,
            MessageHandler.FastHandler<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastPostProcessor(
                        source,
                        broadcastPostProcessor,
                        priority: priority,
                        _messageBus
                    )
            );
        }
#else

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcast<T>(
            InstanceId source,
            Action<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastHandler">Actual handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcast<T>(
            InstanceId source,
            MessageHandler.FastHandler<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastInternal(source, broadcastHandler, priority: priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given source.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastPostProcessor<T>(
            InstanceId source,
            Action<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastPostProcessorInternal(source, broadcastPostProcessor, priority);
        }

        /// <summary>
        /// Stages a registration of the provided PostProcessor to post process BroadcastMessages of the given type for the given source.
        /// </summary>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="source">Source of the messages.</param>
        /// <param name="broadcastPostProcessor">Actual post processor logic.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastPostProcessor<T>(
            InstanceId source,
            MessageHandler.FastHandler<T> broadcastPostProcessor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            return RegisterBroadcastPostProcessorInternal(source, broadcastPostProcessor, priority);
        }
#endif

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="broadcastHandler">Action handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastWithoutSource<T>(
            Action<InstanceId, T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastWithoutSource(
                        broadcastHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="broadcastHandler">Action handler functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastWithoutSource<T>(
            MessageHandler.FastHandlerWithContext<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastWithoutSource(
                        broadcastHandler,
                        priority: priority,
                        messageBus: _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to post process BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="broadcastHandler">Actual post process functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastWithoutSourcePostProcessor<T>(
            Action<InstanceId, T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastWithoutSourcePostProcessor(
                        broadcastHandler,
                        priority: priority,
                        _messageBus
                    )
            );
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to post post process BroadcastMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of the message that the handler accepts.</typeparam>
        /// <param name="broadcastHandler">Actual post process functionality.</param>
        /// <param name="priority">Priority at which to run the handler, lower runs earlier than higher.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        public MessageRegistrationHandle RegisterBroadcastWithoutSourcePostProcessor<T>(
            MessageHandler.FastHandlerWithContext<T> broadcastHandler,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () =>
                    _messageHandler.RegisterSourcedBroadcastWithoutSourcePostProcessor(
                        broadcastHandler,
                        priority: priority,
                        _messageBus
                    )
            );
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
        public MessageRegistrationHandle RegisterGlobalAcceptAll(
            Action<IUntargetedMessage> acceptAllUntargeted,
            Action<InstanceId, ITargetedMessage> acceptAllTargeted,
            Action<InstanceId, IBroadcastMessage> acceptAllBroadcast
        )
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterGlobalAcceptAll(
                        acceptAllUntargeted,
                        acceptAllTargeted,
                        acceptAllBroadcast,
                        _messageBus
                    )
            );
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
        public MessageRegistrationHandle RegisterGlobalAcceptAll(
            MessageHandler.FastHandler<IUntargetedMessage> acceptAllUntargeted,
            MessageHandler.FastHandlerWithContext<ITargetedMessage> acceptAllTargeted,
            MessageHandler.FastHandlerWithContext<IBroadcastMessage> acceptAllBroadcast
        )
        {
            if (_messageHandler == null) // Unity has a bug
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }
            return InternalRegister(
                () =>
                    _messageHandler.RegisterGlobalAcceptAll(
                        acceptAllUntargeted,
                        acceptAllTargeted,
                        acceptAllBroadcast,
                        _messageBus
                    )
            );
        }

        public MessageRegistrationHandle RegisterUntargetedInterceptor<T>(
            IMessageBus.UntargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IUntargetedMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () => _messageHandler.RegisterUntargetedInterceptor(interceptor, priority)
            );
        }

        public MessageRegistrationHandle RegisterBroadcastInterceptor<T>(
            IMessageBus.BroadcastInterceptor<T> interceptor,
            int priority = 0
        )
            where T : IBroadcastMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () => _messageHandler.RegisterBroadcastInterceptor(interceptor, priority)
            );
        }

        public MessageRegistrationHandle RegisterTargetedInterceptor<T>(
            IMessageBus.TargetedInterceptor<T> interceptor,
            int priority = 0
        )
            where T : ITargetedMessage
        {
            if (_messageHandler == null)
            {
                return MessageRegistrationHandle.CreateMessageRegistrationHandle();
            }

            return InternalRegister(
                () => _messageHandler.RegisterTargetedInterceptor(interceptor, priority)
            );
        }

        /// <summary>
        /// Handles the actual [de]registration wrapping and (potential) lazy execution.
        /// </summary>
        /// <param name="registerAndGetDeregistration">Proxied registration function that returns a de-registration function.</param>
        /// <returns>A handle that allows for registration and de-registration.</returns>
        private MessageRegistrationHandle InternalRegister(
            Func<Action> registerAndGetDeregistration
        )
        {
            MessageRegistrationHandle handle =
                MessageRegistrationHandle.CreateMessageRegistrationHandle();

            _registrations[handle] = Registration;

            // Generally, registrations should take place before all calls to enable. Just in case, though...
            if (_enabled)
            {
                Registration();
            }

            return handle;

            // We don't want to actually register at this time (might not be awake/enabled) - so we wrap that shit up, to lazy register when we're enabled.
            void Registration()
            {
                Action actualDeregistration = registerAndGetDeregistration();
                _deregistrations[handle] = actualDeregistration;
            }
        }

        /// <summary>
        /// Enables the token if not already enabled. Executes all staged registrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
        public void Enable()
        {
            if (_enabled)
            {
                return;
            }

            if (_registrations is { Count: > 0 })
            {
                foreach (Action registrationAction in _registrations.Values)
                {
                    registrationAction();
                }
            }

            _enabled = true;
        }

        /// <summary>
        /// Disables the token if not already disabled. Executes all staged de-registrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
        public void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            if (_deregistrations is { Count: > 0 })
            {
                foreach (Action deregistrationAction in _deregistrations.Values)
                {
                    deregistrationAction();
                }
            }

            // ReSharper disable once ForCanBeConvertedToForeach


            _enabled = false;
        }

        /// <summary>
        /// Disables the token and clears all registrations + de-registrations
        /// </summary>
        public void UnregisterAll()
        {
            if (_enabled && _deregistrations is { Count: > 0 })
            {
                foreach (Action deregistrationAction in _deregistrations.Values)
                {
                    deregistrationAction();
                }
            }

            _enabled = false;
            _registrations?.Clear();
            _deregistrations?.Clear();
        }

        public void RemoveRegistration(MessageRegistrationHandle handle)
        {
            if (
                _deregistrations != null
                && _deregistrations.TryGetValue(handle, out Action deregistrationAction)
            )
            {
                deregistrationAction();
                _ = _deregistrations.Remove(handle);
            }

            _ = _registrations?.Remove(handle);
        }

        /// <summary>
        /// Creates a MessagingRegistrationToken that operates on the given handler.
        /// </summary>
        /// <param name="messageHandler">Message handler to register handlers to.</param>
        /// <param name="messageBus">MessageBus to use for this MessageRegistrationToken. Uses the GlobalMessageBus if left null.</param>
        /// <returns>MessagingRegistrationToken bound to the MessageHandler.</returns>
        public static MessageRegistrationToken Create(
            MessageHandler messageHandler,
            IMessageBus messageBus = null
        )
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            return new MessageRegistrationToken(messageHandler, messageBus);
        }
    }
}

namespace DxMessaging.Core.Helper
{
    using System;
    using System.Collections.Generic;

    public static class MessageHelperIndexer
    {
        internal readonly struct MessageHelperIndexerState
        {
            internal readonly int _totalMessages;

            internal readonly Dictionary<Type, int> _sequentialIds;

            internal MessageHelperIndexerState(
                int totalMessages,
                Dictionary<Type, int> sequentialIds
            )
            {
                _totalMessages = totalMessages;
                _sequentialIds = sequentialIds;
            }
        }

        private static readonly object ResetLock = new();
        private static readonly HashSet<Type> RegisteredTypes = new();
        private static readonly Dictionary<Type, Func<int, int>> StateManipulationByType = new();

        internal static int TotalMessages = 0;

        internal static void RegisterType(Type messageType, Func<int, int> idProducer)
        {
            if (messageType == null)
            {
                return;
            }

            lock (ResetLock)
            {
                if (!RegisteredTypes.Add(messageType))
                {
                    return;
                }

                StateManipulationByType[messageType] = idProducer;
            }
        }

        internal static MessageHelperIndexerState CaptureState()
        {
            lock (ResetLock)
            {
                Dictionary<Type, int> snapshot = new(RegisteredTypes.Count);
                return new MessageHelperIndexerState(TotalMessages, snapshot);
            }
        }

        internal static void RestoreState(MessageHelperIndexerState state)
        {
            lock (ResetLock)
            {
                TotalMessages = state._totalMessages;
                foreach (KeyValuePair<Type, Func<int, int>> entry in StateManipulationByType)
                {
                    Type type = entry.Key;
                    Func<int, int> manipulationAction = entry.Value;
                    if (
                        state._sequentialIds == null
                        || !state._sequentialIds.TryGetValue(type, out int value)
                    )
                    {
                        value = -1;
                    }

                    manipulationAction(value);
                }
            }
        }
    }

    public static class MessageHelperIndexer<TMessage>
        where TMessage : IMessage
    {
        // ReSharper disable once StaticMemberInGenericType
        internal static int SequentialId = -1;

        static MessageHelperIndexer()
        {
            MessageHelperIndexer.RegisterType(typeof(TMessage), value => SequentialId = value);
        }
    }
}

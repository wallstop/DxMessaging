namespace DxMessaging.Core.Helper
{
    public sealed class MessageCache<TValue>
        where TValue : class, new()
    {
        private readonly TValue[] _values = new TValue[DxMessagingRuntime.TotalMessageTypes];

        public TValue GetOrAdd<TMessage>()
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            TValue value = _values[index];
            if (value != null)
            {
                return value;
            }

            value = new TValue();
            _values[index] = value;
            return value;
        }

        public void Remove<TMessage>()
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            _values[index] = null;
        }

        public TValue this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }
    }
}

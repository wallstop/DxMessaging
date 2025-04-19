namespace DxMessaging.Core.Helper
{
    using System.Collections;
    using System.Collections.Generic;

    public sealed class MessageCache<TValue> : IEnumerable<TValue>
        where TValue : class, new()
    {
        public struct MessageCacheEnumerator : IEnumerator<TValue>
        {
            private readonly MessageCache<TValue> _cache;

            private int _index;
            private TValue _current;

            internal MessageCacheEnumerator(MessageCache<TValue> cache)
            {
                _cache = cache;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                while (++_index < _cache._values.Length)
                {
                    _current = _cache._values[_index];
                    if (_current != null)
                    {
                        return true;
                    }
                }

                _current = default;
                return false;
            }

            public TValue Current => _current;

            object IEnumerator.Current => Current;

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }

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

        public void Set<TMessage>(TValue value)
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            _values[index] = value;
        }

        public bool TryGetValue<TMessage>(out TValue value)
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            value = _values[index];
            return value != null;
        }

        public void Remove<TMessage>()
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            _values[index] = null;
        }

        public MessageCacheEnumerator GetEnumerator()
        {
            return new MessageCacheEnumerator(this);
        }

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

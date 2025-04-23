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
                while (++_index < _cache._values.Count)
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

        private readonly List<TValue> _values = new();

        public TValue GetOrAdd<TMessage>()
            where TMessage : IMessage
        {
            TValue value;
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index)
            {
                while (_values.Count <= index)
                {
                    _values.Add(null);
                }
                value = _values[index];
                if (value != null)
                {
                    return value;
                }

                value = new TValue();
                _values[index] = value;
            }
            else
            {
                index = MessageHelperIndexer.TotalMessages++;
                MessageHelperIndexer<TMessage>.SequentialId = index;
                while (_values.Count < index)
                {
                    _values.Add(null);
                }
                value = new TValue();
                _values.Add(value);
            }

            return value;
        }

        public void Set<TMessage>(TValue value)
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index)
            {
                while (_values.Count <= index)
                {
                    _values.Add(null);
                }
                _values[index] = value;
                return;
            }

            index = MessageHelperIndexer.TotalMessages++;
            MessageHelperIndexer<TMessage>.SequentialId = index;
            while (_values.Count < index)
            {
                _values.Add(null);
            }
            _values.Add(value);
        }

        public bool TryGetValue<TMessage>(out TValue value)
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index && index < _values.Count)
            {
                value = _values[index];
                return value != null;
            }

            value = default;
            return false;
        }

        public void Remove<TMessage>()
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index && index < _values.Count)
            {
                _values[index] = null;
            }
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

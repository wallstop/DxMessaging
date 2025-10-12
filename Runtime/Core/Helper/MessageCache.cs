namespace DxMessaging.Core.Helper
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Sparse, type-indexed cache keyed by message type for fast lookups without dictionaries.
    /// </summary>
    /// <remarks>
    /// Internally maintains a list indexed by a compact, per-message-type integer (assigned via
    /// <see cref="MessageHelperIndexer{TMessage}"/>). Used heavily by the bus to store handlers and interceptors
    /// with minimal overhead.
    /// </remarks>
    public sealed class MessageCache<TValue> : IEnumerable<TValue>
        where TValue : class, new()
    {
        /// <summary>
        /// Enumerator over non-null values in the cache.
        /// </summary>
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                List<TValue> values = _cache._values;
                int count = values.Count;
                while (++_index < count)
                {
                    _current = values[_index];
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }

        private readonly List<TValue> _values = new();

        /// <summary>
        /// Retrieves the value associated with <typeparamref name="TMessage"/>, creating one if needed.
        /// </summary>
        /// <typeparam name="TMessage">Message type key.</typeparam>
        /// <returns>Existing or newly created value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd<TMessage>()
            where TMessage : IMessage
        {
            TValue value;
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index)
            {
                FillToIndex(index);
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
                FillToIndex(index - 1);
                value = new TValue();
                _values.Add(value);
            }

            return value;
        }

        /// <summary>
        /// Sets the value for the given <typeparamref name="TMessage"/> key.
        /// </summary>
        /// <typeparam name="TMessage">Message type key.</typeparam>
        /// <param name="value">Value to store.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set<TMessage>(TValue value)
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index)
            {
                FillToIndex(index);
                _values[index] = value;
                return;
            }

            index = MessageHelperIndexer.TotalMessages++;
            MessageHelperIndexer<TMessage>.SequentialId = index;
            FillToIndex(index - 1);
            _values.Add(value);
        }

        /// <summary>
        /// Attempts to get the value for the given <typeparamref name="TMessage"/> key.
        /// </summary>
        /// <typeparam name="TMessage">Message type key.</typeparam>
        /// <param name="value">Out parameter receiving the value if present.</param>
        /// <returns>True if a non-null value was present.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        /// <summary>
        /// Removes the value for the given <typeparamref name="TMessage"/> key.
        /// </summary>
        /// <typeparam name="TMessage">Message type key.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<TMessage>()
            where TMessage : IMessage
        {
            int index = MessageHelperIndexer<TMessage>.SequentialId;
            if (0 <= index && index < _values.Count)
            {
                _values[index] = null;
            }
        }

        /// <summary>
        /// Returns an enumerator iterating over non-null entries in insertion order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillToIndex(int index)
        {
            int count = _values.Count;
            for (int i = count; i <= index; ++i)
            {
                _values.Add(null);
            }
        }
    }
}

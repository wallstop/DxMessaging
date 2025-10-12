namespace DxMessaging.Core
{
    using System;
    using System.Threading;

    /// <summary>
    /// Opaque identifier for a staged registration within a <see cref="MessageRegistrationToken"/>.
    /// </summary>
    /// <remarks>
    /// Use with <see cref="MessageRegistrationToken.RemoveRegistration"/> to selectively cancel a registration.
    /// </remarks>
    public readonly struct MessageRegistrationHandle
        : IEquatable<MessageRegistrationHandle>,
            IComparable<MessageRegistrationHandle>,
            IComparable
    {
        private static long StaticIdCount;

        private readonly long _id;
        private readonly int _hashCode;

        /// <summary>
        /// Creates a new unique handle.
        /// </summary>
        public static MessageRegistrationHandle CreateMessageRegistrationHandle()
        {
            return new MessageRegistrationHandle(Interlocked.Increment(ref StaticIdCount));
        }

        private MessageRegistrationHandle(long id)
        {
            _id = id;
            _hashCode = _id.GetHashCode();
        }

        public static bool operator ==(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return !left.Equals(right);
        }

        public static bool operator >(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(
            MessageRegistrationHandle left,
            MessageRegistrationHandle right
        )
        {
            return left.CompareTo(right) >= 0;
        }

        public int CompareTo(MessageRegistrationHandle other)
        {
            return _id.CompareTo(other._id);
        }

        public int CompareTo(object obj)
        {
            if (obj is MessageRegistrationHandle handle)
            {
                return CompareTo(handle);
            }

            return -1;
        }

        public override bool Equals(object other)
        {
            return other is MessageRegistrationHandle handle && Equals(handle);
        }

        public bool Equals(MessageRegistrationHandle other)
        {
            return _id == other._id;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override string ToString()
        {
            return new { Id = _id }.ToString();
        }
    }
}

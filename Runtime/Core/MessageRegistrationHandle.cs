namespace DxMessaging.Core
{
    using System;
    using System.Threading;

    public readonly struct MessageRegistrationHandle : IEquatable<MessageRegistrationHandle>, IComparable<MessageRegistrationHandle>
    {
        private static long StaticIdCount;

        private readonly Guid _handle;
        private readonly long _id;
        private readonly int _hashCode;

        public static MessageRegistrationHandle CreateMessageRegistrationHandle()
        {
            return new MessageRegistrationHandle(Guid.NewGuid(), Interlocked.Increment(ref StaticIdCount));
        }

        private MessageRegistrationHandle(Guid handle, long id)
        {
            _handle = handle;
            _id = id;
            _hashCode = _handle.GetHashCode();
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object other)
        {
            return other is MessageRegistrationHandle handle && Equals(handle);
        }

        public bool Equals(MessageRegistrationHandle other)
        {
            return _id == other._id && _handle.Equals(other._handle);
        }

        public int CompareTo(MessageRegistrationHandle other)
        {
            return _id.CompareTo(other._id);
        }

        public override string ToString()
        {
            return new
            {
                Handle = _handle.ToString(),
                Id = _id,
            }.ToString();
        }
    }
}

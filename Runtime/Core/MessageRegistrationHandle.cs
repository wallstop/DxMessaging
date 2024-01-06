namespace DxMessaging.Core
{
    using System;
    using System.Threading;

    public static class MessageRegistrationHandleData
    {
        public static long IdCount = 0;
    }

    public readonly struct MessageRegistrationHandle : IEquatable<MessageRegistrationHandle>, IComparable<MessageRegistrationHandle>
    {
        private readonly Guid _handle;
        private readonly long _id;
        private readonly int _hashCode;

        public static MessageRegistrationHandle CreateMessageRegistrationHandle()
        {
            return new MessageRegistrationHandle(Guid.NewGuid(), Interlocked.Increment(ref MessageRegistrationHandleData.IdCount));
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
    }
}

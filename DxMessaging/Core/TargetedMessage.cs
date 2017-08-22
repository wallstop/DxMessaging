using System;

namespace DxMessaging.Core
{
    /**
        <summary>
            MessageBase for types of messages that are generally meant for a single consumer.
        </summary>
    */
    [Serializable]
    public abstract class TargetedMessage : AbstractMessage, IEquatable<TargetedMessage>
    {

        public InstanceId Target; // Should never be changed - public and non-readonly for serialization purposes

        protected TargetedMessage(InstanceId target)
        {
            Target = target;
        }

        public override bool Equals(object obj)
        {
            TargetedMessage other = obj as TargetedMessage;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return Target.GetHashCode();
        }

        public bool Equals(TargetedMessage other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return Target.Equals(other.Target);
        }
    }
}

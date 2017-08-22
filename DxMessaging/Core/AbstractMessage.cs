using System;

namespace DxMessaging.Core
{
    /**
        <summary>
            An abstract base class lets us re-use some functionality in MessageBus/handler implementations.
        </summary>
    */
    [Serializable]
    public abstract class AbstractMessage
    {
        [NonSerialized] private string _simpleTypeName;

        public string TypeName
        {
            get
            {
                if (ReferenceEquals(_simpleTypeName, null))
                {
                    _simpleTypeName = GetType().Name;
                }
                return _simpleTypeName;
            }
        }

        protected AbstractMessage() { }
    }
}

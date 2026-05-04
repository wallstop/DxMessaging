namespace DxMessaging.Core.MessageBus.Internal
{
    using System;

    /// <summary>
    /// Describes a <see cref="DxMessaging.Core.MessageBus.MessageBus"/> cache storage field that
    /// participates in explicit or idle sweep coverage.
    /// </summary>
    internal interface ISweepable
    {
        string StorageFieldName { get; }
        Type StorageFieldType { get; }
        int Sweep(DxMessaging.Core.MessageBus.MessageBus bus, bool force);
    }
}

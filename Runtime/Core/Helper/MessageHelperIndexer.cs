namespace DxMessaging.Core.Helper
{
    public static class MessageHelperIndexer
    {
        internal static int TotalMessages = 0;
    }

    public static class MessageHelperIndexer<TMessage>
        where TMessage : IMessage
    {
        // ReSharper disable once StaticMemberInGenericType
        internal static int SequentialId = -1;
    }
}

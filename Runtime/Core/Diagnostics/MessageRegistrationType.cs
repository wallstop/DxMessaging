namespace DxMessaging.Core.Diagnostics
{
    /// <summary>
    /// Categories used when recording registrations in diagnostics logs.
    /// </summary>
    public enum MessageRegistrationType
    {
        /// <summary>
        /// No registration type was captured.
        /// </summary>
        None = 0,

        /// <summary>
        /// A targeted handler that listens for messages addressed to a specific <see cref="Core.InstanceId"/>.
        /// </summary>
        Targeted = 1,

        /// <summary>
        /// A global untargeted handler that receives all messages of a given type.
        /// </summary>
        Untargeted = 2,

        /// <summary>
        /// A broadcast handler that listens for messages emitted from a source <see cref="Core.InstanceId"/>.
        /// </summary>
        Broadcast = 3,

        /// <summary>
        /// A broadcast post-processor that runs after broadcast handlers complete.
        /// </summary>
        BroadcastPostProcessor = 4,

        /// <summary>
        /// A targeted post-processor that runs after targeted handlers complete.
        /// </summary>
        TargetedPostProcessor = 5,

        /// <summary>
        /// A targeted handler that ignores the concrete target during invocation.
        /// </summary>
        TargetedWithoutTargeting = 6,

        /// <summary>
        /// A post-processor for handlers registered without a concrete target.
        /// </summary>
        TargetedWithoutTargetingPostProcessor = 7,

        /// <summary>
        /// A broadcast handler registered without an explicit source identity.
        /// </summary>
        BroadcastWithoutSource = 8,

        /// <summary>
        /// A post-processor for broadcast handlers registered without an explicit source.
        /// </summary>
        BroadcastWithoutSourcePostProcessor = 9,

        /// <summary>
        /// A post-processor that runs after untargeted handlers complete.
        /// </summary>
        UntargetedPostProcessor = 10,

        /// <summary>
        /// A global catch-all registration that observes every message.
        /// </summary>
        GlobalAcceptAll = 11,

        /// <summary>
        /// An untargeted interceptor that can mutate or cancel global messages.
        /// </summary>
        UntargetedInterceptor = 12,

        /// <summary>
        /// A targeted interceptor that can mutate or cancel messages bound to a specific recipient.
        /// </summary>
        TargetedInterceptor = 13,

        /// <summary>
        /// A broadcast interceptor that can mutate or cancel messages emitted from a source.
        /// </summary>
        BroadcastInterceptor = 14,
    }
}

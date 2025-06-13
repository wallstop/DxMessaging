namespace DxMessaging.Core.Diagnostics
{
    public enum MessageRegistrationType
    {
        None = 0,
        Targeted = 1,
        Untargeted = 2,
        Broadcast = 3,
        BroadcastPostProcessor = 4,
        TargetedPostProcessor = 5,
        TargetedWithoutTargeting = 6,
        TargetedWithoutTargetingPostProcessor = 7,
        BroadcastWithoutSource = 8,
        BroadcastWithoutSourcePostProcessor = 9,
        UntargetedPostProcessor = 10,
        GlobalAcceptAll = 11,
        UntargetedInterceptor = 12,
        TargetedInterceptor = 13,
        BroadcastInterceptor = 14,
    }
}

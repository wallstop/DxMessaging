# DxMessaging
Game engine agnostic robust, synchronous pub/sub C# messaging solution geared towards Entity-Component systems (but mostly geared towards Unity/XNA/Monogame).

# Overview
Are you tired of tightly coupled components? Making calls to your achievement system from deep within your player code? Or maybe you're working with a more traidtional messaging system, but keep forgetting all the pesky message codes? Fear no more, DxMessaging is here to help.

## But how?
DxMessaging is an implementation of the [Component-Messaging pattern](http://gameprogrammingpatterns.com/component.html#how-do-components-communicate-with-each-other) that is focused around Message **types**. This allows for absolute maximum IDE friendliness (find all usages), as well as human friendliness. Defining a new MessageType is as easy as defining a new class that inherits off of one of the two existing base Message classes ([Targeted](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/TargetedMessage.cs) and [Untargeted](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/UntargetedMessage.cs)). Nothing else is required.


## Types of Messages

### Targeted Messages
Targeted Messages can be though of as commands, meant to transmit intent, and let the receiver interpret the how. Specifically, these are messages intended for a particular entity.

Consider the below sample of damage code, without DxMessaging:
```csharp
public void OnCollisionEnter2D(Collision2D other) {
    DamagableComponent maybeDamagable = other.gameObject.GetComponent<DamagableComponent>;
    if (maybeDamagable) {
        maybeDamagable.Damage(2f, this);
        if (PlayerService.IsPlayer(maybeDamagable.gameObject)) {
            AchievementService.LogDamageToPlayer(maybeDamagable.gameObject, 2f);
        }
    }
}
```

The above code, or something like it, must be boilerplated at every code site that causes damage. With DxMessaging, this could become:

```csharp
public void OnCollisionEnter2D(Collision2D other) {
    new DamageMessage(other.gameObject.GetInstanceId(), 2f).EmitTargeted();
}

// In DamagableComponent
public void HandleDamage(DamageMessage damageMessage) {
    Damage(damageMessage.Amount);
}

// In AchievementService
public void HandleDamage(DamageMessage damageMessage) {
    if (PlayerService.IsPlayerId(damageMessage.Target)) {
        LogDamageToPlayerId(damageMessage.Target, damageMessage.Amount);
    }
}
```

While I did leave out a small amount of code to register the AchievementService and DamagableComponent to receive the Messages, the below is a much clearer separation of concerns. Notice how the DamageMessage can be broadcast unconditionally - it will only be handled by components/entities that have opted to listen for it. New classes and code can be added easily that subscribe to the message, without ever touching the code where the DamageMessage is emitted.

Notice how the AchievementService also listens to the DamageMessage targeted at the specific DamagableComponent - this goes against what I mentioned above, that only the target will receive it! Never fear, more on this later.

### Untargeted Messages
Untargeted Messages can be though of as Events. Ie, "a thing happened, but not to anyone in particular". Things like the level changing, entities being spawned, created, destroyed, a UI button being clicked.

A contrived example without DxMessaging:
```csharp
// In EntityComponent
public void OnDeath() {
    PlayDeathAudioFx();
    SpawnDeathParticles();
    if (!PlayerService.IsPlayer(this)) {
        AchievementService.LogEntityDead(this);
    }
}
```

With DxMessaging:
```csharp
// In EntityComponent
public void OnDeath() {
    new EntityDeathMessage(this).EmitUntargeted();
}

// In AchievementService
public void HandleEntityDeath(EntityDeathMessage deathMessage) {
    if (!PlayerService.IsPlayer(this)) {
        LogEntityDead(this);
    }
}

// In AudioService
public void HandleEntityDeath(EntityDeathMessage deathMessage) {
    deathMessage.Entity.PlayDeathAudioFx();
}

// In ParticleService
public void HandleEntityDeath(EntityDeathMessage deathMessage) {
    deathMessage.Entity.SpawnDeathParticles();
}
```

Again, contrived, and the Particles/Audio decoupling could be considered unecessary, but hey, it's an example :^)

There are four ways that components/entities can receive messages.

### 1. [Targeted]((https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageBus/IMessageBus.cs#L26)) (direct)
If subscribed for a targeted message, only instances of that message that are sent to YOU (represented by the [owner of your MessageHandler](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageHandler.cs#L35)) will be handled. You will never have to check for "am I the thing that this message is targeted?"

### 2. [Targeted but without targeting]((https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageBus/IMessageBus.cs#L35)) (spying)
Remember the AchivementService example above? It was listening for damage that the player took, but the DamageMessage was targeted! If registered to listen to targeted messages without targeting, you will receive all instances of the targeted message, even if they are not for you. IMPORTANT NOTE: A single MessageHandler can be registered to both Targeted AND TargetedWithoutTargeting, and the Handler will behave as expected - calling the targeted function for those messages that are targeted to the handler's owner, and the TargetedWithoutTargeting for everything else.

This is especially useful for Achievement-style components/services.

### 3. [Untargeted](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageBus/IMessageBus.cs#L17)
All untargeted messages of the specific type, nothing really else to say here.

### 4. [Gosh-darn everything](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageBus/IMessageBus.cs#L43)
The MessageHandler will receive every single message that ever lives. This should be used very sparingly, but is useful for debug purposes, or if you wanted something like a query-cache of previous messages.

## Baby's first Messaging Code
In order to get started with DxMessaging, you'll need a few things:

* One [MessageHandler](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageHandler.cs) per GameObject. For those that are using Unity, this is provided via the [MessagingComponent](https://github.com/wallstop/DxMessaging/blob/master/Unity/DxMessagingUnity/Assets/Scripts/MessagingComponent.cs). For other GameEngine users, a potentially custom class may need to be written to wrap the MessageHandler, or it could be stored at the root GameObject. Up to you.
* Custom message types. You'll need to define your own MessageTypes that inherit off of [TargetedMessage](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/TargetedMessage.cs) or [UntargetedMessage](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/UntargetedMessage.cs).
* Subscriber code. This has been hopefully simplified for components that can be enabled/disabled via the [MessagingRegistrationToken](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageRegistrationToken.cs), which is some light abstractions on top of MessageHandler that will properly register + deregister components when enabled/disabled.
* For TargetingMessaging to work, each GameObject (or MessageHandler, since there's a 1:1 mapping) needs to be identified via a unique [InstanceId](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/InstanceId.cs). For Unity, there are overloads that will implicitly convert a gameObject.GetInstanceId() to InstanceId. For everyone else, generating a unique long shouldn't be too much trouble.

Sample code (Unity):
```csharp

public sealed class DamageMessage : TargetedMessage {

    public float Amount;

    public DamageMessage(InstanceId target, float amount)
        : base(target) {
        Amount = amount;
    }
}

public sealed class EntityDeathMessage : UntargetedMessage {

    public InstanceId DeadEntity;

    public EntityDeathMessage(InstanceId deadEntity) {
        DeadEntity = deadEntity;
    }
}

public sealed class DamageComponent : MonoBehaviour {

    public float Amount;

    private void OnCollisionEnter2D(Collision2D other) {
        new DamageMessage(other.gameObject.GetInstanceId(), Amount).EmitTargeted();
    }
}

public sealed class HealthComponent : MessageAwareComponent {

    public float Health;

    protected override void RegisterMessageHandlers() {
        MessageRegistrationToken.RegisterTargeted<DamageMessage>(HandleDamage);
    }

    private void HandleDamage(DamageMessage damageMessage) {
        Health -= damageMessage.Amount;
        if (Health < 0) {
            new EntityDeathMessage(gameObject.GetInstanceId());
        }
        gameObject.setActive(false);
    }
}

public sealed class AchievementService : MessageAwareComponent {

    protected override void RegisterMessageHandlers() {
        MessageRegistrationToken.RegisterUntargeted<EntityDeathMessage>(HandleEntityDeath);
        MessageRegistrationToken.RegisterTargetedWithoutTargeting<DamageMessage>(HandleDamage);
    }

    // Because we registered this to listen for Targeted without Targeting,
    // any entity that receives DamageMessages will have their messages routed here as well.
    private void HandleDamage(DamageMessage damageMessage) {
        Console.WriteLine("{0} took {1:0.00} points of damage.", damageMessage.Target, damageMessage.Amount);
    }

    private void HandleEntityDeath(EntityDeathMessage deathMessage) {
        Console.WriteLine("{0} died, sad.", deathMessage.DeadEntity);
    }
}
```

# Guidelines

* Message sending is sychronous. That means that as soon as the call to .Emit[Targted/Untargeted/Untyped] is called, all relevant handlers will be invoked.
* Inheritance heirarchy should be ONE DEEP from TargetedMessage and UntargetedMessage. Generically handling parent/children messages can get kind of wonky, due to implementation details.
* Messages should be [POD (Plain Old Data)](https://en.wikipedia.org/wiki/Passive_data_structure). Any fancy logic should be handled by senders/receivers.
* [EmitUntyped](https://github.com/wallstop/DxMessaging/blob/master/DxMessaging/Core/MessageHandler.cs#L340) exists in the case where the exact type of the message is not known. This could be some generic sender / handler, like:

```csharp
protected AbstractMessage DetermineCommandToSendBasedOnGameState() {
    // Fancy logic that returns 1 of N types of Messages
}
```
This is *SLOW*, as it requires some reflection to work properly. Would recommend staying away from this.

# Gotchas / Neato 

* DxMessaging is implemented almost entirely using generics.
* Each Message emission causes some garbage to be created, for both the message, as well as all of the listeners. This hasn't caused any perf concerns yet.
* DxMessaging is not thread safe.

# FAQs

* TBD

# TODO

* Start porting my tests over

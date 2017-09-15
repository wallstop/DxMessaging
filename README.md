# DxMessaging
Game engine agnostic robust, synchronous pub/sub C# messaging solution (but mostly geared towards Unity/XNA/Monogame).

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



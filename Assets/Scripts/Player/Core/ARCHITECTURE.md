# Player Controller Architecture

## Overview

The Player Controller is a **unified, subsystem-based architecture** that orchestrates all player-related gameplay systems. It replaces the scattered manager pattern with clear separation of concerns and event-driven communication.

## Architecture Diagram

```
PlayerController (Main Orchestrator)
├── IMovementSubsystem
│   ├── Movement input handling
│   ├── Gravity & physics
│   ├── FPP/TPP perspective switching
│   └── Ground detection
│
├── IAnimationSubsystem
│   ├── Animator parameter synchronization
│   ├── Animation state management
│   └── Sync with movement & combat
│
├── IStatsSubsystem
│   ├── Health management
│   ├── Energy system
│   ├── Energy regeneration
│   └── Damage/Healing
│
├── ICombatSubsystem
│   ├── Combat state machine (Normal, Fire, etc.)
│   ├── Ability unlock tracking
│   ├── Weapon/ability firing
│   └── State transitions
│
└── IInteractionSubsystem (NEW)
    ├── World object interaction
    ├── Interactable detection
    ├── Prompt management
    └── Interaction events
```

## Key Components

### PlayerController (Orchestrator)

**File:** `PlayerController.cs`

The centralized hub that:
- Initializes and manages all subsystems
- Handles lifecycle (initialization, update, cleanup)
- Coordinates events between subsystems
- Provides single entry point for player logic
- Can be disabled/enabled (pause, death)

**Key Methods:**
```csharp
var controller = container.Resolve<PlayerController>();
controller.Initialize();  // Init all subsystems
controller.Movement.SetDash(true);  // Access subsystems
controller.Stats.TakeDamage(10);    // Call subsystem methods
controller.OnPlayerDeath += HandleDeath;  // Subscribe to events
```

### IMovementSubsystem

**File:** `Subsystems/MovementSubsystem.cs`

Handles locomotion and physics:
- Ground detection via raycasting
- Gravity simulation
- Third-person and first-person movement
- Perspective switching with smooth transitions
- Dash state management

**Events:**
- `OnMoveSpeedChanged(float)` → Animator updates
- `OnDashChanged(bool)` → Dash visual feedback
- `OnPerspectiveChanged(bool)` → Camera transitions

### IAnimationSubsystem

**File:** `Subsystems/AnimationSubsystem.cs`

Synchronizes animator parameters:
- Movement speed blending
- Direction values for locomotion animations
- Aim/combat stance
- Dash animations
- Generic trigger support for special animations

**Connected to:**
- MovementSubsystem (receives speed/direction)
- CombatSubsystem (receives shoot events)
- CameraManager (receives aim events)

### IStatsSubsystem

**File:** `Subsystems/StatsSubsystem.cs`

Manages player health and energy:
- Health tracking and damage calculation
- Energy consumption and regeneration
- Auto-regeneration with delay
- Death state

**Events:**
- `OnHealthChanged(float)` → UI updates
- `OnEnergyChanged(float)` → UI updates
- `OnDeath()` → Player death handler

### ICombatSubsystem

**File:** `Subsystems/CombatSubsystem.cs`

Manages combat states and abilities:
- Combat state machine (Normal, Fire, etc.)
- Ability unlock system
- Shooting/ability triggering
- State transitions

**TODO:** Integrate with existing `PlayerStatesManager`

### IInteractionSubsystem (NEW)

**File:** `Subsystems/InteractionSubsystem.cs`

NEW subsystem for world interactions:
- Interactable object detection
- Interaction prompt management
- Interaction event dispatching
- Can be extended for puzzles, NPCs, collectibles

**Related Interfaces:**
- `IInteractable` - Implement on doors, puzzles, NPCs

## Usage Examples

### Adding a New Ability

```csharp
// In your ability unlock system:
playerController.Combat.UnlockAbility("FireMode");
playerController.Combat.OnAbilityUnlocked += (ability) => 
{
    Debug.Log($"Unlocked: {ability}");
};
```

### Handling Player Death

```csharp
playerController.OnPlayerDeath += () => 
{
    // Show death screen
    // Stop UI updates
    // Trigger respawn
};
```

### Creating an Interactable Object

```csharp
public class DoorObject : MonoBehaviour, IInteractable
{
    public string InteractionPrompt => "Press E to open door";
    public bool CanInteract => !isOpen;
    
    public void OnInteract(PlayerController player)
    {
        if (player.Stats.TryConsumeEnergy(10))
        {
            OpenDoor();
        }
    }
}
```

### Consuming Energy for an Ability

```csharp
public class DashAbility
{
    public void Execute(PlayerController player)
    {
        if (!player.Combat.IsAbilityUnlocked("Dash"))
            return;
            
        if (!player.Stats.TryConsumeEnergy(_dashCost))
            return;  // Not enough energy
            
        player.Movement.SetDash(true);
    }
}
```

## Event Flow Example: Player Moves

```
1. InputManager detects WASD input
2. MovementSubsystem.Tick() reads input
3. MovementSubsystem calculates movement vector
4. MovementSubsystem.FixedTick() applies velocity
5. MovementSubsystem fires OnMoveSpeedChanged event
6. AnimationSubsystem receives event, updates Animator
7. Animator blends locomotion animations
8. Visual feedback to player
```

## Event Flow Example: Player Takes Damage

```
1. Enemy.AttackPlayer()
2. Calls playerController.Stats.TakeDamage(10)
3. StatsSubsystem reduces health
4. StatsSubsystem fires OnHealthChanged event
5. PlayerHudManager (subscribed) updates health bar UI
6. If health <= 0, StatsSubsystem fires OnDeath
7. PlayerController handles death, disables input
8. PlayerController fires OnPlayerDeath event
9. GameManager shows death screen
```

## Migration from Old System

### Old vs New

| Old | New |
|-----|-----|
| `PlayerMovementManager` | `MovementSubsystem` |
| `PlayerAnimatorManager` | `AnimationSubsystem` |
| `PlayerDataManager` | `StatsSubsystem` |
| `PlayerStatesManager` | `CombatSubsystem` |
| `PlayerHudManager` | Subscribes to `StatsSubsystem` events |
| N/A | `InteractionSubsystem` (new!) |

### Phased Migration

1. **Phase 1:** Create new subsystems alongside old managers
2. **Phase 2:** Test subsystem integration with existing systems
3. **Phase 3:** Gradually migrate UI to listen to subsystem events
4. **Phase 4:** Remove old managers once fully integrated
5. **Phase 5:** Extend with new features (interactions, etc.)

## Testing

### Unit Test Example

```csharp
[Test]
public void MovementSubsystem_WithZeroInput_HasZeroVelocity()
{
    var movement = new MovementSubsystem(...);
    movement.Initialize();
    
    // Mock input to return zero
    inputManagerMock.Setup(x => x.GetMovementAxis())
        .Returns(Vector2.zero);
    
    movement.Tick();
    
    Assert.AreEqual(Vector3.zero, movement.MoveVector);
}

[Test]
public void StatsSubsystem_TakeDamage_ReducesHealth()
{
    var stats = new StatsSubsystem(configMock);
    stats.Initialize();
    float startHealth = stats.CurrentHealth;
    
    stats.TakeDamage(10);
    
    Assert.AreEqual(startHealth - 10, stats.CurrentHealth);
}
```

## Future Enhancements

1. **InventorySubsystem** - Items and collectibles
2. **DialogueSubsystem** - NPC conversations
3. **StatusEffectSubsystem** - Buffs/debuffs
4. **SoundSubsystem** - Unified audio management
5. **SaveSystem** - Player state serialization

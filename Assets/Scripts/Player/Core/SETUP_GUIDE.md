# Player Controller Setup Guide

## Quick Start

### Step 1: Create PlayerControllerConfig

1. Right-click in Project window
2. Create → Configs → Player → Controller Config
3. Name it `PlayerControllerConfig`
4. Assign references:
   - **Player Game Object** - Your player GameObject
   - **Player Animator** - Your player's Animator component
   - **Camera Transform** - Main camera transform
   - **Ground Check Transform** - Empty object at player's feet for raycasting
   - **Movement Config** - Your existing MovementConfig
   - **Player Data Config** - Your existing PlayerDataConfig
   - **Combat State Configs** - Your existing PlayerStateConfigs
   - **Interaction Range** - Raycast range for interactions (default 5)
   - **Interaction Layer** - Layer mask for interactable objects

### Step 2: Add Installers to Scene

**Option A: Fresh Setup (No existing PlayerStatesManager)**
```
1. Create empty GameObject: "PlayerSystemInstaller"
2. Add PlayerControllerInstaller component
3. Assign PlayerControllerConfig
4. Add PlayerControllerBootstrapper to your player GameObject
```

**Option B: Gradual Migration (Keep PlayerStatesManager)**
```
1. Create empty GameObject: "PlayerSystemInstaller"
2. Add PlayerControllerLegacyAdapterInstaller component
3. Assign PlayerControllerConfig
4. Add PlayerControllerBootstrapper to your player GameObject
5. Keep your existing PlayerStatesManager running
```

### Step 3: Test Basic Functionality

Add this to your player or a test script:

```csharp
private PlayerController _playerController;

[Inject]
public void Construct(PlayerController controller)
{
    _playerController = controller;
}

private void Update()
{
    if (Input.GetKeyDown(KeyCode.T))
    {
        _playerController.Stats.TakeDamage(10);
        Debug.Log($"Health: {_playerController.Stats.CurrentHealth}");
    }
}
```

---

## Troubleshooting

### Error: "PlayerController was not injected"
- **Cause:** PlayerControllerInstaller not in scene or not assigned properly
- **Fix:** Make sure PlayerControllerInstaller is on a GameObject with a Context/SceneContext

### Error: "PlayerControllerConfig is not assigned"
- **Cause:** Missing config reference
- **Fix:** Create PlayerControllerConfig asset and assign it in the installer

### Error: "CharacterController not found"
- **Cause:** Player GameObject doesn't have CharacterController component
- **Fix:** Add CharacterController to your player GameObject

### Movement not working
- **Cause:** Ground check transform not set or wrong layer
- **Fix:** 
  1. Create empty GameObject at player's feet
  2. Assign to PlayerControllerConfig.GroundCheckTransform
  3. Make sure Ground/Laver tags exist on floor objects

### Animation not syncing
- **Cause:** Animator parameters not matching
- **Fix:** Make sure animator has these parameters:
  - `Speed` (float)
  - `DirectionX` (float)
  - `DirectionY` (float)
  - `Dash` (bool)
  - `Aim` (bool)

---

## Integration Examples

### From Enemy Script

```csharp
[Inject] private PlayerController _playerController;

public void AttackPlayer(float damage)
{
    _playerController.Stats.TakeDamage(damage);
}
```

### From Collectible/Loot

```csharp
[Inject] private PlayerController _playerController;

private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player"))
    {
        _playerController.Stats.Heal(10);
        _playerController.Combat.UnlockAbility("Fire");
        Destroy(gameObject);
    }
}
```

### From Door/Puzzle

```csharp
public class Door : MonoBehaviour, IInteractable
{
    public string InteractionPrompt => "Press E to open";
    public bool CanInteract => !_isOpen;
    
    public void OnInteract(PlayerController player)
    {
        if (player.Stats.TryConsumeEnergy(20))
        {
            OpenDoor();
        }
    }
}
```

---

## Switching from Legacy System

### Before (Old Code)
```csharp
[Inject] private PlayerDataManager _dataManager;
[Inject] private PlayerMovementManager _movementManager;
[Inject] private PlayerAnimatorManager _animatorManager;

private void TakeDamage()
{
    _dataManager.TakeDamage(10);
}
```

### After (New Code)
```csharp
[Inject] private PlayerController _playerController;

private void TakeDamage()
{
    _playerController.Stats.TakeDamage(10);
}
```

---

## Next Steps

1. ✅ Set up PlayerControllerConfig
2. ✅ Add installers to scene
3. ✅ Test basic health/energy systems
4. ✅ Integrate with UI (subscribe to events)
5. ✅ Add interactable objects
6. ✅ Gradually migrate existing systems

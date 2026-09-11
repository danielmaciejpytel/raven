# Level1 Scene - Player Controller Migration

## Overview

This guide walks you through updating Level1 to use the new unified **PlayerController** system.

## Quick Setup (Recommended)

### Step 1: Open Level1 Scene
```
Assets/Scenes/Level1.unity
```

### Step 2: Run Automatic Setup
```
In Unity Editor Menu:
Raven → Level1 Setup → Auto Configure Player System
```

Wait for the Console to show:
```
✅ Level1 PlayerController setup complete! Hit Play to test.
```

### Step 3: Validate Configuration
```
Raven → Level1 Setup → Validate Level1 Configuration
```

You should see all green checkmarks in the Console.

### Step 4: Test
```
Hit Play in Editor
```

Expected logs:
```
[Level1PlayerInstaller] ✅ All subsystems registered successfully!
PlayerControllerBootstrapper: Player system initialized successfully!
```

---

## What Changed in Level1

### ✅ NEW Components Added

| GameObject | Component | Purpose |
|---|---|---|
| PlayerSystemInstaller | Level1PlayerInstaller | Dependency Injection |
| Player | PlayerControllerBootstrapper | Lifecycle Management |

### ✅ NEW Configuration Assets Created

```
Assets/Configs/Player/Level1PlayerControllerConfig.asset
```

Contains:
- Player GameObject reference
- Animator reference
- Camera Transform
- Ground Check Transform
- All existing movement/data configs

### ✅ OLD Components Still Working

All existing managers continue to work:
- ❌ PlayerMovementManager → (Replaced by MovementSubsystem, but can coexist)
- ❌ PlayerAnimatorManager → (Replaced by AnimationSubsystem, but can coexist)
- ✅ PlayerDataManager → (Can be removed when ready)
- ✅ PlayerStatesManager → (Can be removed when ready)
- ✅ PlayerHudManager → (Listens to subsystem events)
- ✅ PlayerRigManager → (Still works independently)

---

## Scene Hierarchy After Setup

```
Level1 Scene
├── PlayerSystemInstaller (NEW)
│   └── Level1PlayerInstaller (NEW)
├── Player
│   ├── Animator (existing)
│   ├── CharacterController (existing)
│   ├── PlayerControllerBootstrapper (NEW)
│   ├── GroundCheck (existing or NEW)
│   └── ... other player components
├── MainCamera
│   └── ... existing
├── ... other level objects
```

---

## Troubleshooting

### Issue: "PlayerControllerConfig is not assigned"
**Solution:**
1. Look for "Level1PlayerControllerConfig" in Project
2. Drag it into the Level1PlayerInstaller inspector
3. Or run: `Raven → Level1 Setup → Auto Configure Player System`

### Issue: Player doesn't move
**Cause:** Ground detection failing
**Solution:**
1. Make sure GroundCheck Transform is positioned at player's feet
2. Make sure ground objects have "Ground" or "Laver" tags
3. Check CharacterController is enabled

### Issue: Animation doesn't update
**Cause:** Animator parameters missing
**Solution:**
Make sure your Animator has these parameters:
- `Speed` (float)
- `DirectionX` (float)
- `DirectionY` (float)
- `Dash` (bool)
- `Aim` (bool)

### Issue: Console errors about Zenject
**Cause:** Missing dependency
**Solution:**
1. Run: `Raven → Level1 Setup → Validate Level1 Configuration`
2. Check Console for specific missing components
3. Assign missing references to Level1PlayerControllerConfig

---

## Event Subscription (For UI Updates)

Update your UI managers to listen to subsystem events:

### Before (Old)
```csharp
[Inject] private PlayerDataManager _dataManager;
[Inject] private PlayerMovementManager _movementManager;

private void Start()
{
    _dataManager.OnTakeDamage += () => UpdateHealthBar();
    _movementManager.OnMove += (speed) => UpdateSpeedAnimation(speed);
}
```

### After (New)
```csharp
[Inject] private PlayerController _playerController;

private void Start()
{
    _playerController.Stats.OnHealthChanged += (health) => UpdateHealthBar(health);
    _playerController.Movement.OnMoveSpeedChanged += (speed) => UpdateSpeedAnimation(speed);
}
```

---

## Next Steps

1. ✅ Run auto-setup
2. ✅ Validate configuration
3. ✅ Test in Play mode
4. ⬜ Update UI to subscribe to subsystem events
5. ⬜ Gradually remove old managers
6. ⬜ Add new features (interactions, new abilities)

---

## Need Help?

Run these menu commands:
```
Raven → Level1 Setup → Show Setup Instructions
Raven → Level1 Setup → Validate Level1 Configuration
```

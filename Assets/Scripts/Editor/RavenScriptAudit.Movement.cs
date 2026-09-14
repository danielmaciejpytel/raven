using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Raven.Config;
using Raven.Container;
using Raven.Input;
using Raven.Manager;
using Raven.Player;
using Raven.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

public static partial class RavenScriptAudit
{
    private static IEnumerator CheckMovementAndCombat(SceneContext context)
    {
        var sourceMovement = context.Container.TryResolve<PlayerMovementManager>();
        var sourceStates = context.Container.TryResolve<PlayerStatesManager>();
        var sourceHud = context.Container.TryResolve<PlayerHudManager>();
        Check(sourceMovement != null && sourceStates != null && sourceHud != null, "Movement/combat dependencies resolve");
        if (sourceMovement == null || sourceStates == null || sourceHud == null) yield break;

        float oldTimeScale = Time.timeScale;
        float oldFixedDelta = Time.fixedDeltaTime;
        Gamepad oldGamepad = Gamepad.current;
        var deviceFilters = new List<KeyValuePair<Controls, ReadOnlyArray<InputDevice>?>>();
        MovementAuditFixture fixture = null;
        try
        {
            // Keep the test gamepad out of the live scene's input actions.
            InputDevice[] existingDevices = InputSystem.devices.ToArray();
            foreach (InputManager input in Object.FindObjectsByType<InputManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Controls controls = Field<Controls>(input, "_controls");
                if (controls == null) continue;
                deviceFilters.Add(new KeyValuePair<Controls, ReadOnlyArray<InputDevice>?>(controls, controls.devices));
                controls.devices = existingDevices;
            }

            Time.timeScale = 1f;
            fixture = new MovementAuditFixture(sourceMovement, sourceStates, sourceHud);
            yield return null;

            fixture.PulseDash();
            Check(!fixture.Movement.Dash && fixture.Energy.value == 200f && fixture.DashStarts == 0,
                "Locked dash consumes no energy and emits no dash event");
            fixture.States.ChangeState();
            Check(fixture.States.CurrentConfig == fixture.Normal, "ChangeState cannot enter locked Fire state");
            fixture.States.UnlockState(CollectibleName.Dash);
            fixture.States.UnlockState(CollectibleName.FireState);
            fixture.Energy.value = 0f;
            fixture.PulseDash();
            Check(!fixture.Movement.Dash && fixture.Energy.value == 0f, "Unaffordable dash is rejected");

            foreach (float step in new[] { 0.01f, 0.02f, 0.03f })
            {
                Time.fixedDeltaTime = step;
                fixture.NormalMode();
                fixture.Place(fixture.Spawn);
                fixture.Energy.value = 200f;
                fixture.ResetDashEvents();
                Vector3 start = fixture.Player.transform.position;
                fixture.PulseDash();
                Check(fixture.Movement.Dash && !fixture.Movement.GravityBool &&
                      Mathf.Approximately(fixture.Energy.value, 200f - fixture.Normal.DashCost),
                    $"Normal dash starts once, pays once and immediately suspends gravity (step {step})");
                fixture.Movement.FixedTick();
                fixture.States.ChangeState();
                int steps = 1 + FinishMovementAuditDash(fixture);
                float distance = fixture.Player.transform.position.z - start.z;
                Check(steps == Mathf.CeilToInt(fixture.Normal.DashTime / step) &&
                      Mathf.Abs(distance - fixture.Normal.DashSpeed * fixture.Normal.DashTime) < 0.005f,
                    $"Normal to Fire preserves dash duration and distance (step {step}, steps {steps}, distance {distance:F4})");
                Check(fixture.DashStarts == 1 && fixture.DashEnters == 1 && fixture.DashExits == 1 &&
                      fixture.Movement.GravityBool && fixture.Effects().Length == 0,
                    "Normal dash has one start/end pair and does not gain a Fire effect after switching state");
            }

            fixture.Place(fixture.Spawn);
            fixture.Energy.value = 200f;
            fixture.ResetDashEvents();
            Vector3 fireStart = fixture.Player.transform.position;
            fixture.PulseDash();
            fixture.Movement.FixedTick();
            fixture.States.ChangeState();
            int fireSteps = 1 + FinishMovementAuditDash(fixture);
            DashEffect[] effects = fixture.Effects();
            Check(fireSteps == Mathf.CeilToInt(fixture.Fire.DashTime / Time.fixedDeltaTime) &&
                  Mathf.Abs(fixture.Player.transform.position.z - fireStart.z - fixture.Fire.DashSpeed * fixture.Fire.DashTime) < 0.005f,
                "Fire to Normal preserves Fire dash speed, duration and final partial step");
            Check(effects.Length == 1 && Vector3.Distance(effects[0].transform.position, fixture.Player.transform.position) < 0.001f &&
                  Field<PlayerStateConfig>(effects[0], "_config") == fixture.Fire && fixture.DashState == PlayerStateName.Fire,
                "Fire effect uses the starting config at the final dash position");
            fixture.ClearEffects();

            fixture.States.ChangeState();
            fixture.Place(fixture.Spawn);
            fixture.Energy.value = fixture.Fire.DashCost;
            fixture.PulseDash();
            SetMovementAuditField(fixture.States, "_energySubtractTimer", 1f);
            fixture.States.Tick();
            Check(fixture.States.CurrentConfig == fixture.Normal && fixture.Movement.Dash,
                "Energy exhaustion changes combat state while the paid Fire dash remains active");
            FinishMovementAuditDash(fixture);
            Check(fixture.Effects().Length == 1, "Energy exhaustion does not remove the pending Fire dash effect");
            fixture.ClearEffects();

            fixture.Energy.value = 200f;
            fixture.States.ChangeState();
            fixture.Place(fixture.Spawn);
            fixture.PulseDash();
            fixture.Movement.FixedTick();
            fixture.Controller.enabled = false;
            fixture.Movement.FixedTick();
            Check(!fixture.Movement.Dash && fixture.Movement.GravityBool && fixture.Effects().Length == 0,
                "Disabling the controller cancels dash without a completion effect");
            fixture.Controller.enabled = true;
            fixture.Place(fixture.Spawn);
            fixture.Energy.value = 200f;
            fixture.PulseDash();
            Check(FinishMovementAuditDash(fixture) == Mathf.CeilToInt(fixture.Fire.DashTime / Time.fixedDeltaTime),
                "The dash after re-enabling the controller has a fresh full duration");
            fixture.ClearEffects();

            fixture.Place(fixture.Spawn + Vector3.up * 10f, false);
            for (int i = 0; i < 20; i++) fixture.Movement.FixedTick();
            Check(Field<float>(fixture.Movement, "_currentGravity") < -1f, "Airborne movement accumulates gravity");
            fixture.Movement.Teleport(fixture.Spawn + Vector3.up * 10f, Quaternion.identity);
            float teleportY = fixture.Player.transform.position.y;
            Check(Mathf.Approximately(Field<float>(fixture.Movement, "_currentGravity"), 0f), "Teleport clears inherited falling speed");
            fixture.Movement.FixedTick();
            float firstFall = teleportY - fixture.Player.transform.position.y;
            float firstStepLimit = Mathf.Abs(Field<MovementConfig>(fixture.Movement, "_movementConfig").GravityValue)
                * Time.fixedDeltaTime * Time.fixedDeltaTime + 0.001f;
            Check(firstFall >= 0f && firstFall <= firstStepLimit,
                $"First movement after teleport has only one step of new gravity (fall {firstFall:F5}, limit {firstStepLimit:F5})");
            fixture.Place(fixture.Spawn);
            fixture.Energy.value = 200f;
            fixture.PulseDash();
            fixture.Movement.Teleport(fixture.Spawn, Quaternion.Euler(0f, 90f, 0f));
            Check(!fixture.Movement.Dash && fixture.Movement.GravityBool && fixture.Movement.MoveVector == Vector3.zero &&
                  Quaternion.Angle(fixture.Player.transform.rotation, Quaternion.Euler(0f, 90f, 0f)) < 0.001f,
                "Teleport applies position/rotation and clears an active dash and buffered movement");
            Time.fixedDeltaTime = oldFixedDelta;

            fixture.NormalMode();
            fixture.Energy.value = 200f;
            Bullet firstShot = fixture.PulseShot();
            Check(firstShot != null && fixture.Shots == 1, "Aimed input fires the first normal shot");
            fixture.States.ChangeState();
            fixture.Coroutines.enabled = false;
            yield return PumpMovementAuditStates(fixture, 0.15f);
            Check(!Field<bool>(fixture.States, "_canShoot"), "Switching state retains the original shot cooldown");
            yield return PumpMovementAuditStates(fixture, 0.35f);
            Check(Field<bool>(fixture.States, "_canShoot"), "Cooldown completes while the unrelated coroutine host is disabled");
            fixture.Coroutines.enabled = true;
            fixture.States.UnlockState(CollectibleName.SecondWeapon);
            Bullet rightShot = fixture.PulseShot();
            Check(rightShot != null && Vector3.Distance(rightShot.transform.position, fixture.OneHand.position) < 0.001f,
                "The first dual-wield shot uses the first hand");
            yield return PumpMovementAuditStates(fixture, 0.16f);
            Bullet leftShot = fixture.PulseShot();
            Check(leftShot != null && Vector3.Distance(leftShot.transform.position, fixture.TwoHands.position) < 0.001f,
                "The next dual-wield shot alternates hands after the configured delay");

            fixture.NormalMode();
            fixture.Energy.value = 10f;
            fixture.States.ChangeState();
            Check(Mathf.Approximately(fixture.Energy.value, 8f), "Entering Fire charges exactly two energy");
            fixture.Coroutines.enabled = false;
            yield return PumpMovementAuditStates(fixture, 1.05f);
            Check(Mathf.Approximately(fixture.Energy.value, 6f), "Fire drains two energy per elapsed second without a coroutine host");
            fixture.Coroutines.enabled = true;

            fixture.Place(fixture.Spawn);
            fixture.Energy.value = 200f;
            fixture.PulseShot();
            fixture.PulseDash();
            fixture.Movement.FixedTick();
            Vector3 pausedPosition = fixture.Player.transform.position;
            float pausedDashTime = Field<float>(fixture.Movement, "_dashTimer");
            float pausedCooldown = Field<float>(fixture.States, "_shootDelayRemaining");
            float pausedEnergy = fixture.Energy.value;
            int pausedShots = fixture.Shots;
            Time.timeScale = 0f;
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                fixture.Movement.Tick();
                fixture.States.Tick();
            }
            fixture.PulseShot();
            Check(fixture.Player.transform.position == pausedPosition && fixture.Movement.Dash &&
                  Field<float>(fixture.Movement, "_dashTimer") == pausedDashTime &&
                  Field<float>(fixture.States, "_shootDelayRemaining") == pausedCooldown &&
                  fixture.Energy.value == pausedEnergy && fixture.Shots == pausedShots,
                "Pause preserves dash progress and freezes cooldown, energy and shooting input");
            Time.timeScale = 1f;
            FinishMovementAuditDash(fixture);
            Check(!fixture.Movement.Dash && fixture.Movement.GravityBool, "The paused dash completes normally after resume");

            fixture.Movement.Dispose();
            fixture.States.Dispose();
            Vector3 disposedPosition = fixture.Player.transform.position;
            float disposedEnergy = fixture.Energy.value;
            fixture.Movement.Tick();
            fixture.Movement.FixedTick();
            fixture.States.Tick();
            fixture.States.ChangeState();
            Check(fixture.Player.transform.position == disposedPosition && fixture.Energy.value == disposedEnergy && !fixture.Movement.Dash,
                "Disposed movement and combat managers perform no further work");
        }
        finally
        {
            fixture?.Dispose();
            foreach (var entry in deviceFilters) entry.Key.devices = entry.Value;
            if (oldGamepad != null && oldGamepad.added) oldGamepad.MakeCurrent();
            Time.fixedDeltaTime = oldFixedDelta;
            Time.timeScale = oldTimeScale;
        }
    }

    private static int FinishMovementAuditDash(MovementAuditFixture fixture)
    {
        int steps = 0;
        while (fixture.Movement.Dash && steps < 100)
        {
            fixture.Movement.FixedTick();
            steps++;
        }
        Check(!fixture.Movement.Dash, "Dash terminates within its finite step budget");
        return steps;
    }

    private static IEnumerator PumpMovementAuditStates(MovementAuditFixture fixture, float duration)
    {
        float elapsed = 0f;
        float deadline = Time.realtimeSinceStartup + duration + 3f;
        while (elapsed < duration && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            fixture.States.Tick();
            elapsed += Time.deltaTime;
        }
        Check(elapsed >= duration, "Combat timer advances in scaled runtime frames");
    }

    private static void SetMovementAuditField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    private sealed class MovementAuditFixture : IDisposable
    {
        public readonly Vector3 Spawn = new Vector3(0f, 1001f, 0f);
        public GameObject Root, Player;
        public CharacterController Controller;
        public PlayerMovementManager Movement;
        public PlayerStatesManager States;
        public PlayerStateConfig Normal, Fire;
        public Slider Energy;
        public Transform OneHand, TwoHands;
        public CoroutinesManager Coroutines;
        public int DashStarts, DashEnters, DashExits, Shots;
        public PlayerStateName DashState;
        private CameraManager _camera;
        private PlayerRigManager _rig;
        private PlayerHudManager _hud;
        private Gamepad _gamepad;
        private GameObject _bulletTemplate, _effectTemplate;
        private readonly List<Object> _configs = new List<Object>();
        private bool _disposed;

        public MovementAuditFixture(PlayerMovementManager sourceMovement, PlayerStatesManager sourceStates, PlayerHudManager sourceHud)
        {
            try
            {
                Root = new GameObject("RavenMovementAudit-" + Guid.NewGuid().ToString("N")) { hideFlags = HideFlags.DontSave };
                var movementConfig = Clone(Field<MovementConfig>(sourceMovement, "_movementConfig"));
                var statesContainer = Clone(Field<PlayerStatesContainer>(sourceStates, "_playerStatesContainer"));
                var dataConfig = Clone(Field<PlayerDataConfig>(sourceHud, "_playerDataConfig"));
                SetMovementAuditField(dataConfig, "_maxEnergyValue", 200f);
                Normal = Clone(statesContainer.FindStateConfig(PlayerStateName.Normal));
                Fire = Clone(statesContainer.FindStateConfig(PlayerStateName.Fire));
                SetMovementAuditField(statesContainer, "_configs", new[] { Normal, Fire });
                SetMovementAuditField(Normal, "_oneHandDelay", 0.4f);
                SetMovementAuditField(Fire, "_oneHandDelay", 0.05f);
                SetMovementAuditField(Fire, "_twoHandsDelay", 0.12f);

                _bulletTemplate = Child("BulletTemplate");
                _bulletTemplate.SetActive(false);
                _bulletTemplate.AddComponent<Bullet>();
                SetMovementAuditField(Normal, "_bulletPrefab", _bulletTemplate);
                SetMovementAuditField(Fire, "_bulletPrefab", _bulletTemplate);
                _effectTemplate = Child("EffectTemplate");
                _effectTemplate.SetActive(false);
                _effectTemplate.AddComponent<DashEffect>();
                SetMovementAuditField(Fire, "_effectPrefab", _effectTemplate);

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(Root.transform, false);
                floor.transform.position = new Vector3(0f, 999.5f, 0f);
                floor.transform.localScale = new Vector3(100f, 1f, 100f);
                floor.tag = "Ground";
                floor.GetComponent<Renderer>().enabled = false;
                Player = Child("Player");
                Controller = Player.AddComponent<CharacterController>();
                Controller.height = 2f;
                Controller.radius = 0.35f;
                Controller.skinWidth = 0.01f;
                Controller.stepOffset = 0f;
                Controller.minMoveDistance = 0f;
                Transform groundCheck = Child("GroundCheck").transform;
                groundCheck.SetParent(Player.transform, false);
                groundCheck.localPosition = new Vector3(0f, -0.9f, 0f);
                Transform cameraTransform = Child("Camera").transform;
                var input = Child("Input").AddComponent<InputManager>();
                input.CanInput = true;
                _gamepad = InputSystem.AddDevice<Gamepad>(Root.name);
                Field<Controls>(input, "_controls").devices = new InputDevice[] { _gamepad };
                Coroutines = Child("Coroutines").AddComponent<CoroutinesManager>();
                _camera = new CameraManager(input, Child("AimCamera"), null, Player, cameraTransform, Child("AimLock"), movementConfig);
                GameObject target = Child("Target");
                target.transform.position = Spawn + Vector3.forward * 100f;
                _rig = new PlayerRigManager(_camera, Array.Empty<UnityEngine.Animations.Rigging.Rig>(), target, 0, cameraTransform);
                var hudReferences = Root.AddComponent<PlayerHudReferences>();
                hudReferences.EnergySlider = Ui<Slider>("Energy");
                hudReferences.HealthSlider = Ui<Slider>("Health");
                hudReferences.StateImage = Ui<Image>("State");
                hudReferences.ViewFinder = Ui<Image>("ViewFinder");
                Energy = hudReferences.EnergySlider;
                _hud = new PlayerHudManager(hudReferences, dataConfig, _camera, Coroutines, Array.Empty<Collectible>(), _rig);
                var references = Root.AddComponent<PlayerReferences>();
                references.Player = Player;
                references.SecondWeapon = Child("SecondWeapon");
                references.NorrmalStateVfx = Array.Empty<GameObject>();
                references.FireStateVfx = Array.Empty<GameObject>();
                OneHand = Child("OneHand").transform;
                TwoHands = Child("TwoHands").transform;
                OneHand.SetParent(Player.transform, false);
                TwoHands.SetParent(Player.transform, false);
                OneHand.localPosition = new Vector3(0.3f, 0.3f, 0.5f);
                TwoHands.localPosition = new Vector3(-0.3f, 0.3f, 0.5f);
                references.OneHandShootPoint = OneHand;
                references.TwoHandsShootPoint = TwoHands;
                States = new PlayerStatesManager(statesContainer, input, new NormalState(), new FireState(), _hud, _rig, references);
                Movement = new PlayerMovementManager(Player, movementConfig, cameraTransform, _camera, input, States, groundCheck);
                Movement.OnDashStart += state => { DashStarts++; DashState = state; };
                Movement.OnDash += active => { if (active) DashEnters++; else DashExits++; };
                States.OnShoot += () => Shots++;
                Place(Spawn);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void NormalMode()
        {
            if (States.CurrentConfig != Normal) States.ChangeState();
        }

        public void ResetDashEvents() { DashStarts = DashEnters = DashExits = 0; }

        public void Place(Vector3 position, bool settle = true)
        {
            Movement.Teleport(position, Quaternion.identity);
            if (settle) Controller.Move(Vector3.down * 0.02f);
            Physics.SyncTransforms();
        }

        public void PulseDash()
        {
            Buttons(GamepadButton.South);
            Movement.Tick();
            Buttons();
        }

        public Bullet PulseShot()
        {
            var previous = new HashSet<Bullet>(Bullets());
            Buttons(GamepadButton.LeftShoulder, GamepadButton.West);
            States.Tick();
            Buttons();
            return Bullets().FirstOrDefault(bullet => !previous.Contains(bullet));
        }

        private void Buttons(params GamepadButton[] buttons)
        {
            var state = new GamepadState();
            foreach (GamepadButton button in buttons) state = state.WithButton(button);
            InputSystem.QueueStateEvent(_gamepad, state);
            InputSystem.Update();
        }

        private Bullet[] Bullets() => Object.FindObjectsByType<Bullet>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(bullet => _bulletTemplate != null && bullet.name == _bulletTemplate.name + "(Clone)").ToArray();

        public DashEffect[] Effects() => Object.FindObjectsByType<DashEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(effect => _effectTemplate != null && effect.name == _effectTemplate.name + "(Clone)").ToArray();

        public void ClearEffects()
        {
            foreach (DashEffect effect in Effects()) Object.DestroyImmediate(effect.gameObject);
        }

        private T Clone<T>(T config) where T : Object
        {
            T copy = Object.Instantiate(config);
            copy.hideFlags = HideFlags.DontSave;
            _configs.Add(copy);
            return copy;
        }

        private GameObject Child(string suffix)
        {
            var child = new GameObject(Root.name + "-" + suffix);
            child.transform.SetParent(Root.transform, false);
            return child;
        }

        private T Ui<T>(string suffix) where T : Component
        {
            var child = new GameObject(Root.name + "-" + suffix, typeof(RectTransform), typeof(T));
            child.transform.SetParent(Root.transform, false);
            return child.GetComponent<T>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Movement?.Dispose();
            States?.Dispose();
            _hud?.Dispose();
            _rig?.Dispose();
            _camera?.Dispose();
            foreach (Bullet bullet in Bullets()) Object.DestroyImmediate(bullet.gameObject);
            ClearEffects();
            if (_gamepad != null && _gamepad.added) InputSystem.RemoveDevice(_gamepad);
            if (Root != null) Object.DestroyImmediate(Root);
            foreach (Object config in _configs) if (config != null) Object.DestroyImmediate(config);
        }
    }
}

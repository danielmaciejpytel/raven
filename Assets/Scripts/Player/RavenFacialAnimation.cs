using Raven.Input;
using UnityEngine;

namespace Raven.Player
{
    [DisallowMultipleComponent]
    public sealed class RavenFacialAnimation : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer _face;
        [Header("Blinking")]
        [SerializeField] private Vector2 _blinkInterval = new Vector2(2.5f, 5.5f);
        [SerializeField, Range(0.03f, 0.2f)] private float _blinkCloseTime = 0.065f;
        [SerializeField, Range(0f, 0.1f)] private float _blinkHoldTime = 0.025f;
        [SerializeField, Range(0.05f, 0.3f)] private float _blinkOpenTime = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _doubleBlinkChance = 0.12f;
        [Header("Expressions")]
        [SerializeField, Range(0f, 2f)] private float _expressionIntensity = 1f;
        [SerializeField, Range(0.05f, 1f)] private float _expressionSmoothTime = 0.25f;
        [SerializeField] private Vector2 _idleExpressionInterval = new Vector2(5f, 10f);
        [SerializeField] private Vector2 _idleExpressionDuration = new Vector2(1.5f, 3f);
        [SerializeField, Range(0f, 40f)] private float _aimSquint = 10f;
        [SerializeField, Range(0f, 40f)] private float _aimBrowsDown = 12f;
        [SerializeField, Range(0f, 30f)] private float _idleSmile = 10f;

        private static readonly string[] Shapes =
        {
            "Blink_L", "Blink_R", "Squint_L", "Squint_R", "BrowsUp",
            "BrowsDown", "BrowInnerUp", "Smile", "Frown", "JawOpen"
        };
        private readonly int[] _indices = new int[Shapes.Length];
        private readonly float[] _baseWeights = new float[Shapes.Length];
        private readonly float[] _values = new float[Shapes.Length];
        private readonly float[] _targets = new float[Shapes.Length];
        private InputManager _input;
        private Animator _animator;
        private System.Random _random;
        private bool _initialized, _blinking, _secondBlink;
        private float _blinkWait, _blinkTime, _expressionWait, _expressionTime, _expressionDuration;
        private int _expression;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int DashHash = Animator.StringToHash("Dash");

        public void Initialize(InputManager input)
        {
            if (_initialized) RestoreWeights();
            _initialized = false;
            _input = input;
            _animator = GetComponent<Animator>();
            if (_face == null || _face.sharedMesh == null) return;
            for (int i = 0; i < Shapes.Length; i++)
            {
                _indices[i] = _face.sharedMesh.GetBlendShapeIndex(Shapes[i]);
                _baseWeights[i] = _indices[i] >= 0 ? _face.GetBlendShapeWeight(_indices[i]) : 0f;
            }
            _random = new System.Random(unchecked(System.Environment.TickCount ^ GetInstanceID()));
            _initialized = true;
            ResetTimers();
        }

        private float Sample(Vector2 range)
        {
            float min = Mathf.Max(0.1f, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return Mathf.Lerp(min, max, (float)_random.NextDouble());
        }

        private void ResetTimers()
        {
            System.Array.Clear(_values, 0, _values.Length);
            _blinking = _secondBlink = false;
            _blinkTime = _expressionTime = _expressionDuration = 0f;
            _blinkWait = Sample(_blinkInterval);
            _expressionWait = Sample(_idleExpressionInterval);
        }

        private void OnEnable()
        {
            if (_initialized) ResetTimers();
        }

        private void OnDisable() => RestoreWeights();

        private void RestoreWeights()
        {
            if (!_initialized || _face == null) return;
            for (int i = 0; i < Shapes.Length; i++)
                if (_indices[i] >= 0) _face.SetBlendShapeWeight(_indices[i], _baseWeights[i]);
        }

        private float UpdateBlink(float dt)
        {
            if (!_blinking)
            {
                _blinkWait -= dt;
                if (_blinkWait > 0f) return 0f;
                _blinking = true;
                _blinkTime = 0f;
            }
            _blinkTime += dt;
            float close = Mathf.Max(0.01f, _blinkCloseTime);
            float hold = Mathf.Max(0f, _blinkHoldTime);
            float open = Mathf.Max(0.01f, _blinkOpenTime);
            if (_blinkTime < close) return Mathf.SmoothStep(0f, 1f, _blinkTime / close);
            if (_blinkTime < close + hold) return 1f;
            if (_blinkTime < close + hold + open)
                return 1f - Mathf.SmoothStep(0f, 1f, (_blinkTime - close - hold) / open);
            _blinking = false;
            bool repeat = !_secondBlink && _random.NextDouble() < _doubleBlinkChance;
            _secondBlink = repeat;
            _blinkWait = repeat ? 0.12f : Sample(_blinkInterval);
            return 0f;
        }

        private void LateUpdate()
        {
            if (!_initialized || _face == null || Time.deltaTime <= 0f) return;
            float dt = Time.deltaTime;
            bool gameplay = _input != null && _input.GameplayInputEnabled;
            bool aiming = gameplay && _input.AimButtonHold();
            bool moving = gameplay && _animator != null && _animator.GetFloat(SpeedHash) > 0.15f;
            bool dash = gameplay && _animator != null && _animator.GetBool(DashHash);
            System.Array.Clear(_targets, 0, _targets.Length);

            if (gameplay && !aiming && !moving && !dash)
            {
                if (_expressionDuration <= 0f)
                {
                    _expressionWait -= dt;
                    if (_expressionWait <= 0f)
                    {
                        _expression = _random.Next(3);
                        _expressionTime = 0f;
                        _expressionDuration = Sample(_idleExpressionDuration);
                    }
                }
                if (_expressionDuration > 0f)
                {
                    _expressionTime += dt;
                    float pulse = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_expressionTime / _expressionDuration));
                    pulse *= pulse;
                    if (_expression == 0) { _targets[7] = _idleSmile * pulse; _targets[6] = 3f * pulse; }
                    else if (_expression == 1) { _targets[4] = 7f * pulse; _targets[6] = 5f * pulse; }
                    else { _targets[2] = _targets[3] = 5f * pulse; _targets[7] = _idleSmile * 0.4f * pulse; }
                    if (_expressionTime >= _expressionDuration)
                    {
                        _expressionDuration = 0f;
                        _expressionWait = Sample(_idleExpressionInterval);
                    }
                }
            }
            else
            {
                // Cancel a smile on entering combat; do not resume an old expression later.
                _expressionDuration = 0f;
                _expressionWait = Sample(_idleExpressionInterval);
            }
            if (aiming)
            {
                _targets[2] = _targets[3] = _aimSquint;
                _targets[5] = _aimBrowsDown;
                _targets[8] = 3f;
            }
            else if (moving || dash)
            {
                _targets[2] = _targets[3] = dash ? 8f : 3f;
                _targets[5] = dash ? 8f : 3f;
                _targets[9] = dash ? 2f : 1f;
            }

            float blink = UpdateBlink(dt);
            float smoothing = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _expressionSmoothTime));
            for (int i = 0; i < Shapes.Length; i++)
            {
                float value;
                if (i < 2) value = 100f * blink;
                else
                {
                    _values[i] = Mathf.Lerp(_values[i], _targets[i] * _expressionIntensity, smoothing);
                    value = _values[i];
                    // Avoid stacking a full eyelid closure with a squint deformation.
                    if (i == 2 || i == 3) value *= 1f - blink;
                }
                if (_indices[i] >= 0) _face.SetBlendShapeWeight(_indices[i], Mathf.Clamp(_baseWeights[i] + value, 0f, 100f));
            }
        }
    }
}

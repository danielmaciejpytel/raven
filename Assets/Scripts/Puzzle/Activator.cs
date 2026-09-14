using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

namespace Raven.Puzzle
{
    public enum ActivatorType { Torch, Lever }

    [RequireComponent(typeof(Collider))]
    public class Activator : MonoBehaviour
    {
        [SerializeField] private ActivatorType _activatorType;
        [Space]
        [SerializeField, ShowIf("_activatorType", ActivatorType.Lever)] private Vector3 _partShift;
        [Space]
        [SerializeField, HideIf("_activatorType", ActivatorType.Lever)] private GameObject _fire;
        [Space]
        [SerializeField] private Transform _doorTransform;
        [SerializeField] private Vector3 _doorShift;
        [Space]
        [SerializeField] private float _openTime;
        [SerializeField] private float _closeTime;
        [SerializeField] private bool _stayOpenForAWhile;
        [SerializeField, ShowIf("_stayOpenForAWhile")] private float _stayOpenTime;
        [SerializeField, HideIf("_stayOpenForAWhile")] private bool _stayOpen;
        [Space]
        [InfoBox("Transforms must be assigned", EInfoBoxType.Warning)]
        [SerializeField, Range(0.1f, 2), Tooltip("It's radius of gizmos spheres")] private float _gizmoRadius = 1f;
        [Space]
        [SerializeField, HideIf("_activatorType", ActivatorType.Lever)] private AudioSource _torchAudioSource;
        [SerializeField] private AudioClip _openSound;
        [SerializeField] private AudioClip _closeSound;
        [SerializeField] private AudioSource _doorAudioSource;

        private bool _closingDoor;
        private bool _openingDoor;
        private bool _doorAreOpened;
        private bool _play;

        private Coroutine _closeDelayCoroutine;
        private readonly HashSet<Collider> _playerColliders = new HashSet<Collider>();

        private float _stayOpenTimer;

        private Vector3 _doorDestiny;
        private Vector3 _doorStartPos;
        private Vector3 _partDestiny;
        private Vector3 _partStartPos;

        private float _percent;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            if (_doorTransform == null)
            {
                Debug.LogError($"Activator '{name}' has no door transform assigned.", this);
                enabled = false;
                return;
            }

            if (_activatorType == ActivatorType.Torch)
            {
                if (_fire != null) _fire.SetActive(false);
            }
            else
            {
                _partDestiny = transform.position + _partShift;
                _partStartPos = transform.position;
            }

            _doorDestiny = _doorTransform.position + _doorShift;
            _doorStartPos = _doorTransform.position;
        }

        private void Update()
        {
            if (_openingDoor)
            {
                OpenDoor();
            }

            else if (_closingDoor)
            {
                CloseDoor();
            }
        }

        public void OpenDoor()
        {
            if (_closingDoor || _doorTransform == null) return;

            if (_openTime > 0f && _percent < 1f && !_doorAreOpened)
            {
                _percent = Mathf.MoveTowards(_percent, 1f, Time.deltaTime / _openTime);
                
                _doorTransform.position = _doorStartPos + _doorShift * _percent;

                if (_activatorType == ActivatorType.Lever)
                {
                    transform.position = _partStartPos + _partShift * _percent;
                }
            }
            else
            {
                _percent = 1f;
                _doorTransform.position = _doorDestiny;
                if (_activatorType == ActivatorType.Lever) transform.position = _partDestiny;
                _doorAreOpened = true;

                if (_stayOpenForAWhile && _activatorType == ActivatorType.Torch)
                {
                    if (_doorAudioSource != null) _doorAudioSource.Stop();
                    StayOpenWhile();
                }
                else if (!_stayOpen && _activatorType == ActivatorType.Torch)
                {
                    _openingDoor = false;
                    _doorAreOpened = false;
                    _closingDoor = true;
                    _play = true;
                    PlaySound(_closeSound);
                }
                else
                {
                    _openingDoor = false;
                    if (_doorAudioSource != null) _doorAudioSource.Stop();
                }
            }
        }

        private void StayOpenWhile()
        {
            if (_stayOpenTimer < _stayOpenTime)
            {
                _stayOpenTimer += Time.deltaTime;
            }
            else
            {
                _openingDoor = false;
                _doorAreOpened = false;
                _closingDoor = true;
                _stayOpenTimer = 0;
                _play = true;
                PlaySound(_closeSound);
            }
        }

        private void CloseDoor()
        {
            _doorAreOpened = false;

            if (_closeTime > 0f && _percent > 0f)
            {
                _percent = Mathf.MoveTowards(_percent, 0f, Time.deltaTime / _closeTime);

                _doorTransform.position = _doorStartPos + _doorShift * _percent;

                if (_activatorType == ActivatorType.Lever)
                {
                    transform.position = _partStartPos + _partShift * _percent;
                }
            }
            else
            {
                _percent = 0f;
                _doorTransform.position = _doorStartPos;
                if (_activatorType == ActivatorType.Lever) transform.position = _partStartPos;
                _closingDoor = false;

                if (_activatorType == ActivatorType.Torch)
                {
                    if (_fire != null) _fire.SetActive(false);
                    if (_torchAudioSource != null) _torchAudioSource.Stop();
                }

                if (_doorAudioSource != null) _doorAudioSource.Stop();
            }
        }

        private void OnTriggerEnter(Collider p_other)
        {
            if (!isActiveAndEnabled) return;

            if (_activatorType == ActivatorType.Torch && p_other.CompareTag("FireBullet"))
            {
                if (_openingDoor || _doorAreOpened) return;
                _closingDoor = false;
                _openingDoor = true;
                _stayOpenTimer = 0f;
                if (_fire != null) _fire.SetActive(true);
                if (_torchAudioSource != null) _torchAudioSource.Play();
                _play = true;
                PlaySound(_openSound);
            }
            else if (_activatorType == ActivatorType.Lever && IsPlayer(p_other))
            {
                _playerColliders.Add(p_other);
                CancelCloseDelay();
                if (_openingDoor || _doorAreOpened) return;

                _closingDoor = false;
                _openingDoor = true;
                _play = true;
                PlaySound(_openSound);
            }
        }

        private void OnTriggerExit(Collider p_collider)
        {
            if (!_playerColliders.Remove(p_collider) || _playerColliders.Count > 0) return;

            if ((!_stayOpen || _stayOpenForAWhile) && !_closingDoor && _activatorType == ActivatorType.Lever)
            {
                if (_stayOpenForAWhile)
                {
                    CancelCloseDelay();
                    _closeDelayCoroutine = StartCoroutine(CloseDelayCoroutine());
                    return;
                }

                _openingDoor = false;
                _doorAreOpened = false;
                _closingDoor = true;
                _play = true;
                PlaySound(_closeSound);
            }
        }

        private IEnumerator CloseDelayCoroutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, _stayOpenTime));
            _closeDelayCoroutine = null;
            if (_playerColliders.Count > 0) yield break;

            _openingDoor = false;
            _doorAreOpened = false;
            _closingDoor = true;
            _play = true;
            PlaySound(_closeSound);
        }

        private void PlaySound(AudioClip p_audioClip)
        {
            if (_play)
            {
                if (_doorAudioSource != null)
                {
                    _doorAudioSource.Stop();
                    _doorAudioSource.clip = p_audioClip;
                    if (p_audioClip != null) _doorAudioSource.Play();
                }
                _play = false;
            }
        }

        private static bool IsPlayer(Collider collider)
        {
            CharacterController player = collider.GetComponentInParent<CharacterController>();
            return collider.CompareTag("Player") || (player != null && player.CompareTag("Player"));
        }

        private void CancelCloseDelay()
        {
            if (_closeDelayCoroutine == null) return;
            StopCoroutine(_closeDelayCoroutine);
            _closeDelayCoroutine = null;
        }

        private void OnDisable()
        {
            CancelCloseDelay();
            _playerColliders.Clear();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;

            if (_doorTransform != null)
            {
                Gizmos.DrawSphere(_doorTransform.position + _doorShift, _gizmoRadius);
            }

            if (_activatorType == ActivatorType.Lever)
            {
                Gizmos.DrawSphere(transform.position + _partShift, _gizmoRadius);
            }
        }
#endif
    }
}

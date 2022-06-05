using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace Gamekit3D
{
    public class MeleeWeapon : MonoBehaviour
    {
        private const int ParticleCount = 10;

        private static readonly RaycastHit[] SRaycastHitCache = new RaycastHit[32];
        protected static Collider[] SColliderCache = new Collider[32];
        public int damage = 1;

        public ParticleSystem hitParticlePrefab;
        public LayerMask targetLayers;

        public AttackPoint[] attackPoints = Array.Empty<AttackPoint>();

        public TimeEffect[] effects;

        [Header("Audio")]
        public RandomAudioPlayer hitAudio;
        public RandomAudioPlayer attackAudio;
        private readonly ParticleSystem[] _particlesPool = new ParticleSystem[ParticleCount];

        private int _currentParticle;
        private Vector3 _direction;
        private bool _inAttack;

        private GameObject _owner;

        private Vector3[] _previousPos;

        private bool ThrowingHit { get; set; }


        private void Awake()
        {
            if (hitParticlePrefab == null)
            {
                return;
            }

            for (var i = 0; i < ParticleCount; ++i)
            {
                _particlesPool[i] = Instantiate(hitParticlePrefab);
                _particlesPool[i].Stop();
            }
        }


        private void OnEnable()
        {
        }


        //whoever own the weapon is responsible for calling that. Allow to avoid "self harm"
        public void SetOwner(GameObject owner)
        {
            _owner = owner;
        }


        public void BeginAttack(bool thowingAttack)
        {
            if (attackAudio != null)
            {
                attackAudio.PlayRandomClip();
            }

            ThrowingHit = thowingAttack;

            _inAttack = true;

            _previousPos = new Vector3[attackPoints.Length];

            for (var i = 0; i < attackPoints.Length; ++i)
            {
                var worldPos = attackPoints[i].attackRoot.position +
                               attackPoints[i].attackRoot.TransformVector(attackPoints[i].offset);

                _previousPos[i] = worldPos;

#if UNITY_EDITOR
                attackPoints[i].PreviousPositions.Clear();
                attackPoints[i].PreviousPositions.Add(_previousPos[i]);
#endif
            }
        }


        public void EndAttack()
        {
            _inAttack = false;


#if UNITY_EDITOR
            foreach (var attackPoint in attackPoints)
            {
                attackPoint.PreviousPositions.Clear();
            }
#endif
        }


        private void FixedUpdate()
        {
            if (!_inAttack)
            {
                return;
            }

            for (var i = 0; i < attackPoints.Length; ++i)
            {
                var pts = attackPoints[i];

                var worldPos = pts.attackRoot.position + pts.attackRoot.TransformVector(pts.offset);
                var attackVector = worldPos - _previousPos[i];

                if (attackVector.magnitude < 0.001f)
                {
                    // A zero vector for the sphere cast don't yield any result, even if a collider overlap the "sphere" created by radius.
                    // so we set a very tiny microscopic forward cast to be sure it will catch anything overlaping that "stationary" sphere cast
                    attackVector = Vector3.forward * 0.0001f;
                }


                var r = new Ray(worldPos, attackVector.normalized);

                var contacts = Physics.SphereCastNonAlloc(r, pts.radius, SRaycastHitCache, attackVector.magnitude,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                for (var k = 0; k < contacts; ++k)
                {
                    var col = SRaycastHitCache[k].collider;

                    if (col != null)
                    {
                        CheckDamage(col, pts);
                    }
                }

                _previousPos[i] = worldPos;

#if UNITY_EDITOR
                pts.PreviousPositions.Add(_previousPos[i]);
#endif
            }
        }


        private bool CheckDamage(Collider other, AttackPoint pts)
        {
            var damageable = other.GetComponent<Damageable>();

            if (damageable == null)
            {
                return false;
            }

            if (damageable.gameObject == _owner)
            {
                return true; //ignore self harm, but do not end the attack (we don't "bounce" off ourselves)
            }

            if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
            {
                //hit an object that is not in our layer, this end the attack. we "bounce" off it
                return false;
            }

            if (hitAudio != null)
            {
                var rend = other.GetComponent<Renderer>();

                if (!rend)
                {
                    rend = other.GetComponentInChildren<Renderer>();
                }

                if (rend)
                {
                    hitAudio.PlayRandomClip(rend.sharedMaterial);
                }
                else
                {
                    hitAudio.PlayRandomClip();
                }
            }

            Damageable.DamageMessage data;

            data.amount = damage;
            data.damager = this;
            data.direction = _direction.normalized;
            data.damageSource = _owner.transform.position;
            data.throwing = ThrowingHit;
            data.stopCamera = false;

            damageable.ApplyDamage(data);

            if (hitParticlePrefab == null)
            {
                return true;
            }

            _particlesPool[_currentParticle].transform.position = pts.attackRoot.transform.position;
            _particlesPool[_currentParticle].time = 0;
            _particlesPool[_currentParticle].Play();
            _currentParticle = (_currentParticle + 1) % ParticleCount;

            return true;
        }


#if UNITY_EDITOR


        private void OnDrawGizmosSelected()
        {
            foreach (var attackPoint in attackPoints)
            {
                if (attackPoint.attackRoot != null)
                {
                    var worldPos = attackPoint.attackRoot.TransformVector(attackPoint.offset);
                    Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.4f);
                    Gizmos.DrawSphere(attackPoint.attackRoot.position + worldPos, attackPoint.radius);
                }

                if (attackPoint.PreviousPositions.Count > 1)
                {
                    Handles.DrawAAPolyLine(10, attackPoint.PreviousPositions.ToArray());
                }
            }
        }


#endif


        [Serializable]
        public class AttackPoint
        {
            public float radius;
            public Vector3 offset;
            public Transform attackRoot;

#if UNITY_EDITOR
            //editor only as it's only used in editor to display the path of the attack that is used by the raycast
            [NonSerialized] public List<Vector3> PreviousPositions = new();
#endif
        }
    }
}
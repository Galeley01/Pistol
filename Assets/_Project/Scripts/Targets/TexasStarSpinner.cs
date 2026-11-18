using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // The Texas star: the arm with the plates stands still until the first plate is shot off, then spins up;
    // when the last plate is gone it slows down and stops. At start the plates of the same target
    // (DetachingPlate among the siblings) are hung on the arm, so they turn with it.
    // Reset stops the arm at its start angle (the plates are put back by themselves).
    //
    // Goes on the arm (the part that turns, pivot in the hub).
    public class TexasStarSpinner : ShootingTarget
    {
        [BoxGroup("Вращение"), SerializeField, Tooltip("Ось вращения в осях рычага (Z — сквозь звезду)")]
        Vector3 axis = Vector3.forward;
        [BoxGroup("Вращение"), SerializeField, Range(-180f, 180f), Tooltip("Скорость после раскрутки, градусы в секунду. Минус — в другую сторону")]
        float speed = 25f;
        [BoxGroup("Вращение"), SerializeField, Range(0.1f, 10f), Tooltip("За сколько секунд раскручивается после первой сбитой пластины")]
        float spinUpTime = 1.5f;
        [BoxGroup("Вращение"), SerializeField, Range(0.1f, 10f), Tooltip("За сколько секунд останавливается после последней сбитой пластины")]
        float spinDownTime = 3f;

        Quaternion rest;
        float angle, current;
        int plates, detached;

        [ShowNativeProperty] public float Angle => angle;
        [ShowNativeProperty] public float CurrentSpeed => current;
        [ShowNativeProperty] public string Plates => $"{plates - detached} / {plates}";

        void Awake()
        {
            rest = transform.localRotation;
            var root = transform.parent;
            if (root == null) return;
            foreach (var plate in root.GetComponentsInChildren<DetachingPlate>(true))
            {
                plate.HangOn(this);
                plates++;
            }
        }

        // Called by a plate when it breaks off
        public void PlateDetached() => detached++;

        void Update()
        {
            // Spinning while at least one plate is off and at least one is still on
            bool spin = detached > 0 && detached < plates;
            float target = spin ? speed : 0f;
            float time = spin ? spinUpTime : spinDownTime;
            current = Mathf.MoveTowards(current, target, Mathf.Abs(speed) / Mathf.Max(time, 0.01f) * Time.deltaTime);
            if (current == 0f) return;
            angle = Mathf.Repeat(angle + current * Time.deltaTime, 360f);
            transform.localRotation = rest * Quaternion.AngleAxis(angle, axis.normalized);
        }

        // Hits on the arm itself do nothing
        protected override void OnHit(in BulletHit hit) { }

        public override void ResetTarget()
        {
            angle = current = 0f;
            detached = 0;
            transform.localRotation = rest;
        }
    }
}

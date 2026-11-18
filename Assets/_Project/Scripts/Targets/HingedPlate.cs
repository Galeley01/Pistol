using UnityEngine;
using NaughtyAttributes;
using Pistol.Ballistics;

namespace Pistol.Targets
{
    // A steel plate on a hinge that moves when hit. One component for every kind of plate on the range,
    // only the settings differ:
    //   Fall — falls over and stays down until the range is reset (plate rack, hostage flag);
    //   Flip — swings over the top to the other side, the next hit sends it back (dueling tree);
    //   Spin — half a turn around its bar on every hit (spinning plate rack).
    // The motion is shaped, not simulated: it speeds up like a falling plate and bounces once on the stop.
    // The hinge is the part's own pivot (set in the model); the axis is in the part's local space.
    //
    // Goes on the moving part (next to its collider).
    public class HingedPlate : ShootingTarget
    {
        public enum Kind { Fall, Flip, Spin }
        public enum Direction { PushedByBullet, OverTheTop, TowardStand }

        [BoxGroup("Движение"), SerializeField, Tooltip("Fall — падает и лежит до сброса; Flip — перекидывается на другую сторону и обратно; Spin — пол-оборота от каждого попадания")]
        Kind kind = Kind.Fall;
        [BoxGroup("Движение"), SerializeField, Tooltip("Ось шарнира в осях этой детали (X — вдоль ширины, Y — вверх, Z — сквозь плиту)")]
        Vector3 axis = Vector3.right;
        [BoxGroup("Движение"), SerializeField, Range(10f, 180f), Tooltip("На сколько градусов поворачивается (для Spin — за одно попадание)")]
        float angle = 85f;
        [BoxGroup("Движение"), SerializeField, Tooltip("В какую сторону: куда толкает пуля; вверх через верх (лопатки дуэльного дерева); " +
                                                         "всегда к центру стойки, откуда бы ни стреляли (тарелки на рейке падают назад)")]
        Direction direction = Direction.PushedByBullet;
        [BoxGroup("Движение"), SerializeField, Tooltip("Поменять сторону, если плита едет не туда")]
        bool invert;

        [BoxGroup("Форма"), SerializeField, Range(0.05f, 1f), Tooltip("За сколько секунд плита доходит до упора")]
        float duration = 0.22f;
        [BoxGroup("Форма"), SerializeField, Range(0f, 0.3f), Tooltip("Отскок от упора, доля угла")]
        float bounce = 0.08f;
        [BoxGroup("Форма"), SerializeField, Range(0.02f, 0.5f), Tooltip("Длина отскока, сек")]
        float bounceTime = 0.14f;

        [ShowNativeProperty] public float CurrentAngle => current;
        [ShowNativeProperty] public bool Down => target != 0f;

        Quaternion rest;
        bool hasRest;
        float current, from, target;
        float time = -1f;    // seconds since the move started, -1 = standing still
        Vector3 lever;       // from the hinge to the middle of the plate, local

        void Awake()
        {
            rest = transform.localRotation;
            hasRest = true;
            var mf = GetComponent<MeshFilter>();
            lever = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.up;
        }

        protected override void OnHit(in BulletHit hit)
        {
            if (time >= 0f) return;   // still moving: it hasn't reached the stop yet
            if (kind == Kind.Fall && target != 0f) return;   // already down

            float sign = Side(hit.point, hit.direction);
            switch (kind)
            {
                case Kind.Fall: MoveTo(sign * angle); break;
                case Kind.Flip: MoveTo(target == 0f ? sign * angle : 0f); break;
                case Kind.Spin: MoveTo(target + sign * angle); break;
            }
        }

        // +1 or -1: which way around the axis this hit sends the plate
        float Side(Vector3 point, Vector3 bulletDir)
        {
            Vector3 worldAxis = RestWorldRotation() * axis.normalized;
            float s;
            if (direction == Direction.PushedByBullet)
            {
                // The hit point moves along the bullet
                s = Vector3.Dot(Vector3.Cross(worldAxis, point - transform.position), bulletDir);
            }
            else if (direction == Direction.TowardStand)
            {
                // The top of the plate goes towards the middle of the target (its root), whatever side the shot came from
                var stand = transform.parent;
                Vector3 toStand = stand != null ? stand.position - transform.position : -transform.forward;
                toStand.y = 0f;
                Vector3 leverWorld = RestWorldRotation() * lever;
                s = Vector3.Dot(Vector3.Cross(worldAxis, leverWorld), toStand);
            }
            else
            {
                // From the side it hangs now the plate goes up first, over the top
                Vector3 leverWorld = transform.rotation * lever;
                s = Vector3.Dot(Vector3.Cross(worldAxis, leverWorld), Vector3.up);
            }
            // s is measured with Cross(axis, lever); a positive angle of the hinge turns the plate the other way
            // round (checked in Play on the plate rack), hence the minus
            float sign = s >= 0f ? -1f : 1f;
            return invert ? -sign : sign;
        }

        Quaternion RestWorldRotation()
        {
            var parent = transform.parent;
            return parent != null ? parent.rotation * rest : rest;
        }

        void MoveTo(float newTarget)
        {
            from = current;
            target = newTarget;
            time = 0f;
        }

        void Update()
        {
            if (time < 0f) return;
            time += Time.deltaTime;
            float total = target - from;
            if (time < duration)
            {
                float t = time / duration;
                current = from + total * t * t;   // speeds up like a falling plate
            }
            else
            {
                // One bounce back from the stop
                float u = (time - duration) / Mathf.Max(bounceTime, 0.001f);
                if (u >= 1f || bounce <= 0f)
                {
                    current = target;
                    time = -1f;
                    if (kind == Kind.Spin) target = current = Mathf.Repeat(target + 180f, 360f) - 180f;   // keep the angle small
                }
                else current = target - total * bounce * Mathf.Sin(Mathf.PI * u) * (1f - u);
            }
            Apply();
        }

        void Apply()
        {
            if (!hasRest) return;
            transform.localRotation = rest * Quaternion.AngleAxis(current, axis.normalized);
        }

        public override void ResetTarget()
        {
            current = from = target = 0f;
            time = -1f;
            Apply();
        }

        // ---------- Setup (editor menu) and test ----------
        public void Configure(Kind kind, Vector3 axis, float angle, Direction direction)
        {
            this.kind = kind;
            this.axis = axis;
            this.angle = angle;
            this.direction = direction;
        }

        [Button("Тест: попадание (пуля от камеры)")]
        void TestHit()
        {
            if (!Application.isPlaying) return;
            var cam = Camera.main;   // test button only: a bullet from where the player looks
            Vector3 point = transform.TransformPoint(lever);
            Vector3 dir = cam != null ? (point - cam.transform.position).normalized : -transform.forward;
            TakeHit(new BulletHit { point = point, normal = -dir, direction = dir, speed = 440f, collider = GetComponent<Collider>() });
        }

        [Button("Тест: поставить на место")] void TestReset() { if (Application.isPlaying) ResetTarget(); }
    }
}

using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

// Выстрел из дула по направлению ствола + отладочная «линия будущей пули».
// Вешается на Revolver. Пуля летит туда, куда реально смотрит ствол в момент выстрела:
// всё раскачивание оружия (инерция, крен, дыхание, Free-Aim) учитывается само.
//
// Пристрелка: ствол ниже глаза, поэтому его чуть наклоняют вверх, чтобы пуля пересекала
// линию прицела (целик—мушка) на дистанции пристрелки.
[DefaultExecutionOrder(1000)]   // после камеры, чтобы линия не отставала от оружия на кадр
public class RevolverShooter : MonoBehaviour
{
    [BoxGroup("Ствол"), Tooltip("Точка дула (найдётся сама: Muzzle)")]
    public Transform muzzle;
    [BoxGroup("Ствол"), Range(5f, 50f), Tooltip("На какой дистанции пуля попадает точно в точку прицеливания, м")]
    public float zeroDistance = 15f;
    [BoxGroup("Ствол"), Range(0f, 0.04f), Tooltip("Насколько линия прицела выше оси ствола, м")]
    public float sightHeight = 0.013f;

    [BoxGroup("Линия будущей пули"), Tooltip("Показывать, куда полетит пуля, если выстрелить сейчас")]
    public bool showTrajectory = true;
    [BoxGroup("Линия будущей пули"), ShowIf("showTrajectory"), Range(10f, 300f), Tooltip("Длина линии, м")]
    public float previewDistance = 80f;
    [BoxGroup("Линия будущей пули"), ShowIf("showTrajectory")] public Color lineColor = new Color(1f, 0.25f, 0.2f, 0.75f);
    [BoxGroup("Линия будущей пули"), ShowIf("showTrajectory")] public Color hitColor = new Color(1f, 0.2f, 0.15f, 0.95f);

    Transform ignoreRoot;
    RevolverCylinder cylinder;
    LineRenderer line;
    GameObject hitMarker;
    readonly List<Vector3> points = new List<Vector3>(64);

    void Start()
    {
        if (muzzle == null) muzzle = transform.Find("Muzzle");
        cylinder = GetComponent<RevolverCylinder>();
        var player = GetComponentInParent<PlayerMovement>();
        ignoreRoot = player != null ? player.transform : transform.root;

        var go = new GameObject("TrajectoryPreview");
        line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = BulletSystem.GetMaterial();
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.0015f), new Keyframe(1f, 0.03f));
        hitMarker = BulletSystem.CreateDisc("TrajectoryHit", hitColor);
    }

    // Откуда и куда летит пуля, если выстрелить сейчас (с учётом пристрелки)
    public void GetShot(AmmoType ammo, out Vector3 origin, out Vector3 direction)
    {
        Transform from = muzzle != null ? muzzle : transform;
        origin = from.position;

        float v = ammo != null ? ammo.muzzleVelocity : 440f;
        float g = Physics.gravity.magnitude * (ammo != null ? ammo.gravityScale : 1f);
        float t = zeroDistance / v;
        float drop = 0.5f * g * t * t;
        float angle = Mathf.Atan2(sightHeight + drop, zeroDistance) * Mathf.Rad2Deg;
        direction = Quaternion.AngleAxis(-angle, transform.right) * transform.forward;   // чуть вверх
    }

    // Вызывается из RevolverFire, когда курок ударил по патрону
    public void Fire(AmmoType ammo)
    {
        if (ammo == null) ammo = cylinder != null ? cylinder.Ammo : AmmoType.CreateDefault();
        GetShot(ammo, out var origin, out var dir);
        BulletSystem.Instance.Fire(origin, dir, ammo, ignoreRoot);
    }

    void LateUpdate()
    {
        if (line == null) return;
        line.enabled = showTrajectory;
        if (!showTrajectory) { hitMarker.SetActive(false); return; }

        var ammo = cylinder != null ? cylinder.Ammo : null;
        GetShot(ammo, out var pos, out var dir);
        float speed = ammo != null ? ammo.muzzleVelocity : 440f;
        Vector3 g = Physics.gravity * (ammo != null ? ammo.gravityScale : 1f);
        Vector3 vel = dir * speed;
        LayerMask mask = BulletSystem.Instance.hitMask;

        // Тот же расчёт полёта, что у настоящей пули, шагами по 5 мс
        points.Clear();
        points.Add(pos);
        float travelled = 0f;
        bool hitSomething = false;
        RaycastHit hit = default;
        float dt = Mathf.Max(0.005f, previewDistance / speed / 60f);   // ~60 точек на всю длину
        while (travelled < previewDistance && points.Count < 64)
        {
            Vector3 newVel = vel + g * dt;
            Vector3 newPos = pos + (vel + newVel) * 0.5f * dt;
            Vector3 seg = newPos - pos;
            if (BulletSystem.Raycast(pos, seg, seg.magnitude, mask, ignoreRoot, out hit))
            {
                points.Add(hit.point);
                hitSomething = true;
                break;
            }
            travelled += seg.magnitude;
            pos = newPos;
            vel = newVel;
            points.Add(pos);
        }

        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        line.startColor = lineColor;
        line.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.3f);

        hitMarker.SetActive(hitSomething);
        if (hitSomething)
        {
            float size = Mathf.Clamp((travelled + hit.distance) * 0.004f + 0.01f, 0.01f, 0.08f);   // дальше — крупнее, чтобы было видно
            hitMarker.transform.SetPositionAndRotation(hit.point + hit.normal * 0.002f, Quaternion.LookRotation(hit.normal));
            hitMarker.transform.localScale = Vector3.one * size;
        }
    }

    [Button("Взять высоту прицела из точки целика")]
    void SightHeightFromRearSight()
    {
        var sway = GetComponent<WeaponSway>();
        var m = muzzle != null ? muzzle : transform.Find("Muzzle");
        if (sway == null || m == null) { Debug.LogWarning("Нужны WeaponSway и Muzzle"); return; }
        sightHeight = Mathf.Max(0f, sway.rearSightPoint.y - m.localPosition.y);
        Debug.Log($"Высота прицела над стволом: {sightHeight * 1000f:F1} мм");
    }

    void OnDisable()
    {
        if (line != null) line.enabled = false;
        if (hitMarker != null) hitMarker.SetActive(false);
    }
}

using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.Arms
{
    // How the hands hold a specific weapon. Everything is in weapon space:
    // X right, Y up, Z forward along the barrel, in meters and degrees.
    // The asset can be edited in Play Mode; changes persist after leaving Play Mode.
    [CreateAssetMenu(menuName = "Weapons/Hand Grip Profile", fileName = "HandGripProfile")]
    public class HandGripProfile : ScriptableObject
    {
        [System.Serializable]
        public class HandPose
        {
            [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.25f), Tooltip("Где запястье относительно оружия, метры")]
            public Vector3 wristPosition;
            [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 180f), Tooltip("Поворот кисти относительно оружия, градусы")]
            public Vector3 wristRotation;
            [Vector3Slider(Vector3SliderAttribute.Kind.Position, 1f), Tooltip("Куда направлен локоть (подсказка для IK), метры")]
            public Vector3 elbowHint;

            [Header("Сгиб пальцев, градусы на каждый сустав")]
            [Range(-40f, 110f)] public float thumb;
            [Range(-40f, 110f)] public float index;
            [Range(-40f, 110f)] public float middle;
            [Range(-40f, 110f)] public float ring;
            [Range(-40f, 110f)] public float pinky;
        }

        [BoxGroup("Правая рука — держит рукоять")] public HandPose right = new HandPose
        {
            wristPosition = new Vector3(0.015f, -0.03f, -0.07f),
            elbowHint = new Vector3(0.3f, -0.4f, -0.25f),
            thumb = 25f, index = 25f, middle = 65f, ring = 70f, pinky = 75f
        };

        [BoxGroup("Левая рука — поддерживает")] public HandPose left = new HandPose
        {
            wristPosition = new Vector3(-0.055f, -0.065f, -0.035f),
            elbowHint = new Vector3(-0.35f, -0.4f, -0.2f),
            thumb = 10f, index = 55f, middle = 60f, ring = 65f, pinky = 70f
        };

        // ---------- Left hand moves away while the cylinder is open ----------
        [BoxGroup("Левая рука — убрана (барабан открыт)")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.5f), Tooltip("Куда уходит запястье, метры относительно оружия")]
        public Vector3 leftAwayPosition = new Vector3(-0.2f, -0.3f, -0.05f);

        [BoxGroup("Левая рука — убрана (барабан открыт)")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 180f), Tooltip("Доворот кисти относительно её поворота на рукояти")]
        public Vector3 leftAwayRotationOffset = new Vector3(0f, 0f, 0f);

        [BoxGroup("Левая рука — убрана (барабан открыт)")]
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 1f), Tooltip("Куда смотрит локоть, когда рука убрана")]
        public Vector3 leftAwayElbowHint = new Vector3(-0.4f, -0.5f, -0.2f);

        [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(-40f, 110f), Tooltip("Сгиб пальцев расслабленной руки")]
        public float leftAwayFingerCurl = 15f;

        [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(0.1f, 2f), Tooltip("Сколько секунд рука уходит / возвращается")]
        public float leftHandMoveTime = 0.45f;

        [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(0f, 0.2f), Tooltip("Насколько рука уходит дугой наружу, а не по прямой")]
        public float leftHandArc = 0.06f;
    }
}

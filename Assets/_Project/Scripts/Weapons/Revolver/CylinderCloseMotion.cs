using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Weapons.Revolver
{
    // One way of closing the cylinder, described as data: a list of phases. Each phase moves the weapon
    // (rotation around the wrist on the grip and a shift) to its end pose, may snap the crane shut
    // (the latch comes at the end of that phase) and may bring the left palm to the cylinder or send the
    // left hand back to the grip. A new way of closing is a new asset of this type, no code needed.
    // Played by RevolverClose; which asset plays is picked at random in CylinderCloseProfile.
    [CreateAssetMenu(menuName = "Pistol/Profiles/Cylinder Close Motion", fileName = "CylinderCloseMotion")]
    public class CylinderCloseMotion : ScriptableObject
    {
        public enum Ease { Smooth, FastStart, FastEnd, Linear }

        [Serializable]
        public class Phase
        {
            [Tooltip("Название фазы (только для удобства)")]
            public string name = "Фаза";
            [Range(0.02f, 1f), Tooltip("Длина фазы, сек")]
            public float duration = 0.15f;
            [Tooltip("Поворот оружия вокруг кисти к концу фазы, градусы. X: + ствол вниз, − вверх; Z: + крен влево, − вправо. Если едет не туда — поменяйте знак")]
            public Vector3 rotation;
            [Tooltip("Сдвиг оружия к концу фазы, м. X: + вправо, − влево")]
            public Vector3 offset;
            [Tooltip("Как идёт движение: плавно; резко в начале (рывок); разгон к концу; равномерно")]
            public Ease ease = Ease.Smooth;
            [Tooltip("В этой фазе кран захлопывается: щелчок — ровно в конце фазы")]
            public bool craneCloses;
            [Range(0f, 1f), Tooltip("Насколько левая ладонь у барабана к концу фазы: 0 — нет, 1 — прижата")]
            public float leftPalmOnCylinder;
            [Tooltip("В начале фазы левая рука возвращается на рукоять")]
            public bool leftHandToGrip;
        }

        [BoxGroup("Фазы"), Tooltip("Фазы по порядку. Последняя фаза должна вернуть оружие в 0")]
        public List<Phase> phases = new List<Phase>();

        [BoxGroup("Левая ладонь"), Tooltip("Где кисть относительно центра барабана, м, в осях оружия (X вправо, Y вверх, Z к дулу). Кисть следует за барабаном, пока он закрывается")]
        public Vector3 palmOffset = new Vector3(-0.035f, -0.01f, -0.06f);
        [BoxGroup("Левая ладонь"), Tooltip("Поворот кисти относительно её обычного хвата, градусы")]
        public Vector3 palmRotation = Vector3.zero;
        [BoxGroup("Левая ладонь"), Range(0f, 90f), Tooltip("Сгиб пальцев у раскрытой ладони, градусы (0 — прямые)")]
        public float palmFingerCurl = 5f;

        [BoxGroup("Щелчок"), Range(0f, 1f), Tooltip("Сила щелчка: громкость звука и толчки ниже умножаются на это число")]
        public float latchStrength = 1f;
        [BoxGroup("Щелчок"), Tooltip("Толчок оружия при защёлкивании, градусы (те же оси, что у фаз)")]
        public Vector3 latchKick = new Vector3(1f, 0f, -2f);
        [BoxGroup("Щелчок"), Range(0.02f, 0.3f), Tooltip("Длина толчка, сек")]
        public float latchKickTime = 0.08f;
        [BoxGroup("Щелчок"), Range(0f, 0.5f), Tooltip("Тряска камеры при защёлкивании")]
        public float latchCameraShake = 0.05f;

        [ShowNativeProperty] public float Duration
        {
            get { float d = 0f; foreach (var p in phases) d += p.duration; return d; }
        }

        public static float Apply(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            return ease switch
            {
                Ease.FastStart => 1f - (1f - t) * (1f - t),
                Ease.FastEnd => t * t,
                Ease.Linear => t,
                _ => t * t * (3f - 2f * t),
            };
        }
    }
}

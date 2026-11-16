using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Items
{
    // Attachment points of a held item, assigned in the prefab: nothing looks them up by name.
    // Weapons take the muzzle and the sights from here; the arms take the grip points and elbow hints.
    // Goes on the item root (Revolver).
    [DisallowMultipleComponent]
    public class ItemRig : MonoBehaviour
    {
        [BoxGroup("Ствол"), SerializeField, Tooltip("Дуло: откуда вылетает пуля и где вспышка. Для предметов без ствола — пусто")]
        Transform muzzle;

        [BoxGroup("Прицел"), SerializeField, Tooltip("Целик: в прицеле оружие вращается вокруг него, он остаётся у глаза. Для предметов без прицела — пусто")]
        Transform rearSight;
        [BoxGroup("Прицел"), SerializeField, Tooltip("Мушка: на неё фокусируется глаз в прицеле. Для предметов без прицела — пусто")]
        Transform frontSight;

        [BoxGroup("Хват"), SerializeField, Tooltip("Точка хвата правой руки (IK-цель кисти)")]
        Transform rightHandGrip;
        [BoxGroup("Хват"), SerializeField, Tooltip("Точка хвата левой руки (IK-цель кисти)")]
        Transform leftHandGrip;
        [BoxGroup("Хват"), SerializeField, Tooltip("Куда смотрит правый локоть (подсказка IK)")]
        Transform rightElbowHint;
        [BoxGroup("Хват"), SerializeField, Tooltip("Куда смотрит левый локоть (подсказка IK)")]
        Transform leftElbowHint;

        public Transform Muzzle => muzzle;
        public Transform RearSight => rearSight;
        public Transform FrontSight => frontSight;
        public Transform RightHandGrip => rightHandGrip;
        public Transform LeftHandGrip => leftHandGrip;
        public Transform RightElbowHint => rightElbowHint;
        public Transform LeftElbowHint => leftElbowHint;

        // Muzzle, or the item root if there is none
        public Transform MuzzleOrRoot => muzzle != null ? muzzle : transform;

        // A point of the rig in item space (relative to the item root), or the fallback if it is not assigned
        public Vector3 LocalPoint(Transform point, Vector3 fallback) =>
            point != null ? transform.InverseTransformPoint(point.position) : fallback;

        // The ItemRig on the given item root; logs an error for the caller if it is missing
        public static ItemRig On(Component caller)
        {
            var rig = caller.GetComponent<ItemRig>();
            if (rig == null) Debug.LogError($"{caller.GetType().Name}: no ItemRig on {caller.name}. Add an ItemRig and assign its points.", caller);
            return rig;
        }
    }
}

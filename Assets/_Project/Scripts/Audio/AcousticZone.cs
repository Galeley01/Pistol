using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Audio
{
    // A box where the place sounds its own way (e.g. a closed hall inside the outdoor range): while the listener is
    // inside, RangeAcoustics uses this profile instead of its default one. Uses the BoxCollider on the same object
    // only for its size (make it a trigger, so it doesn't block anything).
    //
    // Goes on a child of the RangeAcoustics object.
    [RequireComponent(typeof(BoxCollider))]
    public class AcousticZone : MonoBehaviour
    {
        [SerializeField, Required("Назначьте профиль звука этого места"), Tooltip("Как звучит выстрел внутри этой зоны")]
        AcousticsProfile profile;

        BoxCollider box;

        public AcousticsProfile Profile => profile;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        public bool Contains(Vector3 point)
        {
            if (box == null) return false;
            Vector3 local = transform.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}

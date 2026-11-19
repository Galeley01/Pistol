using UnityEngine;
using NaughtyAttributes;
using Pistol.Items;

namespace Pistol.Arms
{
    // The player's arms: creates the arms of the chosen skin under the camera and makes them hold
    // whatever ItemHolderWithPose holds. The skin is chosen before Play (field Arms).
    //
    // Goes on its own object under the camera.
    [DisallowMultipleComponent]
    public class PlayerArms : MonoBehaviour
    {
        [BoxGroup("Руки"), Required("Назначьте руки (ArmsDefinition)"), SerializeField]
        [Tooltip("Какие руки (скин). Выбирается до Play. Префаб собирается кнопкой «Собрать префаб рук» в самом ассете")]
        ArmsDefinition arms;
        [BoxGroup("Руки"), Required("Назначьте ItemHolderWithPose"), SerializeField, Tooltip("Чей предмет держат руки")]
        ItemHolderWithPose holder;

        ArmsRig rig;
        public ArmsRig Rig => rig;

        void Awake()
        {
            if (arms == null || arms.prefab == null)
            {
                Debug.LogError("PlayerArms: no arms prefab. Open the ArmsDefinition and press \"Build arms prefab\".", this);
                enabled = false;
                return;
            }
            var go = Instantiate(arms.prefab, transform, false);
            go.name = arms.prefab.name;
            Pistol.Core.FirstPersonShadows.TurnOff(go);   // first-person arms throw no shadow into the world
            go.transform.SetLocalPositionAndRotation(arms.rootLocalPosition, Quaternion.identity);
            rig = go.GetComponent<ArmsRig>();
            if (rig == null) Debug.LogError($"PlayerArms: the prefab {arms.prefab.name} has no ArmsRig. Rebuild it.", this);
        }

        void Start()
        {
            if (rig == null || holder == null) return;
            holder.ItemChanged += rig.Attach;
            rig.Attach(holder.Current);

            // Debug: fingers from a hand tracking tool (DebugSettings → Live Fingers), editor and Development builds only
            if (Pistol.Core.DebugSettings.Allowed && holder.Context?.Debug != null)
                rig.UseLiveFingers(gameObject.AddComponent<LiveHandReceiver>(), holder.Context.Debug,
                                   holder.Context.Camera != null ? holder.Context.Camera.transform : transform.parent);
        }

#if UNITY_EDITOR
        // Live tuning: the root position from the ArmsDefinition is applied every frame in the editor
        void Update()
        {
            if (rig != null) rig.transform.localPosition = arms.rootLocalPosition;
        }
#endif

        void OnDestroy()
        {
            if (rig != null && holder != null) holder.ItemChanged -= rig.Attach;
        }
    }
}

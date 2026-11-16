using UnityEngine;
using System.Collections.Generic;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Player;
using Pistol.PostFx;

namespace Pistol.Items
{
    // Holds the item in the hands in front of the camera and is the ONLY script that writes its pose.
    // Every frame it asks the item's pose layers (IPoseLayer: aim, sway, recoil...) for their part,
    // in a fixed order, and writes the composed pose into the item once.
    //
    // Runs after the player's input and movement (default order) and before animation, so the arms IK
    // always sees this frame's weapon pose.
    //
    // Goes on its own object under the camera; the held item (Revolver) is its child.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class ItemHolderWithPose : MonoBehaviour
    {
        [BoxGroup("Предмет"), Required("Назначьте предмет в руках (дочерний объект)"), SerializeField]
        [Tooltip("Предмет в руках. Его позу пишет только этот компонент, слои позы лежат на самом предмете")]
        Transform item;

        [BoxGroup("Игрок"), Required("Назначьте камеру игрока"), SerializeField, Tooltip("Камера игрока")]
        Camera viewCamera;
        [BoxGroup("Игрок"), Required("Назначьте PlayerMovement игрока"), SerializeField, Tooltip("Движение игрока: скорость, бег, усталость, Free-Aim")]
        PlayerMovement movement;
        [BoxGroup("Игрок"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод и режим управления игрока")]
        PlayerContext player;

        [BoxGroup("Сервисы"), SerializeField, Tooltip("Звук игры: через него предмет проигрывает свои звуки. Пусто — предмет молчит")]
        GameAudio gameAudio;

        readonly PoseStack stack = new PoseStack();
        Vector3 restPosition;
        Quaternion restRotation;
        PoseFrame last;

        public Transform Item => item;
        public ItemContext Context { get; private set; }

        [ShowNativeProperty] public int PoseLayers => stack.Count;
        [ShowNativeProperty] public float Aim => last.Aim;
        [ShowNativeProperty] public float Inspect => last.Inspect;

        void Awake()
        {
            if (item == null) { Debug.LogError("ItemHolderWithPose: no item assigned.", this); enabled = false; return; }
            if (item.parent != transform)
                Debug.LogWarning("ItemHolderWithPose: the item should be a child of this object, otherwise its pose is in the wrong space.", this);

            var controller = movement != null ? movement.GetComponent<CharacterController>() : null;
            var effects = viewCamera != null ? viewCamera.GetComponent<CameraEffects>() : null;
            if (effects == null) Debug.LogWarning("ItemHolderWithPose: no CameraEffects on the camera, aim and inspect screen effects are off.", this);
            if (gameAudio == null) Debug.LogWarning("ItemHolderWithPose: no GameAudio assigned, the item is silent.", this);
            Context = new ItemContext(viewCamera, movement, controller, player, effects, gameAudio, this);

            // Pose without any layers: where the item is placed in the scene
            restPosition = item.localPosition;
            restRotation = item.localRotation;
            stack.Collect(item.gameObject);

            // Every part of the item that needs the context (pose layers, feedback...) gets it once
            var users = new List<IItemContextUser>();
            item.GetComponents(users);
            foreach (var user in users) user.Bind(Context);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var frame = PoseFrame.Begin(dt, restPosition, restRotation);
            stack.Apply(ref frame);
            frame.Compose(out Vector3 position, out Quaternion rotation);
            item.SetLocalPositionAndRotation(position, rotation);
            last = frame;
        }
    }
}

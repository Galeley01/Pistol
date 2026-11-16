using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Ballistics;
using Pistol.Core;
using Pistol.Player;
using Pistol.PostFx;

namespace Pistol.Items
{
    // Holds the item in the hands in front of the camera and is the ONLY script that writes its pose.
    // Every frame it asks the item's pose layers (IPoseLayer: aim, sway, recoil...) for their part,
    // in a fixed order, and writes the composed pose into the item once.
    //
    // Equip puts an item (HeldItem) into the hands: it becomes a child of this object and every part of it
    // that needs the player context (IItemContextUser) gets it. Unequip lets it go.
    //
    // Runs after the player's input and movement (default order) and before animation, so the arms IK
    // always sees this frame's weapon pose.
    //
    // Goes on its own object under the camera.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class ItemHolderWithPose : MonoBehaviour
    {
        [BoxGroup("Предмет"), SerializeField]
        [Tooltip("Предмет, который в руках при старте (дочерний объект с HeldItem). Его позу пишет только этот компонент, слои позы лежат на самом предмете")]
        Transform item;

        [BoxGroup("Игрок"), Required("Назначьте камеру игрока"), SerializeField, Tooltip("Камера игрока")]
        Camera viewCamera;
        [BoxGroup("Игрок"), Required("Назначьте PlayerMovement игрока"), SerializeField, Tooltip("Движение игрока: скорость, бег, усталость, Free-Aim")]
        PlayerMovement movement;
        [BoxGroup("Игрок"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод и режим управления игрока")]
        PlayerContext player;

        [BoxGroup("Сервисы"), SerializeField, Tooltip("Звук игры: через него предмет проигрывает свои звуки. Пусто — предмет молчит")]
        GameAudio gameAudio;
        [BoxGroup("Сервисы"), SerializeField, Tooltip("Полёт пуль и попадания. Пусто — оружие не стреляет")]
        BulletSystem bulletSystem;
        [BoxGroup("Сервисы"), SerializeField, Tooltip("Отладочные флаги (логи, линии пуль). Пусто — без отладки")]
        DebugSettings debugSettings;

        readonly PoseStack stack = new PoseStack();
        readonly List<IItemContextUser> users = new List<IItemContextUser>();
        HeldItem current;
        Vector3 restPosition;
        Quaternion restRotation;
        PoseFrame last;

        public HeldItem Current => current;
        public ItemContext Context { get; private set; }

        // Raised after an item is equipped (the item) and after it is unequipped (null)
        public event Action<HeldItem> ItemChanged;

        [ShowNativeProperty] public string CurrentItem => current != null ? current.name : "—";
        [ShowNativeProperty] public int PoseLayers => stack.Count;
        [ShowNativeProperty] public float Aim => last.Aim;
        [ShowNativeProperty] public float Inspect => last.Inspect;

        void Awake()
        {
            var controller = movement != null ? movement.GetComponent<CharacterController>() : null;
            var effects = viewCamera != null ? viewCamera.GetComponent<CameraEffects>() : null;
            if (effects == null) Debug.LogWarning("ItemHolderWithPose: no CameraEffects on the camera, aim and inspect screen effects are off.", this);
            if (gameAudio == null) Debug.LogWarning("ItemHolderWithPose: no GameAudio assigned, the item is silent.", this);
            if (bulletSystem == null) Debug.LogWarning("ItemHolderWithPose: no BulletSystem assigned, weapons can't shoot.", this);
            Context = new ItemContext(viewCamera, movement, controller, player, effects, gameAudio, bulletSystem, debugSettings, this);

            if (item != null)
            {
                var held = item.GetComponent<HeldItem>();
                if (held != null) Equip(held);
                else Debug.LogError("ItemHolderWithPose: the start item has no HeldItem component.", item);
            }
        }

        // Puts the item into the hands. Call it right after Instantiate (before the item's Start),
        // so its parts get the context before they start.
        public void Equip(HeldItem newItem)
        {
            if (newItem == null || newItem == current) return;
            Unequip();

            current = newItem;
            var t = newItem.transform;
            if (t.parent != transform) t.SetParent(transform, false);

            // Pose without any layers: where the item is placed (scene or prefab)
            restPosition = t.localPosition;
            restRotation = t.localRotation;
            stack.Collect(newItem.gameObject);

            // Every part of the item that needs the context (pose layers, feedback, firing...) gets it once
            newItem.GetComponents(users);
            foreach (var user in users) user.Bind(Context);

            last = default;
            ItemChanged?.Invoke(newItem);
        }

        // Lets the item go: it stays where it is, the caller decides whether to hide or destroy it
        public void Unequip()
        {
            if (current == null) return;
            current = null;
            stack.Clear();
            users.Clear();
            last = default;
            ItemChanged?.Invoke(null);
        }

        void Update()
        {
            if (current == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var frame = PoseFrame.Begin(dt, restPosition, restRotation);
            stack.Apply(ref frame);
            frame.Compose(out Vector3 position, out Quaternion rotation);
            current.transform.SetLocalPositionAndRotation(position, rotation);
            last = frame;
        }
    }
}

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
        [Tooltip("Что в руках при старте. Префаб (например, Revolver или Revolver_Stainless) создаётся под этим объектом; " +
                 "объект из сцены просто берётся в руки. Позу предмета пишет только этот компонент, слои позы лежат на самом предмете")]
        HeldItem startItem;

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
        int equipFrame = -1;

        public HeldItem Current => current;
        // The prefab (or scene object) the holder started with
        public HeldItem StartItem => startItem;
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

#if UNITY_EDITOR
            ClearPreview();   // never hold the editor preview
#endif
            if (startItem != null)
            {
                // A prefab asset is created here; an object already in the scene is taken as it is
                bool inScene = startItem.gameObject.scene.IsValid();
                var held = inScene ? startItem : Instantiate(startItem, transform, false);
                if (!inScene) held.name = startItem.name;
                Equip(held);
            }
        }

#if UNITY_EDITOR
        // ---------- Editor preview ----------
        // Outside Play Mode the start item prefab is shown under the holder, so the weapon is seen in the scene.
        // The preview is never saved into the scene and is removed before Play, where the real item is created.
        void OnValidate() => UnityEditor.EditorApplication.delayCall += RefreshPreview;

        void RefreshPreview()
        {
            if (this == null || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;
            ClearPreview();
            if (startItem == null || startItem.gameObject.scene.IsValid()) return;   // a scene object is seen anyway

            var preview = Instantiate(startItem.gameObject, transform, false);
            preview.name = startItem.name + " (preview)";
            foreach (var t in preview.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        }

        void ClearPreview()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if ((child.hideFlags & HideFlags.DontSave) == HideFlags.DontSave) DestroyImmediate(child);
            }
        }

        // Remove the previews before Play and bring them back after it
        [UnityEditor.InitializeOnLoadMethod]
        static void WatchPlayMode()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                foreach (var holder in FindObjectsByType<ItemHolderWithPose>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (state == UnityEditor.PlayModeStateChange.ExitingEditMode) holder.ClearPreview();
                    else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) holder.RefreshPreview();
                }
            };
        }
#endif

        // Puts the item into the hands. Call it right after Instantiate (before the item's Start),
        // so its parts get the context before they start.
        public void Equip(HeldItem newItem)
        {
            if (newItem == null || newItem == current) return;
            Unequip();

            current = newItem;
            FirstPersonShadows.TurnOff(newItem.gameObject);   // the weapon at the face throws no shadow into the world
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
            equipFrame = Time.frameCount;
            ItemChanged?.Invoke(newItem);
        }

        // Lets the item go: every part gives back what it changed outside the item (Unbind) and the item returns
        // to its rest pose. It stays a child of the holder; the caller decides whether to hide, drop or destroy it.
        public void Unequip()
        {
            if (current == null) return;
            var item = current;
            current = null;

            // Reverse order: the parts bound last let go first
            for (int i = users.Count - 1; i >= 0; i--) users[i].Unbind();
            users.Clear();
            stack.Clear();
            last = default;

            // Without this the next Equip would take the last swayed pose as the rest pose
            if (item != null) item.transform.SetLocalPositionAndRotation(restPosition, restRotation);
            ItemChanged?.Invoke(null);
        }

        // ---------- Test buttons (Play Mode) ----------
        HeldItem putAway;

        [Button("Тест: убрать предмет из рук")]
        void TestPutAway()
        {
            if (!Application.isPlaying || current == null) return;
            putAway = current;
            Unequip();
            putAway.gameObject.SetActive(false);
        }

        [Button("Тест: взять предмет обратно")]
        void TestTakeBack()
        {
            if (!Application.isPlaying || putAway == null || current != null) return;
            putAway.gameObject.SetActive(true);
            Equip(putAway);
            putAway = null;
        }

        void Update()
        {
            if (current == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // An item taken mid-frame (switched to) hasn't run its Start yet: its pose layers start next frame,
            // until then it stays at its rest pose
            if (Time.frameCount == equipFrame) return;

            var frame = PoseFrame.Begin(dt, restPosition, restRotation);
            stack.Apply(ref frame);
            frame.Compose(out Vector3 position, out Quaternion rotation);
            current.transform.SetLocalPositionAndRotation(position, rotation);
            last = frame;
        }
    }
}

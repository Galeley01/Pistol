using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Player
{
    // Movement tuning for PlayerMovement: speeds, body inertia, fatigue, crouch, look and Free-Aim.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Movement", fileName = "MovementProfile")]
    public class MovementProfile : ScriptableObject
    {
        [BoxGroup("Движение"), MinValue(0)] public float walkSpeed = 3f;
        [BoxGroup("Движение"), MinValue(0)] public float runSpeed = 6f;
        [BoxGroup("Движение"), Tooltip("Плавность разгона/торможения при ходьбе")] public float acceleration = 10f;

        [BoxGroup("Инерция тела"), Range(1f, 20f), Tooltip("Разгон до бега, м/с². Меньше = тяжелее разгоняется")]
        public float runAcceleration = 5f;
        [BoxGroup("Инерция тела"), Range(1f, 30f), Tooltip("Торможение, м/с². Меньше = дольше останавливается")]
        public float deceleration = 9f;
        [BoxGroup("Инерция тела"), Range(30f, 720f), Tooltip("Максимальная скорость поворота на бегу, °/с")]
        public float sprintTurnSpeed = 170f;
        [BoxGroup("Движение")] public float jumpHeight = 1.2f;
        [BoxGroup("Движение")] public float gravity = -20f;

        [BoxGroup("Усталость"), MinValue(0.1f)] public float sprintTimeBeforeTired = 8f;
        [BoxGroup("Усталость"), MinValue(0)]
        [ValidateInput("IsBuildStartValid", "Должно быть меньше Sprint Time Before Tired")]
        [Tooltip("С какой секунды бега начинает нарастать тряска перед усталостью")]
        public float fatigueBuildStart = 6f;
        [BoxGroup("Усталость"), MinValue(0)] public float tiredRunSpeed = 4.5f;
        [BoxGroup("Усталость"), MinValue(0)] public float recoveryDuration = 5f;
        [BoxGroup("Усталость"), Tooltip("Как быстро «остывает» счётчик бега, если остановился раньше, чем устал")]
        public float sprintCooldownRate = 2f;
        [BoxGroup("Усталость"), Tooltip("Насколько плавно падает скорость при усталости")]
        public float tiredSpeedChange = 1.5f;

        [BoxGroup("Приседание (C)")] public float crouchHeight = 1.2f;
        [BoxGroup("Приседание (C)")] public float crouchWalkSpeed = 1.6f;
        [BoxGroup("Приседание (C)"), Tooltip("Медленнее бега стоя, без усталости")] public float crouchRunSpeed = 3f;
        [BoxGroup("Приседание (C)")] public float crouchTransitionSpeed = 10f;

        [BoxGroup("Обзор")] public float mouseSensitivity = 1f;
        [BoxGroup("Обзор"), Range(10f, 89f)] public float maxLookAngle = 85f;

        [BoxGroup("Свободное прицеливание (Free-Aim)"), Tooltip("Небольшие движения мыши сначала двигают только оружие, а камера поворачивается, когда оружие выходит за мёртвую зону")]
        public bool freeAim = true;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 20f), Tooltip("Мёртвая зона по горизонтали, градусы")]
        public float deadzoneYaw = 7f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 15f), Tooltip("Мёртвая зона по вертикали, градусы")]
        public float deadzonePitch = 4f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(100f, 2000f), Tooltip("Скорость мыши (°/с), при которой камера поворачивается сразу, минуя мёртвую зону (резкий разворот)")]
        public float fastTurnSpeed = 450f;
        [BoxGroup("Свободное прицеливание (Free-Aim)"), ShowIf("freeAim"), Range(0f, 5f), Tooltip("Корпус медленно доворачивается за оружием. 0 = выключено")]
        public float bodyFollowSpeed = 0f;

        bool IsBuildStartValid(float value) => value < sprintTimeBeforeTired;
    }
}

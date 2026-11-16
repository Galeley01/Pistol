using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Arms
{
    // One arms skin: the model, how its bones are named (roles -> bone names) and where it sits under the camera.
    // Skeletons differ between skins, so all arms code works with roles (upper arm, forearm, hand, finger joints)
    // and never with bone names. The names are used only once, by the "Build arms prefab" button (editor),
    // which finds the bones, builds the IK rig and saves a prefab with direct references.
    [CreateAssetMenu(menuName = "Pistol/Arms Definition", fileName = "ArmsDefinition")]
    public class ArmsDefinition : ScriptableObject
    {
        [System.Serializable]
        public class FingerBones
        {
            [Tooltip("Сустав 1 (у ладони)")] public string joint1;
            [Tooltip("Сустав 2")] public string joint2;
            [Tooltip("Сустав 3 (кончик)")] public string joint3;

            public string this[int i] => i == 0 ? joint1 : i == 1 ? joint2 : joint3;
        }

        [System.Serializable]
        public class ArmBones
        {
            [Tooltip("Плечо (верхняя часть руки)")] public string upperArm;
            [Tooltip("Предплечье: кость, которая сгибается в локте. Кости скручивания между ней и кистью можно не указывать")] public string forearm;
            [Tooltip("Кисть (запястье)")] public string hand;
            public FingerBones thumb = new FingerBones();
            public FingerBones index = new FingerBones();
            public FingerBones middle = new FingerBones();
            public FingerBones ring = new FingerBones();
            public FingerBones pinky = new FingerBones();

            // 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky
            public FingerBones Finger(int f) => f == 0 ? thumb : f == 1 ? index : f == 2 ? middle : f == 3 ? ring : pinky;
        }

        [BoxGroup("Модель"), Required("Назначьте модель рук (FBX)"), Tooltip("Модель рук (FBX). Из неё кнопка собирает префаб с ригом")]
        public GameObject model;
        [BoxGroup("Модель"), ReadOnly, Tooltip("Готовый префаб рук с ригом. Создаётся кнопкой «Собрать префаб рук»")]
        public GameObject prefab;
        [BoxGroup("Модель"), Tooltip("Где корень рук относительно камеры, м")]
        public Vector3 rootLocalPosition = new Vector3(0f, -0.28f, -0.08f);

        [BoxGroup("Кости правой руки")] public ArmBones right = new ArmBones();
        [BoxGroup("Кости левой руки")] public ArmBones left = new ArmBones();

        [BoxGroup("Сборка"), Tooltip("Вкл: ладони в исходной позе модели смотрят ровно вниз (T-поза), оси сгиба пальцев считаются от этого.\nВыкл: направление ладони считается по костям (для рук, которые в файле уже согнуты или вытянуты вперёд)")]
        public bool palmsDownInRest = false;
        [BoxGroup("Сборка"), Tooltip("Перевести старые профили хвата (повороты кисти в осях костей этих рук) в общий формат. Нужно один раз, для рук, под которые подбирался старый хват")]
        public bool convertLegacyGrips = false;

        public ArmBones Side(bool rightSide) => rightSide ? right : left;
    }
}

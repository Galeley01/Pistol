using System;
using System.Collections.Generic;
using UnityEngine;
using Pistol.Audio;

namespace Pistol.Weapons.Revolver
{
    // Which ways of closing the cylinder the revolver uses and how often: every close picks one at random
    // by weight, but never the same one more than "maxSameInRow" times in a row.
    [CreateAssetMenu(menuName = "Pistol/Profiles/Cylinder Close", fileName = "CylinderCloseProfile")]
    public class CylinderCloseProfile : ScriptableObject
    {
        [Serializable]
        public class Option
        {
            [Tooltip("Ассет движения закрытия")]
            public CylinderCloseMotion motion;
            [Range(0f, 10f), Tooltip("Вес: насколько часто выпадает этот способ. 0 — выключен")]
            public float weight = 1f;
        }

        [Tooltip("Способы закрытия и их веса (одинаковые веса — поровну)")]
        public List<Option> options = new List<Option>();
        [Range(0, 5), Tooltip("Сколько раз подряд может выпасть один и тот же способ. 0 — без ограничения")]
        public int maxSameInRow = 2;
        [Tooltip("Звук закрытия барабана (необязательно). Громкость умножается на силу щелчка способа")]
        public SoundEvent latchSound;
        [Range(0f, 2f), Tooltip("Где в звуке сам щелчок, сек от начала файла. Звук запускается заранее, чтобы щелчок совпал с защёлкиванием; " +
                                "если движение короче, начало звука пропускается")]
        public float latchSoundClickTime = 0.3f;
    }
}

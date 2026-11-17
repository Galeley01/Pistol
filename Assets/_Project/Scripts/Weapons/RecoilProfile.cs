using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Weapons
{
    // Shot recoil: how hard the weapon flips and how much the camera kicks.
    // Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Profiles/Weapon Recoil", fileName = "RecoilProfile")]
    public class RecoilProfile : ScriptableObject
    {
        [BoxGroup("Отдача оружия"), Range(0f, 30f), Tooltip("На сколько градусов подлетает ствол от бедра (верхняя точка)")]
        public float flipAngle = 10f;
        [BoxGroup("Отдача оружия"), Range(0f, 1.5f), Tooltip("Во сколько раз подлёт в прицеле (хват двумя руками у глаза крепче)")]
        public float aimFlipMultiplier = 0.7f;
        [BoxGroup("Отдача оружия"), Range(0f, 0.3f), Tooltip("Разброс силы подлёта от выстрела к выстрелу (0.1 = ±10%)")]
        public float flipVariation = 0.1f;
        [BoxGroup("Отдача оружия"), Range(0f, 5f), Tooltip("Случайный увод ствола вбок, градусы. Маленький — револьвер не мотает")]
        public float sideAngle = 0.6f;
        [BoxGroup("Отдача оружия"), Range(0f, 8f), Tooltip("Случайный крен, градусы")]
        public float rollAngle = 1f;
        [BoxGroup("Отдача оружия"), Range(0f, 0.06f), Tooltip("На сколько рука с оружием уходит назад, к лицу, м (с запаздыванием «Задержка руки»)")]
        public float recoilBack = 0.018f;
        [BoxGroup("Отдача оружия"), Range(0f, 0.02f), Tooltip("На сколько поднимается вся рука, м. Идёт с запаздыванием и на кивке опускается ниже исходного — вместе с подлётом ствола дуло рисует петлю")]
        public float recoilUp = 0.006f;
        [BoxGroup("Отдача оружия"), Range(0f, 1f), Tooltip("Насколько выстрел сбивает хват (ствол гуляет, пока не стабилизируется)")]
        public float gripDisturb = 0.35f;
        [BoxGroup("Отдача оружия"), Range(1f, 2f), Tooltip("Во сколько раз сильнее отдача у измотанного персонажа")]
        public float tiredMultiplier = 1.35f;

        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0.01f, 0.15f), Tooltip("За сколько секунд ствол подлетает до верхней точки. Меньше — резче")]
        public float recoilRiseTime = 0.035f;
        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0.03f, 0.4f), Tooltip("За сколько секунд ствол падает от верхней точки до нижней точки кивка")]
        public float recoilReturnTime = 0.12f;
        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0f, 0.6f), Tooltip("Кивок: насколько ствол проскакивает ниже исходного, доля от высоты подлёта (0.25 = 25%). 0 — без кивка")]
        public float nodDepth = 0.25f;
        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0.05f, 1f), Tooltip("За сколько секунд ствол выравнивается из нижней точки кивка. Больше — медленнее, оружие «тяжелее»")]
        public float settleTime = 0.3f;
        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0f, 3f), Tooltip("Одно короткое движение вбок во время выравнивания, градусы. Сторона случайная")]
        public float settleSideAngle = 0.5f;
        [BoxGroup("Подлёт, кивок и выравнивание"), Range(0f, 0.1f), Tooltip("На сколько секунд вся рука отстаёт от поворота кисти. Из-за этого подъём и возврат идут разными путями (петля). 0 — одна дуга")]
        public float armLag = 0.03f;

        [BoxGroup("Усталость и бег"), Range(0f, 1f), Tooltip("Насколько бег утяжеляет кивок и выравнивание, как доля от полной усталости")]
        public float runStrain = 0.5f;
        [BoxGroup("Усталость и бег"), Range(1f, 2.5f), Tooltip("Во сколько раз глубже кивок у уставшего (или бегущего, с учётом доли выше)")]
        public float strainNodMultiplier = 1.5f;
        [BoxGroup("Усталость и бег"), Range(1f, 3f), Tooltip("Во сколько раз медленнее выравнивание у уставшего (или бегущего)")]
        public float strainSettleMultiplier = 1.6f;

        [BoxGroup("Отдача камеры"), Range(0f, 3f), Tooltip("Насколько градусов коротко подбрасывает голову (потом возвращается)")]
        public float cameraFlip = 0.5f;
        [BoxGroup("Отдача камеры"), Range(0f, 2f), Tooltip("Случайный крен головы, градусы")]
        public float cameraRollAngle = 0.3f;
        [BoxGroup("Отдача камеры"), Range(0f, 1f), Tooltip("Резкая тряска камеры. Для револьвера — совсем немного")]
        public float cameraJolt = 0.06f;
        [BoxGroup("Отдача камеры"), Range(0f, 3f), Tooltip("Насколько взгляд остаётся выше после выстрела, градусы (подъём прицела)")]
        public float viewClimb = 0.4f;
        [BoxGroup("Отдача камеры"), Range(0f, 1f), Tooltip("Случайный увод взгляда вбок, градусы")]
        public float viewClimbSide = 0.15f;
    }
}

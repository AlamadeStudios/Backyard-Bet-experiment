using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Хмель: после глотка из бутылки картинка начинает вести.
    ///
    /// Игра про пьяную дворовую вечеринку, и выпивка должна что-то значить.
    /// Здесь она меняет не цифры, а управление взглядом: чем больше выпито,
    /// тем сильнее ведёт камеру - бросать и целиться становится труднее.
    /// Со временем отпускает.
    ///
    /// Качаем саму камеру, а не поворотную точку: точку крутит обзор мышью и
    /// посадка за стол, и покачивание бы с ними спорило.
    /// </summary>
    public class Tipsy : MonoBehaviour
    {
        [Tooltip("Сколько хмеля даёт один глоток, от 0 до 1.")]
        public float perSip = 0.3f;

        [Tooltip("Насколько хмель спадает за секунду.")]
        public float sober = 0.04f;

        [Tooltip("Наибольший увод взгляда на полном хмеле, градусов.")]
        public float maxSway = 6.5f;

        /// <summary>Сколько выпито, от 0 до 1.</summary>
        public float Level { get; private set; }

        Transform _cam;
        float _phase;

        void Start()
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) _cam = cam.transform;
            _phase = Random.value * 10f;
        }

        /// <summary>Глоток. Возвращает, стало ли заметно хуже.</summary>
        public bool Sip()
        {
            float was = Level;
            Level = Mathf.Min(1f, Level + perSip);
            return Level > was;
        }

        void Update()
        {
            if (_cam == null) return;

            Level = Mathf.Max(0f, Level - sober * Time.deltaTime);
            if (Level <= 0.001f)
            {
                _cam.localRotation = Quaternion.identity;
                return;
            }

            // Две несовпадающие волны разной частоты: одна синусоида читается
            // как качели, а вести должно непредсказуемо.
            float t = Time.time;
            float sway = maxSway * Level;
            float x = (Mathf.Sin(t * 0.9f + _phase) + Mathf.Sin(t * 1.7f)) * 0.5f;
            float y = (Mathf.Sin(t * 1.1f + _phase * 2f) + Mathf.Sin(t * 0.6f)) * 0.5f;
            float z = Mathf.Sin(t * 0.7f + _phase) * 1.4f;

            _cam.localRotation = Quaternion.Euler(x * sway * 0.6f, y * sway, z * sway);
        }
    }
}

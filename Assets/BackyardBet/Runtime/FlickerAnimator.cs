using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Пламя: дрожит в размере и подсвечивает окрестности неровным светом.
    /// Костёр, факелы, горящая бочка.
    /// </summary>
    public class FlickerAnimator : MonoBehaviour
    {
        public float amount = 0.22f;
        public float speed = 7f;

        Vector3 _baseScale;
        Light _light;
        float _baseIntensity;
        float _seed;

        void Awake()
        {
            _baseScale = transform.localScale;
            _seed = Random.value * 100f;      // у каждого огня своя фаза

            _light = GetComponentInChildren<Light>();
            if (_light != null) _baseIntensity = _light.intensity;
        }

        void Update()
        {
            float t = Time.time * speed + _seed;
            // два несовпадающих синуса дают неровное живое мерцание,
            // одиночная волна читалась бы как ритмичное дыхание
            float n = Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.37f) * 0.4f;

            transform.localScale = _baseScale * (1f + n * amount);
            if (_light != null) _light.intensity = _baseIntensity * (1f + n * amount * 1.4f);
        }
    }
}

using UnityEngine;

namespace BackyardBet
{
    /// <summary>Пульсация в такт: магнитофон во дворе.</summary>
    public class PulseAnimator : MonoBehaviour
    {
        public float amount = 0.05f;
        public float beatsPerMinute = 120f;

        Vector3 _baseScale;

        void Awake() => _baseScale = transform.localScale;

        void Update()
        {
            float beat = Time.time * (beatsPerMinute / 60f);
            // резкий удар с затуханием, а не плавная синусоида: так читается
            // именно бит, а не дыхание
            float pulse = Mathf.Pow(1f - (beat - Mathf.Floor(beat)), 3f);
            transform.localScale = _baseScale * (1f + pulse * amount);
        }
    }
}

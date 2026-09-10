using UnityEngine;

namespace BackyardBet
{
    /// <summary>Рябь на воде: лёгкое покачивание поверхности бассейна.</summary>
    public class WaterAnimator : MonoBehaviour
    {
        public float height = 0.03f;
        public float speed = 0.8f;

        Vector3 _basePos;

        void Awake() => _basePos = transform.position;

        void Update()
        {
            float t = Time.time * speed;
            var p = _basePos;
            p.y += (Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 1.7f) * 0.4f) * height;
            transform.position = p;
        }
    }
}

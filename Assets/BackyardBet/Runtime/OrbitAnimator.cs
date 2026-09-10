using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Кружение по орбите: плавники пираний в бассейне.
    /// Центр берём у родителя, радиус - из стартовой позиции.
    /// </summary>
    public class OrbitAnimator : MonoBehaviour
    {
        public float degreesPerSecond = 26f;
        public float bobHeight = 0.05f;
        public float bobSpeed = 2.2f;

        Vector3 _center;
        float _radius;
        float _angle;
        float _baseY;
        float _seed;

        void Awake()
        {
            // центр орбиты - родитель, если он есть; иначе крутимся вокруг себя
            _center = transform.parent != null ? transform.parent.position : transform.position;
            _baseY = transform.position.y;
            _seed = Random.value * 10f;

            Vector3 flat = transform.position - _center;
            flat.y = 0f;
            _radius = flat.magnitude;
            _angle = Mathf.Atan2(flat.z, flat.x);
        }

        void Update()
        {
            if (_radius < 0.05f) return;      // не на орбите - двигать нечего

            _angle += degreesPerSecond * Mathf.Deg2Rad * Time.deltaTime;
            float y = _baseY + Mathf.Sin(Time.time * bobSpeed + _seed) * bobHeight;

            transform.position = new Vector3(_center.x + Mathf.Cos(_angle) * _radius, y,
                                             _center.z + Mathf.Sin(_angle) * _radius);

            // разворачиваем по касательной - плавник должен резать воду, а не ехать боком
            var dir = new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}

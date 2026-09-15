using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Качание на подвесе: гамак, качели из покрышки, лампочки гирлянды,
    /// флажки по забору.
    /// </summary>
    public class SwayAnimator : MonoBehaviour
    {
        public float degrees = 4f;
        public float speed = 1.1f;
        public Vector3 axis = Vector3.forward;

        [Tooltip("Качать вокруг мировой оси, а не собственной.")]
        public bool worldAxis;

        Quaternion _base;
        Quaternion _baseWorld;
        float _seed;

        void Awake()
        {
            _base = transform.localRotation;
            _baseWorld = transform.rotation;
            // фазы разные, иначе весь двор качается как один предмет
            _seed = Random.value * 10f;
        }

        void Update()
        {
            float a = Mathf.Sin(Time.time * speed + _seed) * degrees;

            // Ветер дует в одну сторону для всего двора, поэтому кроны
            // качаются вокруг мировой оси. Собственные оси у них разные:
            // кусты при расстановке развёрнуты вокруг вертикали случайно, и
            // наклон вокруг своей оси у каждого ушёл бы в свою сторону.
            if (worldAxis)
                transform.rotation = Quaternion.AngleAxis(a, axis) * _baseWorld;
            else
                transform.localRotation = _base * Quaternion.AngleAxis(a, axis);
        }
    }
}

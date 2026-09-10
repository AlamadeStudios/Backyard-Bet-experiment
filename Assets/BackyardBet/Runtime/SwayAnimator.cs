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

        Quaternion _base;
        float _seed;

        void Awake()
        {
            _base = transform.localRotation;
            // фазы разные, иначе весь двор качается как один предмет
            _seed = Random.value * 10f;
        }

        void Update()
        {
            float a = Mathf.Sin(Time.time * speed + _seed) * degrees;
            transform.localRotation = _base * Quaternion.AngleAxis(a, axis);
        }
    }
}

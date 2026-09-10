using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Постоянное вращение: мишень для топоров, вентилятор.
    ///
    /// Фоновые анимации двора сделаны процедурно, а не клипами из FBX:
    /// в исходнике клипы собраны под кинематографичный ролик на 1090 кадров
    /// с монтажом камер, и резать их на игровые циклы дороже, чем задать
    /// движение формулой. Всё это косметика - крутится локально у каждого
    /// игрока и по сети не гоняется.
    /// </summary>
    public class SpinAnimator : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 40f;

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.World);
    }
}

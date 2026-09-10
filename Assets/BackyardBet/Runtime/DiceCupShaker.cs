using System.Collections;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Стакан с костями на столе: трясётся в начале раунда и приподнимается
    /// на вскрытии.
    ///
    /// Раньше партия шла только в интерфейсе - цифры менялись, а стол стоял
    /// мёртвый. Это чистая косметика, играет локально у каждого: команду
    /// подаёт BluffTable, когда меняется номер раунда или наступает вскрытие.
    /// </summary>
    public class DiceCupShaker : MonoBehaviour
    {
        [Tooltip("Насколько сильно трясти, м.")]
        public float shakeAmount = 0.05f;

        [Tooltip("Сколько трясти, с.")]
        public float shakeTime = 1.1f;

        [Tooltip("На сколько приподнять при вскрытии, м.")]
        public float liftHeight = 0.45f;

        Vector3 _home;
        Coroutine _running;

        void Awake() => _home = transform.position;

        /// <summary>Начало раунда: кости брошены.</summary>
        public void PlayShake() => Restart(Shake());

        /// <summary>Вскрытие: стакан уходит вверх, показывая кости.</summary>
        public void PlayReveal() => Restart(Reveal());

        void Restart(IEnumerator routine)
        {
            if (_running != null) StopCoroutine(_running);
            transform.position = _home;
            _running = StartCoroutine(routine);
        }

        IEnumerator Shake()
        {
            float t = 0f;
            while (t < shakeTime)
            {
                t += Time.deltaTime;
                // затухающая тряска: к концу броска стакан успокаивается
                float fade = 1f - t / shakeTime;
                transform.position = _home + new Vector3(
                    Mathf.Sin(t * 47f) * shakeAmount * fade,
                    Mathf.Abs(Mathf.Sin(t * 23f)) * shakeAmount * fade,
                    Mathf.Cos(t * 39f) * shakeAmount * fade);
                yield return null;
            }
            transform.position = _home;
            _running = null;
        }

        IEnumerator Reveal()
        {
            float t = 0f;
            const float rise = 0.35f;
            while (t < rise)
            {
                t += Time.deltaTime;
                transform.position = _home + Vector3.up * (liftHeight * (t / rise));
                yield return null;
            }
            // держим поднятым, пока идёт показ вскрытия
            yield return new WaitForSeconds(2.5f);

            t = 0f;
            while (t < rise)
            {
                t += Time.deltaTime;
                transform.position = _home + Vector3.up * (liftHeight * (1f - t / rise));
                yield return null;
            }
            transform.position = _home;
            _running = null;
        }
    }
}

using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Сбиваемая цель - банка на стеллаже.
    ///
    /// Факт попадания определяем не по касанию, а по результату: банка
    /// считается сбитой, когда реально уехала с места или завалилась набок.
    /// Так засчитывается и прямое попадание, и когда одна банка сшибла
    /// соседнюю - то есть ровно то, что игрок и считает попаданием.
    ///
    /// Считает только хост: у клиентов физика выключена и позиции приходят
    /// по сети, там банка "сбита" по определению.
    /// </summary>
    [RequireComponent(typeof(PickupInteractable))]
    public class KnockdownTarget : MonoBehaviour
    {
        [Tooltip("На сколько метров должна уехать, чтобы считаться сбитой.")]
        public float moveThreshold = 0.12f;

        [Tooltip("На сколько градусов должна завалиться.")]
        public float tiltThreshold = 40f;

        [Tooltip("Очки за сбитую цель.")]
        public int score = 2;

        [Tooltip("Через сколько секунд вернуть на место для следующего броска.")]
        public float resetAfter = 4f;

        Vector3 _homePos;
        Quaternion _homeRot;
        PickupInteractable _prop;
        bool _down;

        void Awake()
        {
            _prop = GetComponent<PickupInteractable>();
            _homePos = transform.position;
            _homeRot = transform.rotation;
        }

        void FixedUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (_down || _prop.Held) return;           // в руках - не считаем

            bool moved = (transform.position - _homePos).sqrMagnitude >
                         moveThreshold * moveThreshold;
            bool tilted = Quaternion.Angle(transform.rotation, _homeRot) > tiltThreshold;
            if (!moved && !tilted) return;

            _down = true;

            // очки идут тому, кто бросил снаряд последним; если банку уронили
            // рукой или соседней банкой, автор броска всё равно известен
            if (MatchScore.Instance != null)
                MatchScore.Instance.Add(_prop.LastThrower, score, "Банка сбита!  +" + score);

            StartCoroutine(ResetLater());
        }

        IEnumerator ResetLater()
        {
            yield return new WaitForSeconds(resetAfter);

            if (PropNetwork.Instance != null)
                PropNetwork.Instance.ServerReturnHome(_prop.propIndex);

            // ждём кадр, чтобы позиция успела примениться, иначе тут же
            // снова засчитаем сбитие по старым координатам
            yield return new WaitForFixedUpdate();
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _down = false;
        }
    }
}

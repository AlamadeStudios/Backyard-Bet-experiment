using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Стакан для бир-понга.
    ///
    /// Попадание засчитываем, только если шарик оказался внутри объёма
    /// стакана и ниже его края - касание борта не считается. Иначе очки
    /// сыпались бы за любой чирк по пирамиде.
    ///
    /// Проверяет хост: у клиентов физика реквизита выключена.
    /// </summary>
    public class CupTarget : MonoBehaviour
    {
        [Tooltip("Очки за попадание.")]
        public int score = 3;

        [Tooltip("Через сколько секунд вернуть шарик для следующего броска.")]
        public float returnAfter = 2f;

        [Tooltip("Насколько ниже края должен опуститься шарик, м.")]
        public float depth = 0.03f;

        Bounds _mouth;
        bool _scored;

        void Awake()
        {
            var rend = GetComponentInChildren<Renderer>();
            _mouth = rend != null ? rend.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
        }

        void FixedUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || _scored) return;
            if (PropNetwork.Instance == null) return;

            var ball = FindBallInside();
            if (ball == null) return;

            _scored = true;
            if (MatchScore.Instance != null)
                MatchScore.Instance.Add(ball.LastThrower, score, "Попал в стакан!  +" + score);

            // стакан выбывает из игры - как в настоящем понге
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null) rend.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            StartCoroutine(ReturnBall(ball));
        }

        /// <summary>Шарик, который сейчас внутри стакана и ниже края.</summary>
        PickupInteractable FindBallInside()
        {
            for (int i = 0; i < PropNetwork.Instance.Count; i++)
            {
                var p = PropNetwork.Instance.Prop(i);
                if (p == null || p.Held || !p.WasThrown) continue;
                if (!p.name.StartsWith("PongBall")) continue;

                Vector3 pos = p.transform.position;
                if (pos.y > _mouth.max.y - depth) continue;          // ещё не опустился
                if (pos.y < _mouth.min.y) continue;                  // уже провалился насквозь

                // по горизонтали - внутри стакана
                if (Mathf.Abs(pos.x - _mouth.center.x) > _mouth.extents.x) continue;
                if (Mathf.Abs(pos.z - _mouth.center.z) > _mouth.extents.z) continue;

                return p;
            }
            return null;
        }

        IEnumerator ReturnBall(PickupInteractable ball)
        {
            yield return new WaitForSeconds(returnAfter);
            if (PropNetwork.Instance != null)
                PropNetwork.Instance.ServerReturnHome(ball.propIndex);
        }
    }
}

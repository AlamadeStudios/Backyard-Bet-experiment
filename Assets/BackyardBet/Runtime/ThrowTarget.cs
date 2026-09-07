using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Мишень для метательных мини-игр: топоры, дротики.
    ///
    /// Обычный MonoBehaviour: попадания считает только хост (проверяем через
    /// NetworkManager), очки идут в общий MatchScore. Вешать сюда сетевой
    /// объект незачем - сама мишень не двигается.
    ///
    /// Мишень плоская, поэтому расстояния в 3D достаточно: толщина щита в
    /// счёт почти не идёт, и не нужно гадать, как именно она повёрнута.
    /// </summary>
    public class ThrowTarget : MonoBehaviour
    {
        [Tooltip("Имя снаряда, который эта мишень принимает (Axe, Dart).")]
        public string accepts = "Axe";

        [Header("Очки")]
        public int centerScore = 5;
        public int midScore = 3;
        public int edgeScore = 1;

        [Tooltip("Через сколько секунд снаряд вернётся на исходное место.")]
        public float returnAfter = 2.5f;

        Vector3 _center;
        float _radius;

        void Awake()
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                _center = rend.bounds.center;
                var s = rend.bounds.size;
                _radius = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f;
            }
            else
            {
                _center = transform.position;
                _radius = 1f;
            }
        }

        void OnCollisionEnter(Collision c)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            var item = c.gameObject.GetComponent<PickupInteractable>();
            if (item == null || !item.WasThrown) return;
            if (!c.gameObject.name.StartsWith(accepts)) return;

            Vector3 hit = c.GetContact(0).point;
            float d = _radius > 0.01f ? Vector3.Distance(hit, _center) / _radius : 1f;

            int points = d < 0.25f ? centerScore : (d < 0.55f ? midScore : edgeScore);
            string where = d < 0.25f ? "В ЯБЛОЧКО!" : (d < 0.55f ? "Хорошее попадание" : "По краю");

            if (MatchScore.Instance != null)
                MatchScore.Instance.Add(item.LastThrower, points, where + "  +" + points);

            StartCoroutine(StickAndReturn(item));
        }

        IEnumerator StickAndReturn(PickupInteractable item)
        {
            var rb = item.Body;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;              // воткнулся и торчит
            }

            yield return new WaitForSeconds(returnAfter);
            if (PropNetwork.Instance != null)
                PropNetwork.Instance.ServerReturnHome(item.propIndex);
        }
    }
}

using System.Collections;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Одна карта в мире: текстурированная плоскость, которую можно плавно
    /// перевести из руки на стол.
    ///
    /// Карты создаются кодом, а не префабом: их размер и вид полностью
    /// определяются колодой, и заводить под каждую заготовку в проекте
    /// незачем.
    /// </summary>
    public class CardVisual : MonoBehaviour
    {
        public const float Width = 0.088f;
        public const float Height = 0.126f;

        Renderer _rend;
        Coroutine _move;

        /// <summary>Индекс карты в руке; у карт на столе -1.</summary>
        public int HandIndex { get; set; } = -1;

        public static CardVisual Create(Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(Width, Height, 1f);

            // коллайдер нужен только чтобы ловить щелчок мышью: как триггер
            // он не мешает физике реквизита и не ловит чужие лучи
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var cv = go.AddComponent<CardVisual>();
            cv._rend = go.GetComponent<Renderer>();
            // Unlit: карту надо читать в любое время суток, а двор вечерний
            cv._rend.material = new Material(Shader.Find("Unlit/Texture"));
            return cv;
        }

        public void SetTexture(Texture2D tex)
        {
            if (_rend != null && tex != null) _rend.material.mainTexture = tex;
        }

        public void SetLocal(Vector3 pos, Quaternion rot)
        {
            transform.localPosition = pos;
            transform.localRotation = rot;
        }

        /// <summary>Плавно уехать в мировую точку - бросок карты на стол.</summary>
        public void FlyTo(Vector3 worldPos, Quaternion worldRot, float time, float arc = 0.18f)
        {
            if (_move != null) StopCoroutine(_move);
            _move = StartCoroutine(Fly(worldPos, worldRot, time, arc));
        }

        IEnumerator Fly(Vector3 to, Quaternion toRot, float time, float arc)
        {
            Vector3 from = transform.position;
            Quaternion fromRot = transform.rotation;

            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                // подброс по дуге: карта должна лететь, а не проскальзывать
                Vector3 p = Vector3.Lerp(from, to, eased);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;
                transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, eased));
                yield return null;
            }
            transform.SetPositionAndRotation(to, toRot);
            _move = null;
        }
    }
}

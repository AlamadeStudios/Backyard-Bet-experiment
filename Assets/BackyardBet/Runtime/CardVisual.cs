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

            // У примитива Quad коллайдер сетчатый и плоский, а плоскую сетку
            // нельзя сделать триггером - Unity ругается каждый кадр и
            // оставляет карту твёрдой. Меняем на коробку: она ловит щелчок
            // мышью и при этом не мешает физике реквизита.
            var mesh = go.GetComponent<Collider>();
            if (mesh != null) Destroy(mesh);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.02f);
            box.isTrigger = true;

            var cv = go.AddComponent<CardVisual>();
            cv._rend = go.GetComponent<Renderer>();
            cv._rend.material = new Material(CardShader);
            cv._rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cv._rend.receiveShadows = false;
            return cv;
        }

        static Shader _shader;

        /// <summary>
        /// Шейдер карты. Sprites/Default не гасит свет и рисует обе стороны
        /// плоскости: карту надо читать в любое время суток - двор у нас
        /// вечерний, - и она не должна пропадать, если повернулась изнанкой.
        /// </summary>
        static Shader CardShader
        {
            get
            {
                if (_shader == null) _shader = Shader.Find("Sprites/Default");
                if (_shader == null) _shader = Shader.Find("Unlit/Texture");
                return _shader;
            }
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

        /// <summary>
        /// Размер карты в мире. Рука висит у самого лица, поэтому её размер
        /// задаётся не в метрах, а от того, сколько экрана карта должна
        /// занимать - иначе при смене обзора рука уезжает за край кадра.
        /// </summary>
        public void SetSize(float k)
        {
            transform.localScale = new Vector3(Width * k, Height * k, 1f);
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

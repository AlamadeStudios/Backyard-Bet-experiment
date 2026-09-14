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
    ///
    /// Карта никуда не прыгает: и рука, и бросок на стол идут через
    /// сглаживание. Мгновенная перестановка читается как подмена картинки,
    /// а карта должна вести себя как предмет в руках.
    /// </summary>
    public class CardVisual : MonoBehaviour
    {
        // Соотношение взято с самой колоды (357x537), а не с настоящей
        // карты: иначе картинку растянет по ширине и лица поедут.
        public const float Height = 0.126f;
        public const float Width = Height * 357f / 537f;

        Renderer _rend;
        Coroutine _move;
        Transform _frame;

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

        /// <summary>
        /// Золотая рамка вокруг карты.
        ///
        /// Джокера в колоде из 52 карт нет, под него взят валет - и в руке
        /// среди дам, королей и тузов он читается как чужая карта или сбой
        /// текстуры. Рамка сразу говорит: карта особая, она за любой ранг.
        /// </summary>
        public void SetJoker(bool on)
        {
            if (on == (_frame != null)) return;

            if (!on)
            {
                Destroy(_frame.gameObject);
                _frame = null;
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "JokerFrame";
            // коллайдер рамки перехватывал бы щелчок вместо самой карты
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(1.16f, 1.11f, 1f);
            go.transform.localPosition = new Vector3(0f, 0f, 0.0015f);   // за картой

            var r = go.GetComponent<Renderer>();
            r.material = new Material(CardShader) { color = new Color(0.98f, 0.76f, 0.16f) };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _frame = go.transform;
        }

        static Vector3 Size(float k) => new Vector3(Width * k, Height * k, 1f);

        /// <summary>Поставить сразу - для первой выкладки карты.</summary>
        public void SetLocal(Vector3 pos, Quaternion rot, float k)
        {
            transform.localPosition = pos;
            transform.localRotation = rot;
            transform.localScale = Size(k);
        }

        /// <summary>
        /// Плавно подтянуться к месту в руке. Сглаживание экспоненциальное,
        /// поэтому скорость не зависит от частоты кадров: на слабой машине
        /// карта доедет за то же время, что и на быстрой.
        /// </summary>
        public void MoveLocal(Vector3 pos, Quaternion rot, float k, float speed)
        {
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, pos, t);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, rot, t);
            transform.localScale = Vector3.Lerp(transform.localScale, Size(k), t);
        }

        /// <summary>Размер карты на столе - он не меняется от кадра к кадру.</summary>
        public void SetSize(float k) => transform.localScale = Size(k);

        /// <summary>Плавно уехать в мировую точку - бросок карты на стол.</summary>
        public void FlyTo(Vector3 worldPos, Quaternion worldRot, float time, float arc = 0.25f)
        {
            if (!isActiveAndEnabled) return;
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
                // плавный вход и выход: карта трогается и ложится мягко,
                // а не дёргается с места и не втыкается в стол
                float eased = k * k * k * (k * (6f * k - 15f) + 10f);

                Vector3 p = Vector3.Lerp(from, to, eased);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;      // подброс по дуге
                transform.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, eased));
                yield return null;
            }
            transform.SetPositionAndRotation(to, toRot);
            _move = null;
        }
    }
}

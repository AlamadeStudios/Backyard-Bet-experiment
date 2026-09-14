using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Одна карта в мире: скруглённая плоскость с картинкой, которую можно
    /// плавно перевести из руки на стол.
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

        /// <summary>Радиус скругления углов в долях ширины карты.</summary>
        const float CornerRadius = 20f / 357f;

        Renderer _rend;
        Coroutine _move;
        Transform _frame;

        /// <summary>Индекс карты в руке; у карт на столе -1.</summary>
        public int HandIndex { get; set; } = -1;

        public static CardVisual Create(Transform parent, string name)
        {
            var go = NewSurface(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(Width, Height, 1f);

            // коробка-триггер ловит щелчок мышью и не мешает физике реквизита
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.02f);
            box.isTrigger = true;

            var cv = go.AddComponent<CardVisual>();
            cv._rend = go.GetComponent<Renderer>();
            return cv;
        }

        /// <summary>Скруглённая плашка с материалом карты, без коллайдера.</summary>
        static GameObject NewSurface(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = CardMesh;

            var r = go.AddComponent<MeshRenderer>();
            r.material = new Material(CardShader);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ------------------------------------------------------------ сетка

        static Mesh _mesh;

        /// <summary>
        /// Скруглённый прямоугольник вместо примитива Quad.
        ///
        /// На картинках колоды скругление уже нарисовано, а сама плашка была
        /// прямоугольной - углы торчали белыми квадратами и карта выглядела
        /// обрубленной. Режем геометрию по тому же радиусу, что и на
        /// картинке, тогда силуэт совпадает с рисунком.
        ///
        /// Сетка одна на все карты: она не зависит от размера, потому что
        /// строится в долях карты и растягивается масштабом объекта.
        /// </summary>
        static Mesh CardMesh
        {
            get
            {
                if (_mesh != null) return _mesh;

                const int seg = 6;                      // отрезков на угол
                float rx = CornerRadius;
                // по высоте радиус меньше: плашку потом растянут неравномерно,
                // и только так дуга останется круглой в мире
                float ry = rx * Width / Height;

                var centers = new[]
                {
                    new Vector2( 0.5f - rx,  0.5f - ry),   // правый верхний
                    new Vector2(-0.5f + rx,  0.5f - ry),   // левый верхний
                    new Vector2(-0.5f + rx, -0.5f + ry),   // левый нижний
                    new Vector2( 0.5f - rx, -0.5f + ry),   // правый нижний
                };

                var rim = new List<Vector2>((seg + 1) * 4);
                for (int c = 0; c < 4; c++)
                {
                    float from = c * 90f;
                    for (int s = 0; s <= seg; s++)
                    {
                        float a = (from + 90f * s / seg) * Mathf.Deg2Rad;
                        rim.Add(centers[c] + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
                    }
                }

                int n = rim.Count;
                var verts = new Vector3[n + 1];
                var uvs = new Vector2[n + 1];
                var norms = new Vector3[n + 1];

                verts[0] = Vector3.zero;                  // центр веера треугольников
                uvs[0] = new Vector2(0.5f, 0.5f);
                norms[0] = Vector3.back;
                for (int i = 0; i < n; i++)
                {
                    verts[i + 1] = new Vector3(rim[i].x, rim[i].y, 0f);
                    uvs[i + 1] = new Vector2(rim[i].x + 0.5f, rim[i].y + 0.5f);
                    norms[i + 1] = Vector3.back;
                }

                var tris = new int[n * 3];
                for (int i = 0; i < n; i++)
                {
                    // обход по часовой, лицом на -Z - как у примитива Quad:
                    // на запасном шейдере изнанка отсекается, и при другом
                    // порядке карта просто пропала бы
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = (i + 1) % n + 1;
                    tris[i * 3 + 2] = i + 1;
                }

                _mesh = new Mesh { name = "Card", hideFlags = HideFlags.DontSave };
                _mesh.vertices = verts;
                _mesh.uv = uvs;
                _mesh.normals = norms;
                _mesh.triangles = tris;
                _mesh.RecalculateBounds();
                return _mesh;
            }
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

        // ------------------------------------------------------------ вид

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

            var go = NewSurface("JokerFrame");             // без коллайдера:
            go.transform.SetParent(transform, false);      // щелчок ловит карта
            go.transform.localScale = new Vector3(1.16f, 1.11f, 1f);
            go.transform.localPosition = new Vector3(0f, 0f, 0.0015f);   // за картой
            go.GetComponent<Renderer>().material.color = new Color(0.98f, 0.76f, 0.16f);
            _frame = go.transform;
        }

        // ------------------------------------------------------------ движение

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

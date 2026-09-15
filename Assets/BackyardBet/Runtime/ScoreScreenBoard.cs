using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Табло во дворе: экран показывает накопленные очки каждого участника.
    ///
    /// Раньше счёт «показывали» кубики-плашки на доске - нарисованные раз и
    /// навсегда, они ничего не значили. Здесь на экран кладётся холст в
    /// мировых координатах и на нём строками печатается живой зачёт из
    /// MatchScore, так что двор видит счёт, не открывая интерфейс.
    ///
    /// Куда смотрит экран, берём по метке ScoreFront из модели, а не по осям
    /// объекта: на угадывании осей мы уже обожглись с колесом фортуны.
    /// </summary>
    public class ScoreScreenBoard : MonoBehaviour
    {
        [Tooltip("Отступ холста от плоскости экрана, м.")]
        public float lift = 0.02f;

        Text _title;
        Text _names;
        Text _points;
        string _shown = "";

        void Start()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null)
            {
                Debug.LogWarning("[Backyard Bet] У экрана табло нет меша - счёт рисовать не на чем.");
                enabled = false;
                return;
            }

            Vector3 front = FrontDirection(rend.bounds.center);
            Measure(out float w, out float h);

            Build(rend.bounds.center + front * (Thickness() * 0.5f + lift), front, w, h);
        }

        /// <summary>
        /// Куда смотрит экран. Метка ScoreFront стоит в метре перед ним -
        /// направление меряется по ней, а не выводится из осей.
        /// </summary>
        Vector3 FrontDirection(Vector3 center)
        {
            var mark = GameObject.Find("ScoreFront");
            Vector3 dir = mark != null ? mark.transform.position - center : transform.forward;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        /// <summary>Толщина плашки экрана - самая короткая её сторона.</summary>
        float Thickness()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return 0.03f;
            Vector3 s = Vector3.Scale(mf.sharedMesh.bounds.size, transform.lossyScale);
            return Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        }

        /// <summary>
        /// Ширина и высота экрана.
        ///
        /// Меряем по самой плашке: из трёх её сторон самая короткая - толщина,
        /// из оставшихся высота та, что смотрит вверх. Габариты по осям мира
        /// тут не годятся - плашка повёрнута, и они бы врали.
        /// </summary>
        void Measure(out float width, out float height)
        {
            width = 2.9f; height = 1.6f;

            var mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, transform.lossyScale);
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var len = new[] { size.x, size.y, size.z };

            int thin = 0;
            for (int i = 1; i < 3; i++) if (len[i] < len[thin]) thin = i;

            int a = -1, b = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i == thin) continue;
                if (a < 0) a = i; else b = i;
            }

            float upA = Mathf.Abs(Vector3.Dot(transform.TransformDirection(axes[a]).normalized, Vector3.up));
            float upB = Mathf.Abs(Vector3.Dot(transform.TransformDirection(axes[b]).normalized, Vector3.up));

            int vertical = upA > upB ? a : b;
            int horizontal = vertical == a ? b : a;

            width = len[horizontal];
            height = len[vertical];
        }

        void Build(Vector3 at, Vector3 front, float width, float height)
        {
            var go = new GameObject("ScoreCanvas");
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(front, Vector3.up));
            go.transform.SetParent(transform, true);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // холст считаем в пикселях и ужимаем в метры: так кегль шрифта
            // остаётся привычным числом, а не долей метра
            const float px = 400f;                     // пикселей на метр
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width * px, height * px);
            go.transform.localScale = Vector3.one / px;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            float pad = height * px * 0.08f;
            float titleH = height * px * 0.22f;

            _title = MakeText(rt, font, TextAnchor.MiddleCenter,
                              Mathf.RoundToInt(titleH * 0.72f),
                              new Color(1f, 0.84f, 0.32f));
            Stretch(_title.rectTransform, pad, pad, pad, height * px - titleH - pad);

            _names = MakeText(rt, font, TextAnchor.UpperLeft,
                              Mathf.RoundToInt(titleH * 0.62f), Color.white);
            Stretch(_names.rectTransform, pad, width * px * 0.42f, titleH + pad, pad);

            _points = MakeText(rt, font, TextAnchor.UpperRight,
                               Mathf.RoundToInt(titleH * 0.62f),
                               new Color(0.45f, 0.95f, 1f));
            Stretch(_points.rectTransform, width * px * 0.42f, pad, titleH + pad, pad);

            _title.text = "СЧЁТ";
            Refresh();
        }

        static Text MakeText(RectTransform parent, Font font, TextAnchor align,
                             int size, Color color)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);

            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1.15f;
            return t;
        }

        /// <summary>Растянуть по всему холсту с отступами от краёв.</summary>
        static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        void Update()
        {
            if (_names != null) Refresh();
        }

        void Refresh()
        {
            var score = MatchScore.Instance;
            if (score == null || score.RowCount == 0)
            {
                Set("ЖДЁМ ИГРОКОВ", "");
                return;
            }

            ulong me = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

            var names = new StringBuilder();
            var points = new StringBuilder();
            for (int i = 0; i < score.RowCount; i++)
            {
                var row = score.Row(i);
                if (i > 0) { names.Append('\n'); points.Append('\n'); }
                names.Append(row.clientId == me ? "Ты" : "Игрок " + row.clientId);
                points.Append(row.score);
            }
            Set(names.ToString(), points.ToString());
        }

        void Set(string names, string points)
        {
            // сравниваем перед записью: иначе UI пересобирает сетку каждый
            // кадр на ровном месте
            string now = names + "" + points;
            if (now == _shown) return;
            _shown = now;
            _names.text = names;
            _points.text = points;
        }
    }
}

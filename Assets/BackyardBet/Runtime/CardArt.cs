using System.Collections.Generic;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Отрисовка игральной карты настоящими картинками из колоды.
    ///
    /// Раньше карта была прямоугольником с подписью "Дама" - на столе это
    /// читалось как список, а не как рука.
    ///
    /// Джокера в обычной колоде из 52 карт нет, поэтому под него взят валет
    /// и всегда обводится золотой рамкой: игрок сразу видит, что карта
    /// особая, и не путает её с дамой или королём.
    ///
    /// Масть на правила не влияет - она нужна только чтобы рука не выглядела
    /// пятью одинаковыми картами. Выбирается по позиции в руке, поэтому не
    /// скачет от кадра к кадру.
    /// </summary>
    public static class CardArt
    {
        static readonly string[] Suits = { "Hearts", "Spades", "Diamonds", "Clubs" };
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        static Texture2D _flat;

        static Texture2D Load(string name)
        {
            if (_cache.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("Cards/" + name);
            if (t == null) Debug.LogWarning("[Backyard Bet] Нет картинки карты: Cards/" + name);
            _cache[name] = t;
            return t;
        }

        static Texture2D Flat
        {
            get
            {
                if (_flat == null)
                {
                    _flat = new Texture2D(1, 1);
                    _flat.SetPixel(0, 0, Color.white);
                    _flat.Apply();
                }
                return _flat;
            }
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Flat);
            GUI.color = old;
        }

        static string FaceName(CardRank rank, int seed)
        {
            if (rank == CardRank.Joker) return "Joker";
            string suit = Suits[Mathf.Abs(seed) % Suits.Length];
            switch (rank)
            {
                case CardRank.Queen: return "Queen_" + suit;
                case CardRank.King:  return "King_" + suit;
                default:             return "Ace_" + suit;
            }
        }

        /// <summary>Лицевая сторона. seed задаёт масть - она ни на что не влияет.</summary>
        public static void DrawFace(Rect r, CardRank rank, bool highlighted, int seed = 0)
        {
            Fill(new Rect(r.x + 3f, r.y + 4f, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));

            var gold = new Color(0.95f, 0.72f, 0.18f);
            bool frame = highlighted || rank == CardRank.Joker;
            Fill(r, frame ? gold : new Color(0.12f, 0.12f, 0.14f));

            var inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            var tex = Load(FaceName(rank, seed));
            if (tex != null) GUI.DrawTexture(inner, tex, ScaleMode.StretchToFill);
            else Fill(inner, Color.white);

            if (rank == CardRank.Joker)
            {
                // подпись поверх валета: карта работает как джокер
                var s = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(r.height * 0.13f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.LowerCenter
                };
                Fill(new Rect(r.x + 2f, r.yMax - r.height * 0.19f, r.width - 4f, r.height * 0.17f),
                     new Color(0f, 0f, 0f, 0.72f));
                s.normal.textColor = gold;
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4f), "ДЖОКЕР", s);
            }
        }

        /// <summary>Рубашка - для стопки на столе и чужих карт.</summary>
        public static void DrawBack(Rect r)
        {
            Fill(new Rect(r.x + 3f, r.y + 4f, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
            Fill(r, new Color(0.12f, 0.12f, 0.14f));

            var inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            var tex = Load("Back");
            if (tex != null) GUI.DrawTexture(inner, tex, ScaleMode.StretchToFill);
            else Fill(inner, new Color(0.42f, 0.10f, 0.13f));
        }
    }
}

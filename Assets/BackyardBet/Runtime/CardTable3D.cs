using System.Collections.Generic;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Карты в мире: веер в руках у игрока и сброс на столе.
    ///
    /// Плоский интерфейс внизу экрана читался как список; здесь карты живут
    /// как предметы - их держат в руках и кладут на сукно.
    ///
    /// Веер подвешен к камере игрока, поэтому едет вместе со взглядом. Своя
    /// рука видна только своему хозяину: карты создаются локально из тех
    /// данных, что пришли адресным RPC, и в мире их ни у кого больше нет.
    /// </summary>
    public class CardTable3D : MonoBehaviour
    {
        [Tooltip("Насколько карты вынесены вперёд от глаз, м.")]
        public float handForward = 0.42f;

        [Tooltip("Высота карты в долях высоты экрана.")]
        public float cardScreenHeight = 0.52f;

        [Tooltip("Какая часть карты выступает над нижним краем кадра.")]
        public float cardVisible = 0.82f;

        [Tooltip("Разлёт веера, градусов на карту.")]
        public float fanStep = 8f;

        [Tooltip("На сколько выдвигается выбранная карта, в высотах карты.")]
        public float pickLift = 0.55f;

        [Tooltip("На сколько выдвигается карта под курсором, в высотах карты.")]
        public float hoverLift = 0.28f;

        [Tooltip("Скорость доводки карты к месту. Больше - резче.")]
        public float smoothSpeed = 13f;

        [Tooltip("Во сколько раз карта на столе крупнее настоящей.")]
        public float pileScale = 1.7f;

        [Tooltip("Насколько сброс сдвинут от центра стола к ходившему.")]
        [Range(0f, 0.85f)] public float pileToSeat = 0.52f;

        /// <summary>Карта под курсором. -1, если ни одной.</summary>
        public int Hovered { get; private set; } = -1;

        readonly List<CardVisual> _hand = new List<CardVisual>();
        readonly List<CardVisual> _pile = new List<CardVisual>();

        Transform _handRoot;
        Transform _table;
        int _pileSeat = -1;

        Transform TableTop
        {
            get
            {
                if (_table == null)
                {
                    var go = GameObject.Find("TableTop");
                    if (go != null) _table = go.transform;
                }
                return _table;
            }
        }

        // ------------------------------------------------------------ рука

        /// <summary>
        /// Разложить свою руку веером перед камерой.
        ///
        /// Раскладка считается от поля зрения камеры, а не в метрах: сидя за
        /// столом обзор сужен до 38 градусов, и рука, отложенная на глазок,
        /// целиком уходит под нижний край экрана - карт просто не видно.
        /// </summary>
        public void ShowHand(int[] hand, Camera camera, ICollection<int> picked, int seedBase)
        {
            if (camera == null || hand == null) { ClearHand(); return; }
            var eye = camera.transform;

            if (_handRoot == null || _handRoot.parent != eye)
            {
                if (_handRoot != null) Destroy(_handRoot.gameObject);
                _handRoot = new GameObject("HandCards").transform;
                _handRoot.SetParent(eye, false);
                _hand.Clear();
            }

            // дальше ближней плоскости отсечения, иначе карту срежет камера
            float dist = Mathf.Max(handForward, camera.nearClipPlane * 1.4f);

            // половина видимого кадра на этом расстоянии - вся раскладка
            // меряется от неё, поэтому при любом обзоре рука лежит одинаково
            float halfH = dist * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * camera.aspect;
            float cardH = halfH * 2f * cardScreenHeight;
            float k = cardH / CardVisual.Height;

            // веер гнётся вокруг точки под кадром - так держат карты в руке
            float radius = cardH * 2.4f;
            float pivotY = -halfH + cardH * (cardVisible - 0.5f) - radius;

            while (_hand.Count > hand.Length)
            {
                var last = _hand[_hand.Count - 1];
                _hand.RemoveAt(_hand.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }

            while (_hand.Count < hand.Length)
            {
                // новая карта въезжает из-за правого края - это и есть раздача
                var fresh = CardVisual.Create(_handRoot, "HandCard");
                fresh.SetLocal(new Vector3(halfW * 1.7f, pivotY + radius - cardH * 1.2f, dist),
                               Quaternion.Euler(0f, 0f, -70f), k);
                _hand.Add(fresh);
            }

            // что под курсором, считаем до раскладки: карта поднимается уже
            // на этом кадре, иначе выделение отстаёт от мыши
            Hovered = PickUnder(camera.ScreenPointToRay(Input.mousePosition));

            for (int i = 0; i < hand.Length; i++)
            {
                var c = _hand[i];
                c.HandIndex = i;
                c.SetTexture(CardArt.FaceTexture((CardRank)hand[i], seedBase + i));

                float offset = i - (hand.Length - 1) * 0.5f;
                float angle = -offset * fanStep;
                float rad = angle * Mathf.Deg2Rad;

                float lift = 0f;
                if (picked != null && picked.Contains(i)) lift = pickLift;
                if (i == Hovered) lift = Mathf.Max(lift, hoverLift);

                var pos = new Vector3(
                    -radius * Mathf.Sin(rad),
                    pivotY + radius * Mathf.Cos(rad) + cardH * lift,
                    // мизерный сдвиг по глубине: иначе соседние карты мерцают,
                    // а поднятая должна оказаться поверх остальных
                    dist - i * 0.0012f - (lift > 0f ? 0.006f : 0f));

                c.MoveLocal(pos, Quaternion.Euler(0f, 0f, angle),
                            i == Hovered ? k * 1.07f : k, smoothSpeed);
            }
        }

        public void ClearHand()
        {
            foreach (var c in _hand) if (c != null) Destroy(c.gameObject);
            _hand.Clear();
            Hovered = -1;
            if (_handRoot != null) { Destroy(_handRoot.gameObject); _handRoot = null; }
        }

        /// <summary>Какая карта под лучом. -1, если ни одна.</summary>
        public int PickUnder(Ray ray)
        {
            float best = float.MaxValue;
            int found = -1;
            foreach (var c in _hand)
            {
                if (c == null) continue;
                var col = c.GetComponent<Collider>();
                if (col == null) continue;
                if (!col.Raycast(ray, out var hit, 5f)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                found = c.HandIndex;
            }
            return found;
        }

        // ------------------------------------------------------------ стол

        /// <summary>Чей ход лёг в сброс - перед ним стопка и окажется.</summary>
        public void SetPileSeat(int seat)
        {
            if (_pileSeat == seat) return;
            _pileSeat = seat;
            var top = TableTop;
            if (top != null && _pile.Count > 0) LayOutPile(top);
        }

        /// <summary>
        /// Выложить сброс на сукно рубашкой вверх. Карты долетают из руки,
        /// поэтому ход читается как бросок, а не как мгновенная подмена.
        /// </summary>
        public void ShowPile(int count, Camera fromCamera)
        {
            var top = TableTop;
            if (top == null) return;
            var eye = fromCamera != null ? fromCamera.transform : null;

            while (_pile.Count > count)
            {
                var last = _pile[_pile.Count - 1];
                _pile.RemoveAt(_pile.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }

            for (int i = _pile.Count; i < count; i++)
            {
                var c = CardVisual.Create(null, "PileCard");
                c.SetTexture(CardArt.BackTexture());
                // стол круглый и широкий - настоящий размер карты на другом
                // его конце читается как соринка, поэтому сброс крупнее
                c.SetSize(pileScale);
                // старт от лица игрока, если он за столом - тогда видно бросок
                if (eye != null)
                    c.transform.SetPositionAndRotation(
                        eye.position + eye.forward * 0.45f, eye.rotation);
                _pile.Add(c);
            }

            LayOutPile(top);
        }

        void LayOutPile(Transform top)
        {
            var rend = top.GetComponent<Renderer>();

            // именно границы меша, а не позиция объекта: точка привязки
            // столешницы стоит не в её центре, и стопка уезжала к дальнему краю
            Vector3 center = rend != null ? rend.bounds.center : top.position;
            float surface = rend != null ? rend.bounds.max.y + 0.012f : top.position.y;
            center.y = surface;

            // сброс ложится перед тем, кто ходил: в середине круглого стола
            // диаметром под три метра карту не разглядеть ни с одного места
            var seat = FindSeat(_pileSeat);
            if (seat != null)
            {
                center = Vector3.Lerp(center,
                    new Vector3(seat.position.x, surface, seat.position.z), pileToSeat);

                // стул стоит за краем стола, поэтому сдвиг ограничиваем
                // столешницей - иначе карты повиснут в воздухе рядом с ним
                if (rend != null)
                {
                    Vector3 pivot = rend.bounds.center;
                    float limit = Mathf.Min(rend.bounds.extents.x, rend.bounds.extents.z) * 0.55f;
                    var off = new Vector2(center.x - pivot.x, center.z - pivot.z);
                    if (off.magnitude > limit)
                    {
                        off = off.normalized * limit;
                        center = new Vector3(pivot.x + off.x, surface, pivot.z + off.y);
                    }
                }
            }

            float spread = 0.09f * pileScale;

            for (int i = 0; i < _pile.Count; i++)
            {
                // лёгкий разброс: аккуратная стопка выглядит выложенной линейкой
                float a = i * 37f;
                var pos = new Vector3(center.x + Mathf.Cos(a * Mathf.Deg2Rad) * spread,
                                      surface + i * 0.005f,
                                      center.z + Mathf.Sin(a * Mathf.Deg2Rad) * spread);
                var rot = Quaternion.Euler(90f, a * 0.6f, 0f);
                _pile[i].FlyTo(pos, rot, 0.55f);
            }
        }

        /// <summary>Вскрытие: перевернуть сброс лицом.</summary>
        public void RevealPile(int[] ranks)
        {
            for (int i = 0; i < _pile.Count && i < ranks.Length; i++)
                if (_pile[i] != null)
                    _pile[i].SetTexture(CardArt.FaceTexture((CardRank)ranks[i], i));
        }

        public void ClearPile()
        {
            foreach (var c in _pile) if (c != null) Destroy(c.gameObject);
            _pile.Clear();
        }

        /// <summary>
        /// Место за столом по индексу. Порядок тот же, что у посадки игроков,
        /// иначе сброс ляжет перед чужим стулом.
        /// </summary>
        static Transform FindSeat(int index)
        {
            if (index < 0) return null;

            var found = new List<Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Seat_")) found.Add(t);
            if (found.Count == 0) return null;

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found[index % found.Count];
        }
    }
}

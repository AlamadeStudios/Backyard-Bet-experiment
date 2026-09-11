using System.Collections.Generic;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Карты в мире: веер в руках у игрока и стопка на столе.
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

        [Tooltip("Насколько опущены ниже линии взгляда, м.")]
        public float handDrop = 0.24f;

        [Tooltip("Разлёт веера, градусов на карту.")]
        public float fanStep = 9f;

        [Tooltip("На сколько приподнимается выбранная карта, м.")]
        public float pickLift = 0.035f;

        readonly List<CardVisual> _hand = new List<CardVisual>();
        readonly List<CardVisual> _pile = new List<CardVisual>();

        Transform _handRoot;
        Transform _table;

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

        /// <summary>Разложить свою руку веером перед камерой.</summary>
        public void ShowHand(int[] hand, Transform camera, ICollection<int> picked, int seedBase)
        {
            if (camera == null) { ClearHand(); return; }

            if (_handRoot == null || _handRoot.parent != camera)
            {
                if (_handRoot != null) Destroy(_handRoot.gameObject);
                _handRoot = new GameObject("HandCards").transform;
                _handRoot.SetParent(camera, false);
                _hand.Clear();
            }

            while (_hand.Count > hand.Length)
            {
                var last = _hand[_hand.Count - 1];
                _hand.RemoveAt(_hand.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }
            while (_hand.Count < hand.Length)
                _hand.Add(CardVisual.Create(_handRoot, "HandCard"));

            for (int i = 0; i < hand.Length; i++)
            {
                var c = _hand[i];
                c.HandIndex = i;
                c.SetTexture(CardArt.FaceTexture((CardRank)hand[i], seedBase + i));

                // веер: середина руки перед лицом, края разведены по дуге
                float offset = i - (hand.Length - 1) * 0.5f;
                float angle = -offset * fanStep;
                bool up = picked != null && picked.Contains(i);

                var pos = new Vector3(offset * CardVisual.Width * 0.62f,
                                      -handDrop + (up ? pickLift : 0f)
                                      - Mathf.Abs(offset) * 0.006f,
                                      handForward);
                c.SetLocal(pos, Quaternion.Euler(0f, 0f, angle));
            }
        }

        public void ClearHand()
        {
            foreach (var c in _hand) if (c != null) Destroy(c.gameObject);
            _hand.Clear();
            if (_handRoot != null) { Destroy(_handRoot.gameObject); _handRoot = null; }
        }

        /// <summary>Какая карта под курсором. -1, если ни одна.</summary>
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

        /// <summary>
        /// Выложить стопку на сукно рубашкой вверх. Карты долетают из руки,
        /// поэтому ход читается как бросок, а не как мгновенная подмена.
        /// </summary>
        public void ShowPile(int count, Transform fromCamera)
        {
            var top = TableTop;
            if (top == null) return;

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
                // старт от лица игрока, если он за столом - тогда видно бросок
                if (fromCamera != null)
                    c.transform.SetPositionAndRotation(
                        fromCamera.position + fromCamera.forward * 0.4f, fromCamera.rotation);
                _pile.Add(c);
            }

            LayOutPile(top);
        }

        void LayOutPile(Transform top)
        {
            var rend = top.GetComponent<Renderer>();
            float surface = rend != null ? rend.bounds.max.y + 0.012f : top.position.y;

            for (int i = 0; i < _pile.Count; i++)
            {
                // лёгкий разброс: аккуратная стопка выглядит выложенной линейкой
                float a = i * 37f;
                var pos = new Vector3(top.position.x + Mathf.Cos(a * Mathf.Deg2Rad) * 0.10f,
                                      surface + i * 0.004f,
                                      top.position.z + Mathf.Sin(a * Mathf.Deg2Rad) * 0.10f);
                var rot = Quaternion.Euler(90f, a * 0.6f, 0f);
                _pile[i].FlyTo(pos, rot, 0.45f);
            }
        }

        /// <summary>Вскрытие: перевернуть стопку лицом.</summary>
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
    }
}

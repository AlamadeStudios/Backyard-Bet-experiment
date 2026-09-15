using System.Collections.Generic;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Мангал: на решётке жарятся котлеты.
    ///
    /// Состояние прожарки по сети не гоняется. Оно выводится из того, что уже
    /// синхронизировано, - из положения котлеты: лежит на решётке значит
    /// жарится, и каждый клиент считает время сам. Это и дешевле, и не может
    /// разъехаться с картинкой.
    ///
    /// Котлета кладётся по E, когда она в руке: мангал сам подскажет. Можно
    /// и добросить слабым замахом - решётка ловит всё, что на ней осталось.
    /// </summary>
    public class GrillStation : MonoBehaviour, IInteractable
    {
        [Tooltip("Сколько секунд до готовности.")]
        public float cookTime = 11f;

        [Tooltip("Сколько секунд до угля.")]
        public float burnTime = 20f;

        [Tooltip("Насколько выше решётки ловим котлету, м.")]
        public float catchHeight = 0.45f;

        static readonly Color Raw = new Color(0.62f, 0.24f, 0.24f);
        static readonly Color Done = new Color(0.35f, 0.19f, 0.09f);
        static readonly Color Burnt = new Color(0.09f, 0.08f, 0.08f);

        Bounds _grate;
        readonly Dictionary<Transform, Patty> _onGrate = new Dictionary<Transform, Patty>();

        /// <summary>Что мы знаем про одну котлету: её вид и время на огне.</summary>
        class Patty
        {
            public Renderer rend;
            public float cooked;
        }

        void Start()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) { enabled = false; return; }

            // Ловим только то, что лежит над решёткой: коробка мангала по
            // сторонам, от его верха вверх на ладонь.
            _grate = rend.bounds;
            _grate.min = new Vector3(_grate.min.x, rend.bounds.max.y - 0.05f, _grate.min.z);
            _grate.max = new Vector3(_grate.max.x, rend.bounds.max.y + catchHeight,
                                     _grate.max.z);

            // Угли под решёткой: без огня мангал - просто железный ящик.
            var coals = new GameObject("GrillCoals");
            coals.transform.SetParent(transform, false);
            coals.transform.position = new Vector3(rend.bounds.center.x,
                                                   rend.bounds.max.y - 0.28f,
                                                   rend.bounds.center.z);
            var fx = coals.AddComponent<FireEffect>();
            fx.size = 0.55f;
            fx.castLight = true;
        }

        void Update()
        {
            Collect();

            foreach (var kv in _onGrate)
            {
                var p = kv.Value;
                p.cooked += Time.deltaTime;

                // сырое -> румяное -> уголь; после готовности ещё есть время
                // забрать, и это вся игра: не передержать
                Color c = p.cooked < cookTime
                    ? Color.Lerp(Raw, Done, p.cooked / cookTime)
                    : Color.Lerp(Done, Burnt,
                                 Mathf.Clamp01((p.cooked - cookTime) / (burnTime - cookTime)));

                if (p.rend != null) p.rend.material.color = c;
            }
        }

        /// <summary>
        /// Пересобрать список котлет на решётке.
        ///
        /// Ищем по месту, а не по событию: котлета может попасть на мангал
        /// броском, скатиться с него или быть снятой чужой рукой, и ни одно
        /// из этих событий до нас не доходит.
        /// </summary>
        void Collect()
        {
            var net = PropNetwork.Instance;
            if (net == null) return;

            var seen = new HashSet<Transform>();
            for (int i = 0; i < net.Count; i++)
            {
                var prop = net.Prop(i);
                if (prop == null || prop.Held) continue;
                if (!prop.name.StartsWith("Patty")) continue;
                if (!_grate.Contains(prop.transform.position)) continue;

                seen.Add(prop.transform);
                if (_onGrate.ContainsKey(prop.transform)) continue;

                var r = prop.GetComponent<Renderer>();
                if (r != null) _onGrate[prop.transform] = new Patty { rend = r };
            }

            if (seen.Count == _onGrate.Count) return;

            // снятое с решётки перестаёт жариться, но прожарку не теряет -
            // её видно по цвету, и вернуть котлету на огонь можно
            var gone = new List<Transform>();
            foreach (var kv in _onGrate) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var t in gone) _onGrate.Remove(t);
        }

        // ------------------------------------------------------------ игрок

        public float Range => 3.2f;

        /// <summary>Точка над решёткой, куда ложится котлета.</summary>
        public Vector3 GrateSpot => new Vector3(_grate.center.x,
                                                _grate.min.y + 0.12f, _grate.center.z);

        public bool CanInteract(PlayerInteraction who) => Holding(who);

        public string Prompt(PlayerInteraction who) =>
            Holding(who) ? "положить на решётку" : "";

        /// <summary>Держит ли игрок котлету - только её мангал и принимает.</summary>
        static bool Holding(PlayerInteraction who)
        {
            if (who == null || who.HeldProp < 0 || PropNetwork.Instance == null) return false;
            var p = PropNetwork.Instance.Prop(who.HeldProp);
            return p != null && p.name.StartsWith("Patty");
        }
    }
}

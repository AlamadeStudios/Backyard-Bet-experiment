using System.Text.RegularExpressions;
using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Собирает игровой мир прямо при запуске: вешает двери, реквизит,
    /// мишени и стол, убирает декоративных персонажей, ставит обзорную камеру.
    ///
    /// Раньше это делалось пунктами меню в редакторе, и работало ненадёжно:
    /// правки сцены терялись, если редактор держал её открытой. Здесь всё
    /// строится в памяти при старте сцены - одинаково у хоста и у клиента,
    /// потому что карта у них одна и та же.
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        // Имя объекта в Blender: базовое имя + необязательный номер + суффиксы
        // копий (".d", ".001"). Строгий шаблон нужен, чтобы CanRack,
        // PotatoCannon, DiceCup и SnackCup не стали подбираемым реквизитом.
        static readonly (string name, float mass)[] Props =
        {
            ("Axe",       0.90f),
            ("Dart",      0.022f),
            ("SpareDart", 0.022f),
            ("PongBall",  0.003f),
            ("Can",       0.020f),
            ("Cup",       0.012f),
            ("Die",       0.008f),
            ("Card",      0.002f),
            ("Bottle",    0.350f),
            ("Potato",    0.180f),
        };

        static readonly string[] Doors = { "Door", "GateLeaf" };
        static readonly string[] Mannequins = { "CH_Bo", "CH_Mia", "CH_Rex", "CH_Sam" };

        bool _built;

        void Awake() => Build();

        static bool Matches(string objectName, string baseName) =>
            Regex.IsMatch(objectName,
                @"^" + Regex.Escape(baseName) + @"\d*(_\d+)?(\.d)?(\.\d+)*$");

        void Build()
        {
            if (_built) return;
            _built = true;

            var map = GameObject.Find("Map");
            if (map == null) { Debug.LogError("[Backyard Bet] Нет объекта Map - мир не собрать."); return; }

            int doors = 0, props = 0, targets = 0, table = 0;

            foreach (var mf in map.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                string n = go.name;

                // --- двери
                bool isDoor = false;
                foreach (var d in Doors) if (Matches(n, d)) { isDoor = true; break; }
                if (isDoor)
                {
                    if (go.GetComponent<DoorInteractable>() == null)
                    {
                        var door = go.AddComponent<DoorInteractable>();
                        if (n.StartsWith("GateLeaf")) { door.openAngle = 110f; door.hingeSide = 1f; }
                    }
                    doors++;
                    continue;
                }

                // --- мишени
                if (Matches(n, "AxeTarget") || Matches(n, "DartBoard"))
                {
                    var t = go.GetComponent<ThrowTarget>();
                    if (t == null) t = go.AddComponent<ThrowTarget>();
                    t.accepts = Matches(n, "AxeTarget") ? "Axe" : "Dart";
                    targets++;
                    continue;
                }

                // --- стол блефа
                if (n == "TableTop" || n == "TableFelt")
                {
                    if (go.GetComponent<BluffTableSeat>() == null)
                        go.AddComponent<BluffTableSeat>();
                    table++;
                    continue;
                }

                // --- подбираемый реквизит
                foreach (var (baseName, mass) in Props)
                {
                    if (!Matches(n, baseName)) continue;

                    var mc = go.GetComponent<MeshCollider>();
                    if (mc != null) mc.convex = true;   // Rigidbody не работает с вогнутым мешем

                    var rb = go.GetComponent<Rigidbody>();
                    if (rb == null) rb = go.AddComponent<Rigidbody>();
                    rb.mass = mass;
                    rb.linearDamping = 0.15f;
                    rb.angularDamping = 0.5f;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                    if (go.GetComponent<PickupInteractable>() == null)
                        go.AddComponent<PickupInteractable>();
                    props++;
                    break;
                }
            }

            int seats = ReplaceMannequins();
            EnsureSpectatorCamera();

            Debug.Log(string.Format(
                "[Backyard Bet] Мир собран: двери {0}, предметы {1}, мишени {2}, " +
                "стол {3}, места {4}", doors, props, targets, table, seats));
        }

        /// <summary>
        /// Убирает декоративных персонажей со стола, оставляя вместо каждого
        /// пустышку Seat_* - на них опирается расстановка игроков.
        /// </summary>
        static int ReplaceMannequins()
        {
            int made = 0;
            foreach (var name in Mannequins)
            {
                var ch = GameObject.Find(name);
                if (ch == null) continue;

                var seat = new GameObject("Seat_" + name.Substring(3));
                seat.transform.SetPositionAndRotation(ch.transform.position, ch.transform.rotation);
                Destroy(ch);
                made++;
            }
            return made;
        }

        /// <summary>Без камеры до спавна игрока экран был бы чёрным.</summary>
        static void EnsureSpectatorCamera()
        {
            if (FindAnyObjectByType<SpectatorCamera>() != null) return;
            new GameObject("SpectatorCamera").AddComponent<SpectatorCamera>();
        }
    }
}

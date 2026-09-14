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

        // HOU_FrontDoor - дверь дома из генератора: у неё есть родитель-петля
        // и своя разметка. Старое имя Door оставлено на случай, если карту
        // пересоберут прежней версией скрипта.
        static readonly string[] Doors =
        {
            "Door", "GateLeaf",
            "HOU_FrontDoor",      // входная дверь дома
            "ANX_BowlingDoor",    // дверь пристройки под вывеской боулинга
        };
        static readonly string[] Mannequins = { "CH_Bo", "CH_Mia", "CH_Rex", "CH_Sam" };

        /// <summary>
        /// Что убрано со двора.
        ///
        /// Объекты не удаляются, а выключаются: геометрия остаётся в модели,
        /// пересобирать карту в Blender не нужно. Чтобы вернуть развлечение,
        /// достаточно убрать его строки отсюда.
        ///
        /// Убран только инвентарь самих забав. Постройки вокруг них -
        /// пристройка боулинга, навес над бильярдом - остаются: без них у
        /// дома пропал бы кусок стены, а во дворе появилась бы пустота.
        /// </summary>
        static readonly string[] Hidden =
        {
            // бир-понг: стол, стаканы, шарики
            "BPTop", "BPLeg", "Cup", "PongBall",

            // рогатка: сама рогатка, снаряды и стеллаж с банками
            "SlingFork", "SlingPost", "Band", "Pouch", "Pellet", "CanRack", "Can",

            // бильярд: стол, лузы, шар и кий. Навес остаётся навесом
            "PoolFelt", "PoolFrame", "PoolLeg", "PoolRails", "Pocket",
            "CueBall", "CueStick",

            // боулинг: дорожка, жёлоба, кегли и шар. Само помещение остаётся
            "Lane", "Gutter", "Approach", "Pin", "PinBand", "BallReturn", "BowlBall",

            // лавка перед домом - столешница с двумя лавками
            "PicnicTable",
        };

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

            int hidden = HideRemoved(map);
            int doors = 0, props = 0, targets = 0, table = 0;

            foreach (var mf in map.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                if (!go.activeInHierarchy) continue;   // убранное не оснащаем
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

                // --- колесо фортуны: сам диск и есть точка взаимодействия
                if (n == "FortuneWheel")
                {
                    if (go.GetComponent<FortuneWheelHandle>() == null)
                        go.AddComponent<FortuneWheelHandle>();
                    continue;
                }

                // --- стакан с костями: трясётся в начале раунда
                if (Matches(n, "DiceCup"))
                {
                    if (go.GetComponent<DiceCupShaker>() == null)
                        go.AddComponent<DiceCupShaker>();
                    continue;
                }

                // --- стаканы бир-понга. Отбираем по высоте: на столе для
                // понга они стоят пирамидой, а не валяются как мусор во дворе
                if (Matches(n, "Cup") && go.transform.position.y > 0.5f)
                {
                    if (go.GetComponent<CupTarget>() == null)
                        go.AddComponent<CupTarget>();
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

                    // банки на стеллаже - цели для рогатки: считаются сбитыми,
                    // когда реально уехали с места или завалились
                    if (baseName == "Can" && go.GetComponent<KnockdownTarget>() == null)
                        go.AddComponent<KnockdownTarget>();

                    props++;
                    break;
                }
            }

            int seats = ReplaceMannequins();
            EnsureSpectatorCamera();
            int alive = Animate(map);

            Debug.Log(string.Format(
                "[Backyard Bet] Мир собран: двери {0}, предметы {1}, мишени {2}, " +
                "стол {3}, места {4}, анимаций {5}, убрано {6}",
                doors, props, targets, table, seats, alive, hidden));
        }

        /// <summary>
        /// Выключает то, что перечислено в Hidden.
        ///
        /// Именно выключает, а не удаляет: объект остаётся в сцене, и ничего
        /// не ломается, если на него кто-то ссылается. Идёт первым проходом -
        /// убранному не нужны ни физика, ни подбор, ни анимация.
        /// </summary>
        static int HideRemoved(GameObject map)
        {
            int hidden = 0;
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
            {
                if (!t.gameObject.activeSelf) continue;

                foreach (var name in Hidden)
                {
                    if (!Matches(t.name, name)) continue;
                    t.gameObject.SetActive(false);
                    hidden++;
                    break;
                }
            }
            return hidden;
        }

        /// <summary>
        /// Оживляет двор: огонь дрожит, мишень крутится, гирлянда и гамак
        /// качаются, плавники ходят по кругу, вода рябит.
        ///
        /// Всё локальное и косметическое - по сети не идёт ничего.
        /// </summary>
        static int Animate(GameObject map)
        {
            int n = 0;
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                string s = go.name;

                if (s.StartsWith("Flame"))
                {
                    if (go.GetComponent<FlickerAnimator>() == null)
                    {
                        AddFireLight(go);
                        go.AddComponent<FlickerAnimator>();
                        n++;
                    }
                }
                else if (s.StartsWith("AxeTarget"))
                {
                    if (go.GetComponent<SpinAnimator>() == null)
                    {
                        // мишень для топоров вращается вокруг своей оси на щите
                        var sp = go.AddComponent<SpinAnimator>();
                        sp.axis = Vector3.right;
                        sp.degreesPerSecond = 30f;
                        n++;
                    }
                }
                else if (s.StartsWith("Fin"))
                {
                    if (go.GetComponent<OrbitAnimator>() == null)
                    { go.AddComponent<OrbitAnimator>(); n++; }
                }
                else if (s.StartsWith("Bulb") || s.StartsWith("Flag"))
                {
                    if (go.GetComponent<SwayAnimator>() == null)
                    {
                        var sw = go.AddComponent<SwayAnimator>();
                        sw.degrees = s.StartsWith("Flag") ? 9f : 3f;
                        sw.speed = s.StartsWith("Flag") ? 1.8f : 0.9f;
                        n++;
                    }
                }
                else if (s.StartsWith("Hammock") || s.StartsWith("SwingRope"))
                {
                    if (go.GetComponent<SwayAnimator>() == null)
                    {
                        var sw = go.AddComponent<SwayAnimator>();
                        sw.degrees = 5f;
                        sw.speed = 0.6f;
                        n++;
                    }
                }
                else if (s.StartsWith("Boombox"))
                {
                    if (go.GetComponent<PulseAnimator>() == null)
                    { go.AddComponent<PulseAnimator>(); n++; }
                }
                else if (s.StartsWith("PoolWater"))
                {
                    if (go.GetComponent<WaterAnimator>() == null)
                    { go.AddComponent<WaterAnimator>(); n++; }
                }
                else if (s.StartsWith("HouseCeilingLamp") || s.StartsWith("PorchLampBulb") ||
                         s.StartsWith("WLampBulb"))
                {
                    // Лампы в модели есть, а источников света в FBX не бывает -
                    // без этого в доме кромешная темнота.
                    if (go.GetComponentInChildren<Light>() == null)
                    { AddRoomLight(go); n++; }
                }
            }
            return n;
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

        /// <summary>
        /// Живой огонёк к пламени.
        ///
        /// В FBX едут только меши и пустышки - источники света из Blender туда
        /// не попадают вообще. Поэтому костёр и факелы светились бы только
        /// собственным материалом, ничего вокруг не освещая. Свет вешаем сюда
        /// же, чтобы FlickerAnimator подхватил его и модулировал вместе с
        /// размером пламени.
        /// </summary>
        static void AddFireLight(GameObject flame)
        {
            if (flame.GetComponentInChildren<Light>() != null) return;

            var go = new GameObject("FireLight");
            go.transform.SetParent(flame.transform, false);
            go.transform.localPosition = Vector3.up * 0.15f;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.38f);
            light.intensity = 3.2f;
            light.range = 9f;
            light.shadows = LightShadows.None;      // теней от каждого огня не тянем
        }

        /// <summary>Ровный тёплый свет от лампы - в комнатах и над мишенями.</summary>
        static void AddRoomLight(GameObject lamp)
        {
            var go = new GameObject("RoomLight");
            go.transform.SetParent(lamp.transform, false);
            go.transform.localPosition = Vector3.down * 0.12f;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.92f, 0.78f);
            light.intensity = 2.6f;
            light.range = 11f;
            light.shadows = LightShadows.None;
        }

        /// <summary>Без камеры до спавна игрока экран был бы чёрным.</summary>
        static void EnsureSpectatorCamera()
        {
            if (FindAnyObjectByType<SpectatorCamera>() != null) return;
            new GameObject("SpectatorCamera").AddComponent<SpectatorCamera>();
        }
    }
}

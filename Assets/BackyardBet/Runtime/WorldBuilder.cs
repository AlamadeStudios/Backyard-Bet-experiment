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
            ("Patty",     0.120f),
            ("Stone",     0.900f),
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
        /// Вместе с забавой уходит и постройка над ней: пустая пергола и
        /// тент без стола - это не двор, а декорации, и они загораживают дом.
        /// Пристройка боулинга - исключение: она приделана к дому, и без неё
        /// в стене была бы дыра вместе с входной дверью, поэтому коробка
        /// остаётся, а вычищается только её нутро - см. AnnexKeep.
        /// </summary>
        static readonly string[] Hidden =
        {
            // бир-понг: стол, стаканы, шарики
            "BPTop", "BPLeg", "Cup", "PongBall",

            // рогатка: сама рогатка, снаряды и стеллаж с банками
            "SlingFork", "SlingPost", "Band", "Pouch", "Pellet", "CanRack", "Can",

            // бильярд: стол, лузы, пятнадцать шаров, кий и пергола над ними
            "PoolFelt", "PoolFrame", "PoolLeg", "PoolRails", "Pocket",
            "CueBall", "CueStick", "Ball", "Pergola", "PergPost",

            // лавка перед домом - столешница с двумя лавками - и тент над
            // ней: четыре стойки с крышей стоят прямо напротив фасада, а
            // арбуз лежал на столешнице и без неё повис бы в воздухе.
            // Палатка в дальнем углу двора к этому не относится и остаётся
            "PicnicTable", "TentRoof", "TentPost", "Watermelon",
        };

        /// <summary>
        /// Что держалось на убранном и уходит вслед за ним.
        ///
        /// Эти вещи есть по всему двору, и убирать их по имени нельзя: вместе
        /// с бильярдной площадкой исчезла бы площадка дартса, а вместе с
        /// бутылками со столешницы - бутылки на барной стойке. Поэтому они
        /// уходят по соседству: если рядом не осталось ничего из убранного,
        /// вещь стоит на своём месте и остаётся.
        ///
        /// Радиусы взяты по замерам карты, а не на глаз:
        ///  * площадка - инвентарь стоит на ней вплотную (дальше 0.05 м ни
        ///    один не отходит), ближайшая чужая площадка в 7.5 м;
        ///  * бутылки - те, что стояли на столешнице, в 0.93 м, ближайшая
        ///    чужая в 3.03 м.
        /// </summary>
        /// <summary>
        /// Что оставить внутри пристройки боулинга.
        ///
        /// Внутри полторы сотни объектов - дорожка, кегли, диван, полка с
        /// обувью, стойка со снеками, афиши, пиксельное табло. Перечислять их
        /// по именам бессмысленно и опасно: жёлоб дорожки называется Gutter,
        /// ровно как водостоки на крыше дома, и по имени вместе с боулингом
        /// уходили они. Поэтому наоборот: убираем всё, что внутри коробки
        /// здания, а здесь перечислено то немногое, что остаётся, - сама
        /// коробка и свет.
        ///
        /// Светильники трогать не надо ещё и потому, что источники света -
        /// объекты без меша, а правило работает только по видимой геометрии.
        /// </summary>
        static readonly string[] AnnexKeep =
        {
            "AnnexFloor", "AnnexWallX", "AnnexWallY", "AnnexCeil", "AnnexRoof",
            "AnnexGutter", "AnnexDoorFrame", "ANX_BowlingDoor",
            "WinFrame", "WinGlass",
            "LampBulb", "LampCord", "LampShade",
        };

        /// <summary>Высота помещения пристройки, м - от пола до конька.</summary>
        const float AnnexHeight = 6f;

        static readonly (string name, float radius)[] HiddenWith =
        {
            ("Apron",       4f),    // бетонная площадка под забавой
            ("LooseBottle", 2f),    // стояли на столешнице пикникового стола
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

                // --- мангал: на решётке жарятся котлеты
                if (n == "Grill")
                {
                    if (go.GetComponent<GrillStation>() == null)
                        go.AddComponent<GrillStation>();
                    continue;
                }

                // --- экран табло: на нём рисуется живой счёт двора
                if (n == "ScoreScreen")
                {
                    if (go.GetComponent<ScoreScreenBoard>() == null)
                        go.AddComponent<ScoreScreenBoard>();
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
            var all = map.GetComponentsInChildren<Transform>(true);
            var gone = new System.Collections.Generic.List<Vector3>();

            // первым проходом убираем сам инвентарь и запоминаем, где он стоял
            foreach (var t in all)
            {
                if (!t.gameObject.activeSelf) continue;

                bool off = false;
                foreach (var name in Hidden)
                    if (Matches(t.name, name)) { off = true; break; }
                if (!off) continue;

                gone.Add(t.position);
                t.gameObject.SetActive(false);
            }

            int hidden = gone.Count;

            // вторым - то, что на нём держалось. Выключенный объект сохраняет
            // свои координаты, поэтому порядок проходов роли не играет
            foreach (var t in all)
            {
                if (!t.gameObject.activeSelf) continue;

                float radius = 0f;
                foreach (var (name, r) in HiddenWith)
                    if (Matches(t.name, name)) { radius = r; break; }
                if (radius <= 0f) continue;

                foreach (var at in gone)
                {
                    var d = t.position - at;
                    if (new Vector2(d.x, d.z).magnitude > radius) continue;
                    t.gameObject.SetActive(false);
                    hidden++;
                    break;
                }
            }
            hidden += HideAnnexInside(all);
            return hidden;
        }

        /// <summary>
        /// Вычистить пристройку боулинга, оставив здание и свет.
        ///
        /// Границы берём с плиты пола - она лежит ровно по footprint здания,
        /// - и тянем вверх до конька. Координаты нигде не вбиты: подвинут
        /// пристройку в генераторе, правило переедет вместе с ней.
        /// </summary>
        static int HideAnnexInside(Transform[] all)
        {
            Transform floor = null;
            foreach (var t in all)
                if (t.name == "AnnexFloor") { floor = t; break; }

            var fr = floor != null ? floor.GetComponent<Renderer>() : null;
            if (fr == null) return 0;

            Bounds room = fr.bounds;
            room.Encapsulate(new Vector3(room.center.x, room.max.y + AnnexHeight, room.center.z));

            int hidden = 0;
            foreach (var t in all)
            {
                if (!t.gameObject.activeSelf) continue;
                if (t.GetComponent<Renderer>() == null) continue;   // свет и метки не трогаем
                if (!room.Contains(t.position)) continue;

                bool keep = false;
                foreach (var name in AnnexKeep)
                    if (Matches(t.name, name)) { keep = true; break; }
                if (keep) continue;

                t.gameObject.SetActive(false);
                hidden++;
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

                // Огонь - это движение, а не форма: неподвижная оранжевая
                // капля в модели читалась пластилином. Меш прячем, на его
                // место встаёт пламя с искрами, дымом и дрожащим светом.
                if (s.StartsWith("Flame") || s == "Campfire")
                {
                    if (go.GetComponentInChildren<FireEffect>() == null)
                    {
                        FireEffect.Replace(go, s == "Campfire" ? 2.4f : 0.9f, true);
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

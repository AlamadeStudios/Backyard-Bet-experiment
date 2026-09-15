using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Живой огонь: пламя, искры, дым и дрожащий свет.
    ///
    /// В модели огонь был неподвижной оранжевой каплей - издали пятно,
    /// вблизи пластилин. Огонь нельзя вылепить мешем: он читается движением,
    /// а не формой, поэтому здесь система частиц.
    ///
    /// Всё строится кодом и не тянет за собой ни одного стороннего ассета:
    /// текстура частицы - мягкое пятно, посчитанное при запуске.
    /// </summary>
    public class FireEffect : MonoBehaviour
    {
        [Tooltip("Размер огня: 1 - факел, 2.5 - костёр.")]
        public float size = 1f;

        [Tooltip("Светит ли огонь вокруг себя.")]
        public bool castLight = true;

        Light _light;
        float _lightBase;
        float _phase;

        /// <summary>Поставить огонь на место объекта и спрятать его меш.</summary>
        public static FireEffect Replace(GameObject marker, float size, bool light)
        {
            var go = new GameObject("Fire");
            go.transform.SetPositionAndRotation(marker.transform.position, Quaternion.identity);
            go.transform.SetParent(marker.transform.parent, true);

            foreach (var r in marker.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            var fx = go.AddComponent<FireEffect>();
            fx.size = size;
            fx.castLight = light;
            return fx;
        }

        void Start()
        {
            _phase = Random.value * 10f;

            Flame();
            Sparks();
            Smoke();
            if (castLight) Glow();
        }

        // ------------------------------------------------------------ части

        void Flame()
        {
            var ps = NewSystem("Flame", 0.55f * size, 90);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f * size, 0.62f * size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f * size, 1.5f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.30f * size, 0.62f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.08f;              // пламя тянет вверх

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.16f * size;

            // от белого ядра к оранжевому языку и в прозрачный дымок
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(
                new[] { new Color(1f, 0.95f, 0.72f), new Color(1f, 0.62f, 0.16f),
                        new Color(0.85f, 0.22f, 0.05f) },
                new[] { 0f, 1f, 1f, 0.55f, 0f });

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 0.35f, 1f, 1f, 0.15f));

            Render(ps, additive: true, stretch: false);
        }

        void Sparks()
        {
            var ps = NewSystem("Sparks", 0.9f * size, 16);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f * size, 1.4f * size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * size, 2.6f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.gravityModifier = -0.25f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.2f * size;

            // искру сносит вбок - без этого она летит по линейке
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 1.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(
                new[] { new Color(1f, 0.92f, 0.6f), new Color(1f, 0.5f, 0.1f) },
                new[] { 0f, 1f, 0.7f, 1f, 0f });

            Render(ps, additive: true, stretch: true);
        }

        void Smoke()
        {
            var ps = NewSystem("Smoke", 1.4f * size, 10);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * size, 0.9f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f * size, 0.8f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 0.22f * size;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(
                new[] { new Color(0.35f, 0.32f, 0.30f), new Color(0.55f, 0.55f, 0.56f) },
                new[] { 0f, 0.3f, 0.25f, 0.28f, 1f, 0f });

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0.6f, 1f, 2.2f));

            Render(ps, additive: false, stretch: false);
        }

        void Glow()
        {
            var go = new GameObject("FireLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.4f * size;

            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.68f, 0.32f);
            _light.range = 7f * size;
            _lightBase = 2.2f * size;
            _light.intensity = _lightBase;
            _light.shadows = LightShadows.None;    // мягкий заполняющий свет
        }

        void Update()
        {
            if (_light == null) return;

            // Дрожание: две несинхронные волны плюс редкий всплеск. Одна
            // синусоида читается как ровная пульсация, а не как костёр.
            float t = Time.time * 7f + _phase;
            float flicker = Mathf.Sin(t) * 0.12f + Mathf.Sin(t * 2.7f) * 0.07f
                            + (Mathf.PerlinNoise(t * 0.35f, _phase) - 0.5f) * 0.3f;
            _light.intensity = _lightBase * (1f + flicker);
        }

        // ------------------------------------------------------------ сборка

        ParticleSystem NewSystem(string name, float rate, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = max / Mathf.Max(0.2f, rate);
            return ps;
        }

        void Render(ParticleSystem ps, bool additive, bool stretch)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = stretch ? ParticleSystemRenderMode.Stretch
                                   : ParticleSystemRenderMode.Billboard;
            if (stretch) r.lengthScale = 2.5f;
            r.material = additive ? AdditiveMaterial : SoftMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = additive ? -2f : 0f;
        }

        static ParticleSystem.MinMaxGradient Gradient(Color[] colors, float[] alphaPairs)
        {
            var g = new UnityEngine.Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                ck[i] = new GradientColorKey(colors[i], colors.Length == 1 ? 0f
                                             : i / (float)(colors.Length - 1));

            int n = alphaPairs.Length / 2;
            var ak = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
                ak[i] = new GradientAlphaKey(alphaPairs[i * 2 + 1], alphaPairs[i * 2]);

            g.SetKeys(ck, ak);
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>Кривая по парам «время, значение».</summary>
        static AnimationCurve Curve(params float[] pairs)
        {
            var c = new AnimationCurve();
            for (int i = 0; i < pairs.Length; i += 2) c.AddKey(pairs[i], pairs[i + 1]);
            return c;
        }

        // ------------------------------------------------------------ материал

        static Material _add, _soft;
        static Texture2D _dot;

        static Material AdditiveMaterial
        {
            get
            {
                if (_add == null) _add = Make("Legacy Shaders/Particles/Additive", Color.white);
                return _add;
            }
        }

        static Material SoftMaterial
        {
            get
            {
                if (_soft == null)
                    _soft = Make("Legacy Shaders/Particles/Alpha Blended Premultiply",
                                 Color.white);
                return _soft;
            }
        }

        static Material Make(string shaderName, Color c)
        {
            var sh = Shader.Find(shaderName)
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { mainTexture = Dot, color = c };
            return m;
        }

        /// <summary>
        /// Мягкое пятно - тело частицы. Считается один раз при запуске:
        /// заводить под него файл в проекте незачем, а спад яркости к краю
        /// важнее разрешения.
        /// </summary>
        static Texture2D Dot
        {
            get
            {
                if (_dot != null) return _dot;

                const int n = 64;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n - 0.5f;
                        float dy = (y + 0.5f) / n - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);               // мягкий спад
                        px[y * n + x] = new Color(1f, 1f, 1f, a);
                    }
                _dot.SetPixels32(px);
                _dot.Apply(true);
                return _dot;
            }
        }
    }
}

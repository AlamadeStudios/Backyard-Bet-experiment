using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Струя дыма: из трубы, от мангала, из дула пушки.
    ///
    /// В модели дым был гроздью неподвижных шаров - ровно та же беда, что и
    /// с огнём: дым узнаётся движением, а висящий шар читается пластилином.
    ///
    /// Отдельно от FireEffect, потому что это другая задача: там нужен
    /// источник света и искры, здесь - медленный редкий подъём, которого
    /// должно быть ровно столько, чтобы его заметили, но он не лез в кадр.
    /// </summary>
    public class SmokePlume : MonoBehaviour
    {
        [Tooltip("Размер клубов, м.")]
        public float size = 1f;

        [Tooltip("Скорость подъёма, м/с.")]
        public float rise = 0.7f;

        [Tooltip("Сколько живёт клуб, с.")]
        public float life = 3.2f;

        [Tooltip("Наибольшая непрозрачность клуба.")]
        [Range(0f, 1f)] public float density = 0.26f;

        [Tooltip("Куда сносит ветром, м/с.")]
        public Vector3 wind = new Vector3(0.35f, 0f, 0.15f);

        /// <summary>Поставить дым на место объекта и спрятать его меш.</summary>
        public static SmokePlume Replace(GameObject marker, float size, float density)
        {
            var go = new GameObject("Smoke");
            go.transform.SetPositionAndRotation(marker.transform.position, Quaternion.identity);
            go.transform.SetParent(marker.transform.parent, true);

            foreach (var r in marker.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            var s = go.AddComponent<SmokePlume>();
            s.size = size;
            s.density = density;
            return s;
        }

        void Start()
        {
            var ps = gameObject.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.7f, rise * 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * size, 1.0f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.02f;

            var em = ps.emission;
            em.rateOverTime = 24f / life;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.18f * size;

            // Ветер сносит струю вбок, иначе дым стоит столбом - так не
            // бывает даже в безветренный день.
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = wind.x;
            force.y = wind.y;
            force.z = wind.z;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.35f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.42f, 0.40f, 0.38f), 0f),
                    new GradientColorKey(new Color(0.62f, 0.62f, 0.63f), 1f)
                },
                // клуб проявляется, держится и тает - ключи обязаны идти
                // парами «время, прозрачность», иначе последний потеряется
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(density, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0.5f);
            curve.AddKey(1f, 2.0f);          // клуб расходится, поднимаясь
            sz.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = FireEffect.SoftParticle;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}

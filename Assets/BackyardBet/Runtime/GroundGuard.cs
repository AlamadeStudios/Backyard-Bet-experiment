using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Страховочный пол под всем двором.
    ///
    /// Меш земли из Blender плоский ровно на нуле внутри забора (terrain_z()
    /// поднимает рельеф только снаружи участка), поэтому один большой
    /// коллайдер с верхней гранью на y=0 закрывает весь игровой двор.
    /// Он невидим и нужен как гарантия: даже если коллайдеры меша не
    /// сгенерировались или где-то дырка в геометрии, игрок не улетит вниз.
    /// </summary>
    public class GroundGuard : MonoBehaviour
    {
        public const float YardHalfSize = 45f;   // забор стоит на ±38 м, берём с запасом

        void Awake()
        {
            if (GameObject.Find("__GroundGuard") != null) return;

            var go = new GameObject("__GroundGuard");
            go.transform.position = new Vector3(0f, -0.5f, 0f);   // верхняя грань ровно на 0

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(YardHalfSize * 2f, 1f, YardHalfSize * 2f);

            Debug.Log("[Backyard Bet] Страховочный пол создан: " +
                      (YardHalfSize * 2f) + "x" + (YardHalfSize * 2f) + " м на уровне y=0");
        }
    }
}

using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Предмет, который можно взять и бросить: топор, бутылка, банка, шарик,
    /// кубик.
    ///
    /// Обычный MonoBehaviour без сетевых компонентов. Владением, физикой и
    /// рассылкой позиций занимается PropNetwork на GameRoot: объекты,
    /// расставленные в сцене заранее, сетевыми делать ненадёжно, а созданный
    /// в игре GameRoot работает всегда.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PickupInteractable : MonoBehaviour, IInteractable
    {
        [Tooltip("Номер предмета в общем списке - выставляется при сборке мира.")]
        public int propIndex = -1;

        [Tooltip("Сила броска.")]
        public float throwForce = 11f;

        [Tooltip("Закрутка при броске - чтобы топор летел кувырком.")]
        public float throwSpin = 12f;

        public float Range => 3.5f;

        /// <summary>Кто держит: 0 - никто (храним clientId + 1).</summary>
        public ulong HolderPlusOne { get; set; }
        public bool Held => HolderPlusOne != 0;

        /// <summary>Кто бросил последним - кому засчитывать попадание.</summary>
        public ulong LastThrower { get; set; }
        public bool WasThrown { get; set; }

        public Rigidbody Body { get; private set; }
        public Vector3 HomePos { get; private set; }
        public Quaternion HomeRot { get; private set; }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            HomePos = transform.position;
            HomeRot = transform.rotation;
        }

        public string Prompt(PlayerInteraction who) => Held ? "" : "взять";

        public bool CanInteract(PlayerInteraction who) =>
            propIndex >= 0 && !Held && who.HeldProp < 0;
    }
}

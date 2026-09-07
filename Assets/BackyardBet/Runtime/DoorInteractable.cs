using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Дверь и калитка.
    ///
    /// Это обычный MonoBehaviour без сетевых компонентов: состояние всех
    /// дверей держит WorldState на GameRoot, который хост создаёт в рантайме.
    /// Так надёжнее, чем вешать NetworkObject на каждую створку прямо в
    /// сцене - объекты в сцене оживают только при определённых настройках
    /// сетевого менеджера, а созданный в игре объект работает всегда.
    ///
    /// Створка вращается вокруг вычисленной петли у своего края, а не вокруг
    /// центра - иначе доска крутилась бы в воздухе.
    /// </summary>
    public class DoorInteractable : MonoBehaviour, IInteractable
    {
        [Tooltip("Номер двери в общем списке - выставляется при настройке сцены.")]
        public int doorIndex = -1;

        [Tooltip("На сколько градусов распахивается.")]
        public float openAngle = 95f;

        [Tooltip("Скорость открытия, градусов в секунду.")]
        public float speed = 200f;

        [Tooltip("С какой стороны петля: -1 левый край, +1 правый.")]
        public float hingeSide = -1f;

        public float Range => 3.5f;

        bool _open;
        Vector3 _hinge;
        Vector3 _closedPos;
        Quaternion _closedRot;
        float _angle;

        void Awake() => ComputeHinge();

        /// <summary>Петля - у края створки по её узкой стороне.</summary>
        void ComputeHinge()
        {
            _closedPos = transform.position;
            _closedRot = transform.rotation;

            var rend = GetComponentInChildren<Renderer>();
            if (rend == null) { _hinge = _closedPos; return; }

            Bounds b = rend.bounds;
            Vector3 h = b.center;
            if (b.size.x >= b.size.z) h.x += hingeSide * b.size.x * 0.5f;
            else                      h.z += hingeSide * b.size.z * 0.5f;
            h.y = _closedPos.y;
            _hinge = h;
        }

        public string Prompt(PlayerInteraction who) => _open ? "закрыть" : "открыть";

        public bool CanInteract(PlayerInteraction who) => doorIndex >= 0;

        /// <summary>Вызывает WorldState, когда состояние приехало по сети.</summary>
        public void SetOpen(bool open) => _open = open;

        void Update()
        {
            float target = _open ? openAngle : 0f;
            if (Mathf.Approximately(_angle, target)) return;
            Apply(Mathf.MoveTowards(_angle, target, speed * Time.deltaTime));
        }

        void Apply(float angle)
        {
            _angle = angle;
            var rot = Quaternion.AngleAxis(angle * -hingeSide, Vector3.up);
            transform.SetPositionAndRotation(
                _hinge + rot * (_closedPos - _hinge),
                rot * _closedRot);
        }
    }
}

using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Игрок от первого лица. Ввод обрабатывает только владелец объекта,
    /// остальным этот компонент выключается - иначе каждый клиент пытался бы
    /// двигать чужих игроков.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NetworkPlayerMovement : NetworkBehaviour
    {
        [Header("Ходьба")]
        public float walkSpeed = 3.2f;
        public float sprintSpeed = 5.8f;
        public float jumpHeight = 1.05f;
        public float gravity = -18f;

        [Header("Обзор")]
        public float mouseSensitivity = 2.2f;
        public float pitchLimit = 85f;
        public Transform cameraPivot;

        [Header("Страховка от падения")]
        [Tooltip("Ниже этой высоты игрок считается провалившимся и возвращается к столу.")]
        public float killHeight = -12f;

        CharacterController _cc;
        Vector3 _velocity;
        float _pitch;
        Vector3 _spawnPos;

        void Awake() => _cc = GetComponent<CharacterController>();

        public override void OnNetworkSpawn()
        {
            bool mine = IsOwner;

            // камера и слух только у своего игрока
            if (cameraPivot != null)
            {
                var cam = cameraPivot.GetComponentInChildren<Camera>(true);
                if (cam != null) cam.gameObject.SetActive(mine);
            }
            if (!mine)
            {
                enabled = false;
                return;
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _spawnPos = transform.position;
        }

        /// <summary>Вернуть игрока на твёрдую землю - страховка от проваливания.</summary>
        public void Recover()
        {
            var spawner = FindAnyObjectByType<PlayerSpawnPoints>();
            Vector3 target = spawner != null ? spawner.SafeSpot()
                                             : _spawnPos + Vector3.up * 1f;
            _cc.enabled = false;
            transform.position = target;
            _cc.enabled = true;
            _velocity = Vector3.zero;
            Debug.Log("[Backyard Bet] Игрок провалился и возвращён на землю.");
        }

        void Update()
        {
            if (!IsOwner) return;

            if (transform.position.y < killHeight) Recover();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Cursor.lockState == CursorLockMode.Locked) Look();
            Move();
        }

        void Look()
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, mx, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - my, -pitchLimit, pitchLimit);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            bool grounded = _cc.isGrounded;
            if (grounded && _velocity.y < 0f) _velocity.y = -2f;   // прижать к земле

            Vector3 wish = Vector3.zero;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var mv = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                wish = transform.right * mv.x + transform.forward * mv.y;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                wish *= Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

                if (grounded && Input.GetKeyDown(KeyCode.Space))
                    _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            _velocity.y += gravity * Time.deltaTime;
            _cc.Move((wish + Vector3.up * _velocity.y) * Time.deltaTime);
        }
    }
}

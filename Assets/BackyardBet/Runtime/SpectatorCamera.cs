using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Обзорная камера двора.
    ///
    /// В сцене своей камеры нет - её приносит префаб игрока. Пока игрок не
    /// заспавнился (меню, подключение, сбой сети), экран был бы чёрным с
    /// надписью "No cameras rendering". Эта камера показывает двор всё это
    /// время и сама уходит, как только появилась камера игрока.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Откуда смотрим на двор в меню.")]
        public Vector3 viewPosition = new Vector3(14f, 12f, -16f);

        [Tooltip("Куда направлен взгляд.")]
        public Vector3 lookAt = new Vector3(0f, 3f, 2f);

        [Tooltip("Медленный облёт двора, градусов в секунду. 0 - камера стоит.")]
        public float orbitSpeed = 2.5f;

        Camera _cam;
        float _angle;
        float _radius;
        float _height;

        void Awake()
        {
            var go = new GameObject("SpectatorCamera");
            go.transform.SetParent(transform, false);

            _cam = go.AddComponent<Camera>();
            _cam.fieldOfView = 55f;
            go.AddComponent<AudioListener>();

            Vector3 flat = new Vector3(viewPosition.x - lookAt.x, 0f, viewPosition.z - lookAt.z);
            _radius = flat.magnitude;
            _height = viewPosition.y;
            _angle = Mathf.Atan2(flat.z, flat.x);

            Place();
        }

        void LateUpdate()
        {
            if (_cam == null) return;

            // как только у игрока появилась своя камера - уступаем ей
            if (PlayerCameraLive())
            {
                _cam.gameObject.SetActive(false);
                enabled = false;
                return;
            }

            _angle += orbitSpeed * Mathf.Deg2Rad * Time.deltaTime;
            Place();
        }

        void Place()
        {
            var p = new Vector3(lookAt.x + Mathf.Cos(_angle) * _radius,
                                _height,
                                lookAt.z + Mathf.Sin(_angle) * _radius);
            _cam.transform.position = p;
            _cam.transform.LookAt(lookAt);
        }

        /// <summary>Есть ли в сцене работающая камера игрока, кроме нашей.</summary>
        bool PlayerCameraLive()
        {
            foreach (var c in Camera.allCameras)
                if (c != _cam && c.isActiveAndEnabled) return true;
            return false;
        }
    }
}

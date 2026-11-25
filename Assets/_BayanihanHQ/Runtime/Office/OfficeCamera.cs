using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BayanihanHQ
{
    /// <summary>
    /// Isometric office camera (orthographic, fixed tilt). Right-drag or Q/E orbits, the scroll wheel zooms, middle-drag
    /// or WASD/arrows pan, F frames the whole room. <see cref="Focus"/> glides to a point, used when an agent is clicked.
    /// Reads devices directly through the Input System, as the game's InputHelper does.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OfficeCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(0, 0.6f, 0.5f);
        [Range(20, 70)] public float pitch = 38f;
        public float yaw = 32f;
        public float size = 9.5f;
        public Vector2 sizeRange = new Vector2(2.5f, 14f);
        [Tooltip("Room extent the target is kept inside (x, z half sizes).")]
        public Vector2 bounds = new Vector2(12f, 8f);
        public float distance = 40f;

        Camera cam;
        Vector3 homeTarget;
        float homeYaw, homeSize;
        Vector3 goalTarget;
        float goalYaw, goalSize;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            homeTarget = goalTarget = target;
            homeYaw = goalYaw = yaw;
            homeSize = goalSize = size;
            Apply();
        }

        /// <summary>Glides to look at a point, zoomed to the given size (smaller is closer).</summary>
        public void Focus(Vector3 point, float zoom = 3.6f)
        {
            goalTarget = point + Vector3.up * 0.6f;
            goalSize = Mathf.Clamp(zoom, sizeRange.x, sizeRange.y);
        }

        /// <summary>Back to the whole-room view.</summary>
        public void FrameAll()
        {
            goalTarget = homeTarget;
            goalYaw = homeYaw;
            goalSize = homeSize;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (keys != null)
            {
                if (keys.qKey.isPressed) goalYaw += 90f * dt;
                if (keys.eKey.isPressed) goalYaw -= 90f * dt;
                if (keys.fKey.wasPressedThisFrame) FrameAll();

                var pan = Vector2.zero;
                if (keys.wKey.isPressed || keys.upArrowKey.isPressed) pan.y += 1;
                if (keys.sKey.isPressed || keys.downArrowKey.isPressed) pan.y -= 1;
                if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) pan.x += 1;
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) pan.x -= 1;
                if (pan != Vector2.zero) Pan(pan * goalSize * 1.2f * dt);
            }

            if (mouse != null)
            {
                var delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed) goalYaw += delta.x * 0.25f;
                if (mouse.middleButton.isPressed)
                {
                    float unitsPerPixel = 2f * cam.orthographicSize / Mathf.Max(1, Screen.height);
                    Pan(-delta * unitsPerPixel);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (!overUi && Mathf.Abs(scroll) > 0.01f) goalSize *= Mathf.Pow(0.9f, Mathf.Sign(scroll));
            }

            goalSize = Mathf.Clamp(goalSize, sizeRange.x, sizeRange.y);
            goalTarget.x = Mathf.Clamp(goalTarget.x, -bounds.x, bounds.x);
            goalTarget.z = Mathf.Clamp(goalTarget.z, -bounds.y, bounds.y);

            float k = 1f - Mathf.Exp(-8f * dt);
            target = Vector3.Lerp(target, goalTarget, k);
            yaw = Mathf.Lerp(yaw, goalYaw, k);
            size = Mathf.Lerp(size, goalSize, k);
            Apply();
        }

        /// <summary>Moves the target along the ground in screen directions.</summary>
        void Pan(Vector2 screen)
        {
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            goalTarget += right * screen.x + forward * screen.y;
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
            cam.orthographicSize = size;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = distance * 2.5f;
        }
    }
}

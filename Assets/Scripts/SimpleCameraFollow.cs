using UnityEngine;

namespace Vampire.Player
{
    /// <summary>
    /// Simple first-person camera controller
    /// </summary>
    public class SimpleCameraFollow : MonoBehaviour
    {
        [Header("Mouse Look Settings")]
        [SerializeField] private float mouseSensitivity = 100f;
        [SerializeField] private Transform playerBody;
        
        private float xRotation = 0f;

        void Start()
        {
            Debug.Log("[SimpleCameraFollow] Camera controller started");
            
            // Lock cursor for FPS
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            // Mouse look
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            
            if (playerBody != null)
            {
                playerBody.Rotate(Vector3.up * mouseX);
            }

            // Press ESC to unlock cursor
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}

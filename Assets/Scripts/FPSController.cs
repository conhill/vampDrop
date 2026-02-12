using UnityEngine;

namespace Vampire.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FPSController : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraTransform;
        public LayerMask groundMask;

        [Header("Movement Settings")]
        public float walkSpeed = 7f;
        public float runSpeed = 12f;
        public float crouchSpeed = 3f;
        public float jumpHeight = 2.5f;
        public float gravity = -19.62f;

        [Header("Look Settings")]
        public float mouseSensitivity = 2f;
        public float lookSmoothing = 10f;
        public bool invertY = false;
        public float maxLookAngle = 90f;

        [Header("Ground Check")]
        public float groundCheckRadius = 0.3f;
        public float groundCheckDistance = 0.4f;

        // Private variables
        private CharacterController controller;
        private float defaultHeight;
        private float crouchHeight;
        private Vector3 velocity;
        private float xRotation = 0f;
        private float smoothMouseX = 0f;
        private float smoothMouseY = 0f;
        private bool isGrounded;

        void Start()
        {
            controller = GetComponent<CharacterController>();
            defaultHeight = controller.height;
            crouchHeight = defaultHeight * 0.5f;

            // Lock cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (cameraTransform == null)
            {
                cameraTransform = Camera.main.transform;
            }

            Debug.Log("[FPSController] Initialized");
        }

        void Update()
        {
            ProcessLook();
            ProcessMovement();

            // ESC to unlock cursor
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void ProcessLook()
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            // Smooth mouse input
            smoothMouseX = Mathf.Lerp(smoothMouseX, mouseX, lookSmoothing * Time.deltaTime);
            smoothMouseY = Mathf.Lerp(smoothMouseY, mouseY, lookSmoothing * Time.deltaTime);

            // Rotate camera up/down
            xRotation += (invertY ? smoothMouseY : -smoothMouseY) * mouseSensitivity;
            xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
            cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

            // Rotate player left/right
            transform.Rotate(Vector3.up * smoothMouseX * mouseSensitivity);
        }

        void ProcessMovement()
        {
            // Ground check
            Vector3 groundCheckPos = transform.position + Vector3.up * groundCheckRadius;
            isGrounded = Physics.CheckSphere(groundCheckPos, groundCheckRadius, groundMask);

            // Reset vertical velocity when grounded
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }

            // Get input
            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");
            bool running = Input.GetKey(KeyCode.LeftShift);
            bool crouching = Input.GetKey(KeyCode.LeftControl);
            bool jumping = Input.GetKeyDown(KeyCode.Space);

            // Calculate move direction
            Vector3 move = transform.right * moveX + transform.forward * moveZ;
            if (move.magnitude > 1f)
            {
                move = move.normalized;
            }

            // Determine speed
            float targetSpeed = walkSpeed;
            if (crouching)
            {
                targetSpeed = crouchSpeed;
            }
            else if (running && isGrounded)
            {
                targetSpeed = runSpeed;
            }

            // Handle crouching height
            float targetHeight = crouching ? crouchHeight : defaultHeight;
            if (controller.height != targetHeight)
            {
                float oldHeight = controller.height;
                controller.height = Mathf.Lerp(controller.height, targetHeight, 8f * Time.deltaTime);
                
                // Adjust position when changing height
                Vector3 pos = transform.position;
                pos.y += (controller.height - oldHeight) * 0.5f;
                transform.position = pos;

                // Adjust camera
                Vector3 camPos = cameraTransform.localPosition;
                camPos.y = controller.height * 0.85f; // Camera at 85% of height
                cameraTransform.localPosition = camPos;
            }

            // Apply movement
            controller.Move(move * targetSpeed * Time.deltaTime);

            // Jump
            if (jumping && isGrounded && !crouching)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            // Apply gravity
            velocity.y += gravity * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);
        }

        void OnDrawGizmosSelected()
        {
            if (controller != null)
            {
                Vector3 groundCheckPos = transform.position + Vector3.up * groundCheckRadius;
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheckPos, groundCheckRadius);
            }
        }
    }
}

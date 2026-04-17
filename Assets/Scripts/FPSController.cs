using UnityEngine;

namespace Vampire.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FPSController : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraTransform;
        public LayerMask groundMask;

        [Header("Audio")]
        public FPSAudioManager audioManager;

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
        
        [Header("Jump Settings")]
        [Tooltip("Cooldown between jumps in seconds")]
        public float jumpCooldown = 0.3f;
        
        [Tooltip("Y velocity damping factor (0-1, higher = more damping)")]
        [Range(0f, 1f)]
        public float yVelocityDamping = 0.15f;
        
        [Header("Debug Ground Detection")]
        [Tooltip("Enable detailed ground detection logging")]
        public bool debugGroundDetection = false;

        // Private variables
        private CharacterController controller;
        private float defaultHeight;
        private float crouchHeight;
        private Vector3 velocity;
        private float xRotation = 0f;
        private float smoothMouseX = 0f;
        private float smoothMouseY = 0f;
        private bool isGrounded;
        private float lastJumpTime = -999f; // Track last jump time for cooldown

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

            // Find audio manager if not assigned
            if (audioManager == null)
            {
                audioManager = FindObjectOfType<FPSAudioManager>();
                // Debug.Log($"[FPSController] Audio manager found: {audioManager != null}");
            }
            else
            {
                // Debug.Log("[FPSController] Audio manager already assigned");
            }

            // Debug.Log("[FPSController] Initialized");
        }

        void Update()
        {
            ProcessLook();
            ProcessMovement();

            // ESC — let EscapeMenuManager handle it when present; legacy unlock otherwise
            if (Input.GetKeyDown(KeyCode.Escape)
                && Vampire.EscapeMenuManager.Instance == null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
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
            // Ground check with enhanced debugging
            Vector3 groundCheckPos = transform.position + Vector3.up * groundCheckRadius;
            isGrounded = Physics.CheckSphere(groundCheckPos, groundCheckRadius, groundMask);
            
            // Also check with a simple raycast as backup
            RaycastHit hit;
            bool raycastGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out hit, groundCheckDistance + 0.1f, groundMask);
            
            if (!isGrounded && raycastGrounded)
            {
                isGrounded = true;
            }
            
            // Detailed ground detection debugging
            if (debugGroundDetection && Time.frameCount % 120 == 0) // Every 2 seconds
            {
                int layerMaskValue = groundMask.value;
                // Debug.Log($"[FPSController] === Ground Detection Debug ===");
                // Debug.Log($"Player Position: {transform.position}");
                // Debug.Log($"Ground Check Position: {groundCheckPos}");
                // Debug.Log($"Ground Check Radius: {groundCheckRadius}");
                // Debug.Log($"Ground Mask Value: {layerMaskValue} (Binary: {System.Convert.ToString(layerMaskValue, 2)})");
                // Debug.Log($"Sphere Check Result: {isGrounded}");
                // Debug.Log($"Raycast Result: {raycastGrounded}");
                if (raycastGrounded)
                {
                    // Debug.Log($"Raycast Hit: {hit.collider.name} on layer {hit.collider.gameObject.layer}");
                }
                
                // Check what colliders are actually nearby
                Collider[] nearbyColliders = Physics.OverlapSphere(groundCheckPos, groundCheckRadius * 2f);
                // Debug.Log($"Nearby Colliders ({nearbyColliders.Length}): {string.Join(", ", System.Array.ConvertAll(nearbyColliders, c => $"{c.name}(L{c.gameObject.layer})"))}");
                // Debug.Log("================================");
            }

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
            Vector3 moveVector = move * targetSpeed * Time.deltaTime;
            controller.Move(moveVector);

            // Notify audio manager of movement state (optimized)
            if (audioManager != null)
            {
                if (moveVector.magnitude > 0.1f)
                {
                    // Player is moving - reduce audio manager calls for performance
                    if (Time.frameCount % 3 == 0) // Only call audio manager every 3 frames
                    {
                        audioManager.OnPlayerMoving(transform.position, running, crouching, isGrounded);
                    }
                    
                    // Reduce log frequency
                    if (Time.frameCount % 240 == 0) // Log once per 4 seconds
                    {
                        // Debug.Log($"[FPSController] Player moving - MoveVector: {moveVector.magnitude:F3}, Running: {running}, Crouching: {crouching}, Grounded: {isGrounded}");
                    }
                }
                else
                {
                    // Only call OnPlayerStopped occasionally to avoid spam
                    if (Time.frameCount % 10 == 0)
                    {
                        audioManager.OnPlayerStopped();
                    }
                    
                    if (Time.frameCount % 240 == 0)
                    {
                        // Debug.Log($"[FPSController] Player stopped - MoveVector: {moveVector.magnitude:F3}");
                    }
                }
            }
            else
            {
                // Log missing audio manager occasionally
                if (Time.frameCount % 300 == 0) // Every 5 seconds
                {
                    // Debug.LogWarning("[FPSController] Audio manager is null!");
                }
            }

            // Jump (with cooldown to prevent infinite jumping)
            bool canJump = isGrounded && !crouching && (Time.time - lastJumpTime >= jumpCooldown);
            
            if (jumping && canJump)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                lastJumpTime = Time.time;
                
                // Play jump sound
                if (audioManager != null)
                {
                    audioManager.OnPlayerJump();
                }
            }

            // Apply gravity with Y velocity damping to prevent bouncing
            velocity.y += gravity * Time.deltaTime;
            
            // Dampen Y velocity when in air (prevents infinite jumping exploit)
            if (!isGrounded)
            {
                velocity.y *= (1f - yVelocityDamping * Time.deltaTime);
            }
            
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

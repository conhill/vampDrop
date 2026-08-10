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

        [Header("Run Particles")]
        [Tooltip("Transform used to position running particles from the inspector")]
        public Transform runParticlesOrigin;
        public ParticleSystem runParticleSystem;

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

        [Header("Ground Detection State (Debug)")]
        [Tooltip("Detect")]
        public bool isGrounded = false;

        [Header("Runtime Debug")]
        [Tooltip("Shows whether the player pressed jump this frame (read/write at runtime)")]
        [SerializeField]
        private bool jumping = false;

        [Tooltip("Shows whether the raycast ground check is true this frame")]
        [SerializeField]
        private bool raycastGrounded = false;

        // Private variables
        private CharacterController controller;
        private float defaultHeight;
        private float crouchHeight;
        private Vector3 defaultCenter;   // saved in Start; used to anchor capsule bottom
        private Vector3 velocity;
        private float xRotation = 0f;
        private float smoothMouseX = 0f;
        private float smoothMouseY = 0f;
        //private bool isGrounded = false;
        private float lastJumpTime = -999f; // Track last jump time for cooldown

        void Start()
        {
            controller = GetComponent<CharacterController>();
            defaultHeight = controller.height;
            crouchHeight  = defaultHeight * 0.5f;
            defaultCenter = controller.center;

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
            if (StartupInstructionsOverlay.IsInstructionsActive)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            ProcessLook();
            ProcessMovement();

            // ESC — let EscapeMenuManager handle it when present; legacy unlock otherwise
            if (Input.GetKeyDown(KeyCode.Escape)
                && Vampire.EscapeMenuManager.Instance == null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
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
            // ── Ground detection ──────────────────────────────────────────────
            // Sphere placed at the bottom hemisphere centre of the capsule.
            // Formula keeps it valid even while the capsule is lerping during crouch.
            Vector3 capsuleBottomCenter = transform.position + controller.center
                + Vector3.down * (controller.height * 0.5f - controller.radius);

            isGrounded = Physics.CheckSphere(
                capsuleBottomCenter, controller.radius + 0.05f,
                groundMask, QueryTriggerInteraction.Ignore);

            // Fallback raycast — from capsule bottom downward a small extra distance
            if (!isGrounded)
            {
                float rayLen = controller.radius + groundCheckDistance;
                isGrounded = Physics.Raycast(
                    capsuleBottomCenter, Vector3.down, rayLen,
                    groundMask, QueryTriggerInteraction.Ignore);
            }

            // Final fallback: trust the CharacterController's own flag
            if (!isGrounded && controller.isGrounded)
                isGrounded = true;

            if (debugGroundDetection && Time.frameCount % 120 == 0)
                Debug.Log($"[FPSController] grounded={isGrounded}  capsuleBot={capsuleBottomCenter}");

            // Reset vertical velocity when grounded
            if (isGrounded && velocity.y < 0)
                velocity.y = -2f;

            // ── Input ─────────────────────────────────────────────────────────
            float moveX    = Input.GetAxis("Horizontal");
            float moveZ    = Input.GetAxis("Vertical");
            bool  running  = Input.GetKey(KeyCode.LeftShift);
            bool  crouching = Input.GetKey(KeyCode.LeftControl);
            jumping = Input.GetKeyDown(KeyCode.Space);

            Vector3 move = transform.right * moveX + transform.forward * moveZ;
            if (move.magnitude > 1f) move = move.normalized;

            float targetSpeed = crouching ? crouchSpeed
                              : (running && isGrounded) ? runSpeed
                              : walkSpeed;

            // ── Crouch / stand ────────────────────────────────────────────────
            float targetHeight = crouching ? crouchHeight : defaultHeight;
            if (!Mathf.Approximately(controller.height, targetHeight))
            {
                controller.height = Mathf.Lerp(controller.height, targetHeight, 8f * Time.deltaTime);

                // Shift the capsule centre so the BOTTOM stays at the same world height.
                // This keeps feet on the ground; the top of the capsule (head) rises/falls.
                //   derivation: defaultCenter.y - defaultHeight/2 == newCenter.y - newHeight/2
                //               newCenter.y = defaultCenter.y + (newHeight - defaultHeight) * 0.5f
                float newCenterY = defaultCenter.y + (controller.height - defaultHeight) * 0.5f;
                controller.center = new Vector3(defaultCenter.x, newCenterY, defaultCenter.z);

                // Camera: 85 % of the way from the capsule bottom to the capsule top (local space)
                float capsuleBottomLocal = controller.center.y - controller.height * 0.5f;
                float camLocalY = capsuleBottomLocal + controller.height * 0.85f;
                cameraTransform.localPosition = new Vector3(
                    cameraTransform.localPosition.x, camLocalY,
                    cameraTransform.localPosition.z);
            }

            // ── Movement ──────────────────────────────────────────────────────
            controller.Move(move * targetSpeed * Time.deltaTime);

            // Audio
            bool isMoving = move.magnitude > 0.01f;
            if (audioManager != null)
            {
                if (isMoving  && Time.frameCount % 3  == 0) audioManager.OnPlayerMoving(transform.position, running, crouching, isGrounded);
                if (!isMoving && Time.frameCount % 10 == 0) audioManager.OnPlayerStopped();
            }

            // ── Jump ──────────────────────────────────────────────────────────
            if (jumping && isGrounded && !crouching && Time.time - lastJumpTime >= jumpCooldown)
            {
                velocity.y    = Mathf.Sqrt(jumpHeight * -2f * gravity);
                lastJumpTime  = Time.time;
                if (audioManager != null) audioManager.OnPlayerJump();
            }

            // ── Gravity ───────────────────────────────────────────────────────
            velocity.y += gravity * Time.deltaTime;
            if (!isGrounded)
                velocity.y *= (1f - yVelocityDamping * Time.deltaTime);

            controller.Move(velocity * Time.deltaTime);
        }

        void UpdateRunParticles(bool shouldEmit)
        {
            if (runParticleSystem == null)
            {
                return;
            }

            if (runParticlesOrigin != null)
            {
                runParticleSystem.transform.position = runParticlesOrigin.position;
            }

            if (shouldEmit)
            {
                if (!runParticleSystem.isPlaying)
                {
                    runParticleSystem.Play();
                }
            }
            else
            {
                if (runParticleSystem.isPlaying)
                {
                    runParticleSystem.Stop();
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (controller != null)
            {
                Vector3 bottomCenter = transform.position + controller.center - Vector3.up * (controller.height * 0.5f);
                Vector3 groundCheckPos = bottomCenter + Vector3.up * groundCheckDistance;
                Gizmos.color = isGrounded ? Color.blue : Color.red;
                Gizmos.DrawWireSphere(groundCheckPos, groundCheckRadius);
            }
        }
    }
}

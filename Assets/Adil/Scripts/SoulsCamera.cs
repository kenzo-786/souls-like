using UnityEngine;
using UnityEngine.InputSystem;

public class SoulsCamera : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target; // Assign the "CameraPivot" object here
    public float smoothSpeed = 0.1f; // Lower = smoother follow (laggy), Higher = tighter

    [Header("Orbit Settings")]
    public float lookSensitivity = 2f;
    public float verticalMin = -40f;
    public float verticalMax = 60f;

    [Header("Collision Settings")]
    public LayerMask collisionLayers; // Set this to "Default" or "Environment"
    public float maxDistance = 3.5f; // Normal distance
    public float minDistance = 0.5f; // Closest zoom
    public float cameraRadius = 0.2f; // For SphereCast

    // Internal State
    private float _currentX = 0f;
    private float _currentY = 0f;
    private Vector3 _currentVelocity;
    private float _finalDistance;

    void Start()
    {
        // Lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize angles
        _currentX = transform.eulerAngles.y;
        _currentY = 20f; // Start with a slight downward angle
        _finalDistance = maxDistance;
    }

    void LateUpdate()
    {
        if (target == null) return;

        HandleInput();

        // 1. Calculate Rotation
        Quaternion rotation = Quaternion.Euler(_currentY, _currentX, 0);

        // 2. Calculate Desired Position (Before Collision)
        // We calculate where the camera WANTS to be based on the Max Distance
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -maxDistance);
        Vector3 desiredPosition = rotation * negDistance + target.position;

        // 3. Handle Wall Collision (The "Souls" Zoom)
        // We cast a sphere from the target BACKWARDS towards the camera
        Vector3 directionToCamera = desiredPosition - target.position;
        float distToCamera = directionToCamera.magnitude;

        RaycastHit hit;
        if (Physics.SphereCast(target.position, cameraRadius, directionToCamera.normalized, out hit, maxDistance, collisionLayers))
        {
            // If we hit a wall, set distance to the hit point minus a small buffer
            _finalDistance = Mathf.Clamp(hit.distance, minDistance, maxDistance);
        }
        else
        {
            // No wall, reset to max distance smoothly
            _finalDistance = Mathf.Lerp(_finalDistance, maxDistance, 10f * Time.deltaTime);
        }

        // 4. Apply Final Position
        Vector3 finalNegDistance = new Vector3(0.0f, 0.0f, -_finalDistance);
        Vector3 finalPosition = rotation * finalNegDistance + target.position;

        // Smoothly dampen the movement to remove jitter
        transform.position = Vector3.SmoothDamp(transform.position, finalPosition, ref _currentVelocity, smoothSpeed);
        transform.LookAt(target);
    }

    void HandleInput()
    {
        float inputX = 0;
        float inputY = 0;

        // 1. Gamepad Input
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.magnitude > 0.1f) // Deadzone check
            {
                inputX += stick.x * lookSensitivity * 100f * Time.deltaTime;
                inputY += stick.y * lookSensitivity * 100f * Time.deltaTime;
            }
        }

        // 2. Mouse Input (New Input System specific)
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            // Mouse delta is raw pixels, so we use a smaller multiplier
            inputX += delta.x * lookSensitivity * 0.05f;
            inputY += delta.y * lookSensitivity * 0.05f;
        }

        _currentX += inputX;
        _currentY -= inputY; // Invert Y is standard for coding

        // Clamp looking up/down
        _currentY = Mathf.Clamp(_currentY, verticalMin, verticalMax);
    }
}

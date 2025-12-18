using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class EldenMovement : MonoBehaviour
{
    [Header("Movement Stats")]
    public float walkSpeed = 2f;
    public float runSpeed = 5.5f;
    public float sprintSpeed = 8f;

    [Header("Roll Stats")]
    public bool useRootMotionRoll = false;
    public float rollDuration = 1.0f;
    [Tooltip("Only used if Root Motion is OFF")]
    public float rollSpeed = 10f;
    [Tooltip("Only used if Root Motion is OFF")]
    public AnimationCurve rollSpeedCurve;

    [Header("Roll Steering")]
    [Tooltip("How much you can turn while rolling (0 = None, 1 = Slow, 5 = Fast)")]
    public float rollRotationSpeed = 2.0f;

    [Header("Roll Tweaks")]
    [Tooltip("Target height ratio during roll (0.5 = Half height)")]
    public float rollColliderHeightRatio = 0.5f;
    [Tooltip("Tries to expand radius to this multiplier. Will be capped automatically to fit height.")]
    public float rollRadiusMultiplier = 1.2f; // Reduced default to be safer

    [Tooltip("Assign your Child Mesh Object (e.g. 'Vrun') here. We lift THIS object visually.")]
    public Transform playerModel;
    [Tooltip("How high to visually lift the mesh during roll.")]
    public float rollVisualLift = 0.15f;

    [Header("Responsiveness")]
    public float acceleration = 20f;
    public float deceleration = 40f;
    public float rotationSpeed = 12f;

    [Header("Gravity & Ground")]
    public float gravity = -20f;
    public float groundedGravity = -0.5f;
    public float groundedGracePeriod = 0.2f;

    [Header("References")]
    public Transform cameraTransform;
    public Animator animator;

    private CharacterController _controller;
    private Vector3 _currentVelocity;
    private Vector3 _targetVelocity;
    private Vector3 _verticalVelocity;
    private bool _isGrounded;
    private float _lastGroundedTime;
    private bool _isSprinting;
    private bool _isWalking;
    private float _inputMagnitude;

    // Roll State
    private bool _isRolling = false;
    private bool _rollQueued = false;
    private float _rollBufferTimer;
    private float _originalHeight;
    private float _originalCenterY;
    private float _originalRadius;

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        if (cameraTransform == null) cameraTransform = Camera.main.transform;

        _originalHeight = _controller.height;
        _originalCenterY = _controller.center.y;
        _originalRadius = _controller.radius;

        if (playerModel == null)
        {
            foreach (Transform child in transform)
            {
                if (child.GetComponentInChildren<SkinnedMeshRenderer>())
                {
                    playerModel = child;
                    Debug.Log($"Auto-Assigned Player Model to: {child.name}");
                    break;
                }
            }
        }

        if (rollSpeedCurve == null || rollSpeedCurve.length == 0)
        {
            rollSpeedCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 0.5f), new Keyframe(1, 0.0f));
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleGravity();
        HandleInput();

        if (!_isRolling)
        {
            HandleMovement();
            HandleRotation();
            HandleAnimation();
        }
    }

    void OnAnimatorMove()
    {
        if (_isRolling && useRootMotionRoll)
        {
            Vector3 rootMotion = animator.deltaPosition;
            rootMotion.y = _verticalVelocity.y * Time.deltaTime;
            _controller.Move(rootMotion);
        }
    }

    void HandleInput()
    {
        Vector2 input = Vector2.zero;
        _isSprinting = false;
        _isWalking = false;
        bool rollPressed = false;

        if (Gamepad.current != null)
        {
            input = Gamepad.current.leftStick.ReadValue();
            _isSprinting = Gamepad.current.buttonEast.isPressed;
            if (input.magnitude > 0.1f && input.magnitude < 0.6f) _isWalking = true;
            rollPressed = Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.buttonEast.wasPressedThisFrame;
        }
        else if (Keyboard.current != null)
        {
            var kb = Keyboard.current;
            if (kb.wKey.isPressed) input.y += 1;
            if (kb.sKey.isPressed) input.y -= 1;
            if (kb.aKey.isPressed) input.x -= 1;
            if (kb.dKey.isPressed) input.x += 1;
            input = input.normalized;

            _isSprinting = kb.leftShiftKey.isPressed;
            _isWalking = kb.leftCtrlKey.isPressed;
            rollPressed = kb.spaceKey.wasPressedThisFrame;
        }

        _inputMagnitude = input.magnitude;

        if (rollPressed) _rollBufferTimer = 0.2f;

        if (_rollBufferTimer > 0)
        {
            _rollBufferTimer -= Time.deltaTime;
            bool canRoll = _isGrounded || (Time.time - _lastGroundedTime < groundedGracePeriod);

            if (!_isRolling && canRoll)
            {
                StartCoroutine(RollRoutine());
                _rollBufferTimer = 0;
            }
            else if (_isRolling)
            {
                _rollQueued = true;
                _rollBufferTimer = 0;
            }
        }
    }

    void HandleMovement()
    {
        float targetSpeed = 0f;
        if (_inputMagnitude > 0.1f)
        {
            if (_isSprinting) targetSpeed = sprintSpeed;
            else if (_isWalking) targetSpeed = walkSpeed;
            else targetSpeed = runSpeed;
        }

        Vector3 moveDir = GetCameraRelativeInput();
        _targetVelocity = moveDir * targetSpeed;
        float accelRate = (_inputMagnitude > 0.1f) ? acceleration : deceleration;
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, _targetVelocity, accelRate * Time.deltaTime);
        _controller.Move(_currentVelocity * Time.deltaTime);
    }

    Vector3 GetCameraRelativeInput()
    {
        if (_inputMagnitude <= 0.1f) return Vector3.zero;
        Vector3 camFwd = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camFwd.y = 0;
        camRight.y = 0;
        camFwd.Normalize();
        camRight.Normalize();
        return (camFwd * inputY() + camRight * inputX()).normalized;
    }

    float inputX()
    {
        if (Gamepad.current != null) return Gamepad.current.leftStick.ReadValue().x;
        if (Keyboard.current != null) return (Keyboard.current.dKey.isPressed ? 1 : 0) - (Keyboard.current.aKey.isPressed ? 1 : 0);
        return 0;
    }

    float inputY()
    {
        if (Gamepad.current != null) return Gamepad.current.leftStick.ReadValue().y;
        if (Keyboard.current != null) return (Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0);
        return 0;
    }

    IEnumerator RollRoutine()
    {
        _isRolling = true;
        animator.applyRootMotion = useRootMotionRoll;

        // --- SMART COLLIDER RESIZE (Fixes Jitter) ---

        // 1. Calculate new safe height
        float newHeight = _originalHeight * rollColliderHeightRatio;

        // 2. Calculate ideal radius
        float targetRadius = _originalRadius * rollRadiusMultiplier;
        // CAP the radius: Radius cannot be more than half the height (Geometrically impossible for Capsule)
        float maxSafeRadius = newHeight * 0.5f;
        float newRadius = Mathf.Min(targetRadius, maxSafeRadius); // Use the smaller valid one

        // 3. Calculate new Center Y so the feet stay exactly on the floor
        // Formula: NewCenter = FeetY + (NewHeight / 2)
        // Original FeetY = OriginalCenter - (OriginalHeight / 2)
        float feetY = _originalCenterY - (_originalHeight * 0.5f);
        float newCenterY = feetY + (newHeight * 0.5f);

        // Apply Physics Changes
        _controller.height = newHeight;
        _controller.center = new Vector3(0, newCenterY, 0);
        _controller.radius = newRadius;

        // Apply Visual Lift
        Vector3 originalModelPos = Vector3.zero;
        if (playerModel != null)
        {
            originalModelPos = playerModel.localPosition;
            playerModel.localPosition += Vector3.up * rollVisualLift;
        }

        do
        {
            _rollQueued = false;
            animator.SetTrigger("Roll");

            if (_inputMagnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(GetCameraRelativeInput());
            }

            float timer = 0f;
            while (timer < rollDuration)
            {
                if (_rollQueued && timer > rollDuration * 0.75f) break;

                if (_inputMagnitude > 0.1f && rollRotationSpeed > 0)
                {
                    Vector3 targetDir = GetCameraRelativeInput();
                    Quaternion lookRot = Quaternion.LookRotation(targetDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rollRotationSpeed * Time.deltaTime);
                }

                if (!useRootMotionRoll)
                {
                    float normalizedTime = timer / rollDuration;
                    float curveMod = rollSpeedCurve.Evaluate(normalizedTime);
                    _controller.Move(transform.forward * rollSpeed * curveMod * Time.deltaTime);
                }

                timer += Time.deltaTime;
                yield return null;
            }

        } while (_rollQueued);

        // Restore Collider
        _controller.height = _originalHeight;
        _controller.center = new Vector3(0, _originalCenterY, 0);
        _controller.radius = _originalRadius;

        // Restore Visuals
        if (playerModel != null)
        {
            playerModel.localPosition = originalModelPos;
        }

        _isRolling = false;
        animator.applyRootMotion = false;
    }

    void HandleRotation()
    {
        if (_currentVelocity.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(_currentVelocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    void HandleGravity()
    {
        _isGrounded = _controller.isGrounded;
        if (_isGrounded)
        {
            _lastGroundedTime = Time.time;
            if (_verticalVelocity.y < 0) _verticalVelocity.y = groundedGravity;
        }
        _verticalVelocity.y += gravity * Time.deltaTime;
        _controller.Move(_verticalVelocity * Time.deltaTime);
    }

    void HandleAnimation()
    {
        float currentSpeed = _currentVelocity.magnitude;
        float animValue = 0f;
        if (currentSpeed > 0.1f)
        {
            if (currentSpeed <= walkSpeed + 0.5f) animValue = 0.5f;
            else if (currentSpeed <= runSpeed + 0.5f) animValue = 1.0f;
            else animValue = 1.5f;
        }
        animator.SetFloat("Speed", animValue, 0.15f, Time.deltaTime);
    }
}






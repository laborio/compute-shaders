using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class SimpleFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float fastMoveMultiplier = 2.5f;
    [SerializeField] private float verticalSpeed = 4f;

    [Header("Rotation")]
    [SerializeField] private float yawSpeed = 100f;
    [SerializeField] private float pitchSpeed = 80f;
    [SerializeField] private Vector2 pitchLimits = new(-80f, 80f);

    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        Vector3 euler = transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = NormalizePitch(euler.x);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        UpdateRotation(deltaTime);
        UpdateMovement(deltaTime);
    }

    private void UpdateRotation(float deltaTime)
    {
        float yawInput = 0f;
        if (GetKey(KeyCode.LeftArrow))
        {
            yawInput -= 1f;
        }
        if (GetKey(KeyCode.RightArrow))
        {
            yawInput += 1f;
        }

        float pitchInput = 0f;
        if (GetKey(KeyCode.UpArrow))
        {
            pitchInput += 1f;
        }
        if (GetKey(KeyCode.DownArrow))
        {
            pitchInput -= 1f;
        }

        yaw += yawInput * yawSpeed * deltaTime;
        pitch = Mathf.Clamp(pitch + pitchInput * pitchSpeed * deltaTime, pitchLimits.x, pitchLimits.y);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement(float deltaTime)
    {
        Vector3 planarInput = Vector3.zero;
        if (GetKey(KeyCode.W))
        {
            planarInput += Vector3.forward;
        }
        if (GetKey(KeyCode.S))
        {
            planarInput += Vector3.back;
        }
        if (GetKey(KeyCode.A))
        {
            planarInput += Vector3.left;
        }
        if (GetKey(KeyCode.D))
        {
            planarInput += Vector3.right;
        }

        float speed = moveSpeed;
        if (GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift))
        {
            speed *= fastMoveMultiplier;
        }

        Vector3 movement = transform.TransformDirection(planarInput.normalized) * (speed * deltaTime);

        float verticalInput = 0f;
        if (GetKey(KeyCode.Q))
        {
            verticalInput += 1f;
        }
        if (GetKey(KeyCode.E))
        {
            verticalInput -= 1f;
        }

        movement += Vector3.up * (verticalInput * verticalSpeed * deltaTime);
        transform.position += movement;
    }

    private static bool GetKey(KeyCode primary, KeyCode alternate)
    {
        return GetKey(primary) || GetKey(alternate);
    }

    private static bool GetKey(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        Key inputKey = key switch
        {
            KeyCode.LeftArrow => Key.LeftArrow,
            KeyCode.RightArrow => Key.RightArrow,
            KeyCode.UpArrow => Key.UpArrow,
            KeyCode.DownArrow => Key.DownArrow,
            KeyCode.LeftShift => Key.LeftShift,
            KeyCode.RightShift => Key.RightShift,
            KeyCode.Z => Key.Z,
            KeyCode.W => Key.W,
            KeyCode.Q => Key.Q,
            KeyCode.E => Key.E,
            KeyCode.A => Key.A,
            KeyCode.S => Key.S,
            KeyCode.D => Key.D,
            KeyCode.R => Key.R,
            KeyCode.F => Key.F,
            _ => Key.None,
        };

        return inputKey != Key.None && keyboard[inputKey].isPressed;
#else
        return Input.GetKey(key);
#endif
    }

    private static float NormalizePitch(float degrees)
    {
        if (degrees > 180f)
        {
            degrees -= 360f;
        }

        return degrees;
    }
}

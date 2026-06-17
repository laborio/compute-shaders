using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class BallClickMover : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform pitchRoot;
    [SerializeField] private Renderer pitchRenderer;
    [SerializeField] private Collider pitchCollider;

    [Header("Movement")]
    [SerializeField] private float fixedY = 0.1f;
    [SerializeField] private float maxSpeed = 10f;
    [SerializeField] private float acceleration = 35f;
    [SerializeField] private float deceleration = 18f;
    [SerializeField] private float decelerationFraction = 0.15f;
    [SerializeField] private float arrivalThreshold = 0.02f;

    private Vector3 targetPosition;
    private float currentSpeed;
    private float travelDistance;
    private bool hasTarget;

    private void Awake()
    {
        ResolveReferences();
        Vector3 position = transform.position;
        position.y = fixedY;
        transform.position = position;
        targetPosition = position;
    }

    private void OnValidate()
    {
        fixedY = Mathf.Max(0f, fixedY);
        maxSpeed = Mathf.Max(0.1f, maxSpeed);
        acceleration = Mathf.Max(0.1f, acceleration);
        deceleration = Mathf.Max(0.1f, deceleration);
        decelerationFraction = Mathf.Clamp(decelerationFraction, 0.05f, 0.9f);
        arrivalThreshold = Mathf.Max(0.001f, arrivalThreshold);
    }

    private void Update()
    {
        ResolveReferences();

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUi())
        {
            TrySetTargetFromPointer(Mouse.current.position.ReadValue());
        }
#endif

        UpdateMovement(Time.deltaTime);
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (pitchRoot == null)
        {
            GameObject pitchObject = GameObject.Find("Pitch_Football");
            if (pitchObject != null)
            {
                pitchRoot = pitchObject.transform;
            }
        }

        if (pitchRenderer == null && pitchRoot != null)
        {
            pitchRenderer = pitchRoot.GetComponentInChildren<Renderer>(true);
        }

        if (pitchCollider == null && pitchRoot != null)
        {
            pitchCollider = pitchRoot.GetComponentInChildren<Collider>(true);
        }
    }

    private void TrySetTargetFromPointer(Vector2 screenPosition)
    {
        if (targetCamera == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        if (!TryGetPitchHit(ray, out Vector3 hitPoint))
        {
            return;
        }

        Vector3 current = transform.position;
        targetPosition = new Vector3(hitPoint.x, fixedY, hitPoint.z);
        targetPosition.y = fixedY;
        travelDistance = Vector2.Distance(new Vector2(current.x, current.z), new Vector2(targetPosition.x, targetPosition.z));
        hasTarget = travelDistance > arrivalThreshold;
    }

    private bool TryGetPitchHit(Ray ray, out Vector3 hitPoint)
    {
        if (pitchCollider != null && pitchCollider.Raycast(ray, out RaycastHit hit, 500f))
        {
            hitPoint = hit.point;
            hitPoint.y = fixedY;
            return true;
        }

        Plane groundPlane = new(Vector3.up, new Vector3(0f, pitchRoot != null ? pitchRoot.position.y : 0f, 0f));
        if (!groundPlane.Raycast(ray, out float enter))
        {
            hitPoint = default;
            return false;
        }

        hitPoint = ray.GetPoint(enter);
        if (!IsInsidePitchBounds(hitPoint))
        {
            return false;
        }

        hitPoint.y = fixedY;
        return true;
    }

    private bool IsInsidePitchBounds(Vector3 worldPoint)
    {
        if (pitchRenderer == null)
        {
            return true;
        }

        Bounds bounds = pitchRenderer.bounds;
        return worldPoint.x >= bounds.min.x &&
               worldPoint.x <= bounds.max.x &&
               worldPoint.z >= bounds.min.z &&
               worldPoint.z <= bounds.max.z;
    }

    private void UpdateMovement(float deltaTime)
    {
        Vector3 position = transform.position;
        position.y = fixedY;
        transform.position = position;

        if (!hasTarget)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * deltaTime);
            return;
        }

        Vector2 currentXZ = new(position.x, position.z);
        Vector2 targetXZ = new(targetPosition.x, targetPosition.z);
        Vector2 toTarget = targetXZ - currentXZ;
        float remaining = toTarget.magnitude;
        if (remaining <= arrivalThreshold)
        {
            transform.position = targetPosition;
            currentSpeed = 0f;
            hasTarget = false;
            return;
        }

        float decelDistance = Mathf.Max(travelDistance * decelerationFraction, arrivalThreshold * 2f);
        float desiredSpeed = maxSpeed;
        float speedChange = acceleration;
        if (remaining <= decelDistance)
        {
            float normalized = Mathf.Clamp01(remaining / decelDistance);
            desiredSpeed = maxSpeed * normalized;
            speedChange = deceleration;
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, speedChange * deltaTime);
        currentSpeed = Mathf.Min(currentSpeed, maxSpeed);

        float step = Mathf.Min(remaining, currentSpeed * deltaTime);
        Vector2 nextXZ = currentXZ + (toTarget / remaining) * step;
        transform.position = new Vector3(nextXZ.x, fixedY, nextXZ.y);
    }

#if ENABLE_INPUT_SYSTEM
    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
#endif
}

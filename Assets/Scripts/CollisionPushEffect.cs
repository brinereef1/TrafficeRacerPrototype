
using UnityEngine;

public class CollisionPushEffect : MonoBehaviour
{
    [Header("Impact Animation")]
    [SerializeField] private float pushDistance = 1.2f;
    [SerializeField] private float pushDuration = 0.45f;
    [SerializeField] private float tiltAngle = 8f;

    private float impactLateralOffset;
    private float impactLateralVelocity;
    private float impactRoll;
    private float impactYaw;

    private Vector3 startOffset;
    private float startTilt;
    private float elapsed;
    private bool isPushing;

    public void ApplyPush(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            direction = -transform.forward;

        direction.Normalize();

        startOffset = direction * Mathf.Max(0f, pushDistance);

        // Tilt toward the side of the impact.
        float side = Vector3.Dot(direction, transform.right);
        startTilt = -Mathf.Clamp(side, -1f, 1f) * tiltAngle;

        elapsed = 0f;
        isPushing = true;
    }

    private void LateUpdate()
    {
        if (!isPushing)
            return;

        elapsed += Time.deltaTime;

        float duration = Mathf.Max(0.01f, pushDuration);
        float progress = Mathf.Clamp01(elapsed / duration);

        // Smooth ease-out from the impact toward the normal pose.
        float easedProgress =
            1f - Mathf.Pow(1f - progress, 3f);

        Vector3 currentOffset = Vector3.Lerp(
            startOffset,
            Vector3.zero,
            easedProgress
        );

        float currentTilt = Mathf.Lerp(
            startTilt,
            0f,
            easedProgress
        );

        // PlayerController and TrafficVehicle set the base pose
        // in Update. Apply the temporary impact in LateUpdate.
        Vector3 basePosition = transform.position;
        Quaternion baseRotation = transform.rotation;

        transform.SetPositionAndRotation(
            basePosition + currentOffset,
            baseRotation * Quaternion.Euler(0f, 0f, currentTilt)
        );

        if (progress >= 1f)
            isPushing = false;
    }
}
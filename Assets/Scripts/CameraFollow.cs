
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Position")]
    [SerializeField] private Vector3 offset =
        new Vector3(0f, 5f, -9f);

    [Header("Camera Settings")]
    [SerializeField] private float positionSmoothTime = 0.18f;
    [SerializeField] private float rotationSmoothSpeed = 8f;
    [SerializeField] private float lookAheadDistance = 10f;
    [SerializeField] private float lookHeight = 1.5f;

    private Vector3 smoothVelocity;

    private void LateUpdate()
    {
        if (target == null)
            return;

        // Keep the camera behind and above the player.
        Vector3 desiredPosition =
            target.TransformPoint(offset);

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref smoothVelocity,
            positionSmoothTime
        );

        // Look toward the road ahead.
        Vector3 lookTarget =
            target.position
            + target.forward * lookAheadDistance
            + Vector3.up * lookHeight;

        Vector3 direction =
            lookTarget - transform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(direction, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationSmoothSpeed * Time.deltaTime
            );
        }
    }
}

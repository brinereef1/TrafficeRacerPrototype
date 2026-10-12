
using UnityEngine;

public class MeasureObjects : MonoBehaviour
{
    [ContextMenu("Measure Object")]
    private void MeasureObject()
    {
        Renderer[] renderers =
            GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            Debug.LogWarning(
                "No Renderer found on " + gameObject.name,
                gameObject
            );
            return;
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Debug.Log(
            $"{gameObject.name} dimensions: {bounds.size}",
            gameObject
        );
    }
}

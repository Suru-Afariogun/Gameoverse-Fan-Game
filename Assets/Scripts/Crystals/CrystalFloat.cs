using UnityEngine;

/// <summary>
/// Gentle idle bob for spawn / enemy crystals (not collectable shards).
/// </summary>
public class CrystalFloat : MonoBehaviour
{
    [Tooltip("Total peak-to-peak vertical travel in world units.")]
    [SerializeField] private float floatRadius = 2f;
    [SerializeField] private float floatSpeed = 1.25f;
    [Tooltip("Optional phase offset so nearby crystals do not move in perfect sync.")]
    [SerializeField] private float phaseOffset;

    private Vector3 restPosition;
    private float phase;

    private void Awake()
    {
        restPosition = transform.position;
        phase = phaseOffset;
    }

    private void OnEnable()
    {
        if (restPosition == Vector3.zero && transform.position != Vector3.zero)
            restPosition = transform.position;
    }

    private void Update()
    {
        phase += Time.deltaTime * floatSpeed;
        float y = restPosition.y + Mathf.Sin(phase) * (floatRadius * 0.5f);
        transform.position = new Vector3(restPosition.x, y, restPosition.z);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        floatRadius = Mathf.Max(0f, floatRadius);
        floatSpeed = Mathf.Max(0.01f, floatSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? restPosition : transform.position;
        Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.35f);
        Gizmos.DrawWireSphere(center, floatRadius * 0.5f);
    }
#endif
}

using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Boss room sliding door. Opens when the player passes the enter box left-to-right,
/// closes when they pass the close box left-to-right.
/// </summary>
public class BossRoomDoor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform door;
    [SerializeField] private BossRoomPassDetector enterDetector;
    [SerializeField] private BossRoomPassDetector closeDetector;

    [Header("Auto Bind")]
    [Tooltip("Child names searched under the door transform when detectors are not assigned.")]
    [SerializeField] private string enterDetectorName = "Enter";
    [SerializeField] private string closeDetectorName = "Close";

    [Header("Slide")]
    [SerializeField] private float slideDistance = 6f;
    [SerializeField] private float slideSpeed = 8f;

    private Vector3 closedLocalPosition;
    private Coroutine slideRoutine;
    private bool isOpen;

    public event Action DoorClosed;
    public event Action DoorOpened;

    public bool IsOpen => isOpen;
    public BossRoomPassDetector EnterDetector => enterDetector;
    public BossRoomPassDetector CloseDetector => closeDetector;

    /// <summary>Re-open the door and reset pass detectors (e.g. after dying in the boss room).</summary>
    public void ResetToOpen()
    {
        if (slideRoutine != null)
        {
            StopCoroutine(slideRoutine);
            slideRoutine = null;
        }

        isOpen = true;
        if (door != null)
            door.localPosition = closedLocalPosition + Vector3.up * slideDistance;

        enterDetector?.ResetDetector();
        closeDetector?.ResetDetector();
    }

    private void Awake()
    {
        if (door == null)
            door = transform;

        closedLocalPosition = door.localPosition;
        EnsureDetectorsReady();
    }

    private void Start()
    {
        EnsureDetectorsReady();
    }

    private void OnEnable()
    {
        SubscribeDetectors();
    }

    private void OnDisable()
    {
        UnsubscribeDetectors();
    }

    /// <summary>
    /// Finds Enter/Close children, adds pass detectors if needed, and wires subscriptions.
    /// </summary>
    public void EnsureDetectorsReady()
    {
        if (enterDetector == null)
            enterDetector = FindOrCreateDetector(enterDetectorName);

        if (closeDetector == null)
            closeDetector = FindOrCreateDetector(closeDetectorName);

        EnsureLeftToRightDetectorOrder();
        ConfigureDetectorCrossingEdges();
        SubscribeDetectors();
    }

    private void ConfigureDetectorCrossingEdges()
    {
        if (enterDetector != null)
            enterDetector.SetUseLeftWorldEdge(true);

        if (closeDetector != null)
            closeDetector.SetUseLeftWorldEdge(false);
    }

    /// <summary>
    /// Enter must be the first line crossed when walking left-to-right (lower world X).
    /// The boss room door is rotated in Level one, so serialized Enter/Close children can be reversed.
    /// </summary>
    private void EnsureLeftToRightDetectorOrder()
    {
        if (enterDetector == null || closeDetector == null || ReferenceEquals(enterDetector, closeDetector))
            return;

        float enterX = enterDetector.GetCrossingX();
        float closeX = closeDetector.GetCrossingX();
        if (enterX <= closeX)
            return;

        BossRoomPassDetector swap = enterDetector;
        enterDetector = closeDetector;
        closeDetector = swap;
    }

    private BossRoomPassDetector FindOrCreateDetector(string childName)
    {
        if (door == null || string.IsNullOrWhiteSpace(childName))
            return null;

        Transform child = door.Find(childName);
        if (child == null)
        {
            Debug.LogWarning($"[BossRoomDoor] Could not find detector child '{childName}' under '{door.name}'.", this);
            return null;
        }

        BossRoomPassDetector detector = child.GetComponent<BossRoomPassDetector>();
        if (detector == null)
            detector = child.gameObject.AddComponent<BossRoomPassDetector>();

        return detector;
    }

    private void SubscribeDetectors()
    {
        if (enterDetector != null)
        {
            enterDetector.PassedLeftToRight -= HandleEnterPassed;
            enterDetector.PassedLeftToRight += HandleEnterPassed;
        }

        if (closeDetector != null)
        {
            closeDetector.PassedLeftToRight -= HandleClosePassed;
            closeDetector.PassedLeftToRight += HandleClosePassed;
        }
    }

    private void UnsubscribeDetectors()
    {
        if (enterDetector != null)
            enterDetector.PassedLeftToRight -= HandleEnterPassed;

        if (closeDetector != null)
            closeDetector.PassedLeftToRight -= HandleClosePassed;
    }

    private void HandleEnterPassed()
    {
        if (isOpen)
            return;

        isOpen = true;
        closeDetector?.ResetDetector();
        SlideTo(closedLocalPosition + Vector3.up * slideDistance, opening: true);
    }

    private void HandleClosePassed()
    {
        if (!isOpen)
            return;

        isOpen = false;
        DoorClosed?.Invoke();
        SlideTo(closedLocalPosition, opening: false);
    }

    private void SlideTo(Vector3 targetLocalPosition, bool opening)
    {
        if (slideRoutine != null)
            StopCoroutine(slideRoutine);

        slideRoutine = StartCoroutine(SlideRoutine(targetLocalPosition, opening));
    }

    private IEnumerator SlideRoutine(Vector3 targetLocalPosition, bool opening)
    {
        if (door == null)
            yield break;

        float speed = Mathf.Max(0.01f, slideSpeed);
        while (Vector3.Distance(door.localPosition, targetLocalPosition) > 0.01f)
        {
            door.localPosition = Vector3.MoveTowards(door.localPosition, targetLocalPosition, speed * Time.deltaTime);
            yield return null;
        }

        door.localPosition = targetLocalPosition;
        slideRoutine = null;

        if (opening)
            DoorOpened?.Invoke();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        slideDistance = Mathf.Max(0f, slideDistance);
        slideSpeed = Mathf.Max(0.01f, slideSpeed);

        if (door == null)
            door = transform;
    }
#endif
}

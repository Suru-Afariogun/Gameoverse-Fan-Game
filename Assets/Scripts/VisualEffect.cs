using UnityEngine;

/// <summary>
/// Categories of visual effect prefabs. Set the matching type on each prefab in the Inspector
/// (same workflow as Projectile Shot Type on projectile prefabs).
/// </summary>
public enum VisualEffectType
{
    BusterBlast,
    DefaultStunned,
    DeathEnergyBall
}

/// <summary>
/// Attach to a VFX prefab and choose Effect Type in the Inspector to match what you built.
/// Characters/bosses reference these prefabs directly (like Kit's projectile prefab slots).
/// </summary>
public class VisualEffect : MonoBehaviour
{
    [Header("Effect Type")]
    [Tooltip("Choose the type that matches this prefab: Buster Blast, Default Stunned, or Death Energy Ball.")]
    [SerializeField] private VisualEffectType effectType = VisualEffectType.BusterBlast;

    [Header("Buster Blast")]
    [Tooltip("Fully faded once the linked shot has traveled this far (world units).")]
    [SerializeField] private float maxTravelDistance = 2f;
    [Tooltip("Fully faded this many seconds after spawn (whichever comes first with travel).")]
    [SerializeField] private float maxLifetime = 0.3f;
    [Tooltip("Local scale grows from 1 to this value while fading.")]
    [SerializeField] private float maxEndScale = 1.5f;
    [Tooltip("Pink tint applied to Buster Blast sprites (matches Kit's charge aura).")]
    [SerializeField] private Color blastColor = new Color(1f, 0.45f, 0.75f, 1f);
    [SerializeField] private bool tintBlastSprites = true;

    [Header("Default Stunned - Animator")]
    [Tooltip("Bool parameter on the stunned effect Animator (same as your yellow/red controllers).")]
    [SerializeField] private string isStunnedBoolParameter = "IsStunned";
    [Tooltip("Trigger parameter that plays stunned yellow / stunned red on the effect Animator.")]
    [SerializeField] private string stunnedTriggerParameter = "stunned";
    [Tooltip("Draw this many sorting-order steps above the host sprite(s).")]
    [SerializeField] private int sortOrderInFrontOfHost = 1;

    [Header("Death Energy Ball - Mega Man Burst")]
    [Tooltip("How many balls shoot out in a ring (classic Mega Man = 8).")]
    [SerializeField] private int deathBallCount = 8;
    [Tooltip("Outward speed in world units per second.")]
    [SerializeField] private float deathBallSpeed = 8f;
    [Tooltip("Degrees offset for the first ball (22.5 = between cardinal directions).")]
    [SerializeField] private float deathBallAngleOffsetDegrees = 22.5f;
    [Tooltip("Fully faded after traveling this far from the death center.")]
    [SerializeField] private float deathBallFadeDistance = 6f;
    [Tooltip("Destroy each ball after this many seconds (safety cap).")]
    [SerializeField] private float deathBallMaxLifetime = 2.5f;
    [Tooltip("Scene fade / return waits until every ball has traveled at least this far (world units / spaces).")]
    [SerializeField] private float deathBallWaitTravelDistance = 8f;
    [Tooltip("Bool parameter on the death-ball Animator (if present).")]
    [SerializeField] private string deathBoolParameter = "IsDying";
    [Tooltip("Trigger parameter on the death-ball Animator (if present).")]
    [SerializeField] private string deathTriggerParameter = "death";
    [Tooltip("Animator state / clip name (e.g. Death animation / Death animation purple). Used as Play fallback.")]
    [SerializeField] private string deathAnimationStateName = "Death animation";

    [Header("Optional Defaults By Type")]
    [Tooltip("When enabled, changing Effect Type in the Inspector fills in suggested settings.")]
    [SerializeField] private bool applySuggestedSettingsWhenTypeChanges = true;

    public VisualEffectType EffectType => effectType;
    public float MaxTravelDistance => maxTravelDistance;
    public float MaxLifetime => maxLifetime;
    public float MaxEndScale => maxEndScale;
    public Color BlastColor => blastColor;
    public bool TintBlastSprites => tintBlastSprites;
    public string IsStunnedBoolParameter => isStunnedBoolParameter;
    public string StunnedTriggerParameter => stunnedTriggerParameter;
    public int SortOrderInFrontOfHost => sortOrderInFrontOfHost;

    public int DeathBallCount => deathBallCount;
    public float DeathBallSpeed => deathBallSpeed;
    public float DeathBallAngleOffsetDegrees => deathBallAngleOffsetDegrees;
    public float DeathBallFadeDistance => deathBallFadeDistance;
    public float DeathBallMaxLifetime => deathBallMaxLifetime;
    public float DeathBallWaitTravelDistance => deathBallWaitTravelDistance;
    public string DeathBoolParameter => deathBoolParameter;
    public string DeathTriggerParameter => deathTriggerParameter;
    public string DeathAnimationStateName => deathAnimationStateName;

    public bool IsBusterBlastType => effectType == VisualEffectType.BusterBlast;
    public bool IsStunnedType => effectType == VisualEffectType.DefaultStunned;
    public bool IsDeathEnergyBallType => effectType == VisualEffectType.DeathEnergyBall;

#if UNITY_EDITOR
    private VisualEffectType lastValidatedType;

    private void OnValidate()
    {
        maxTravelDistance = Mathf.Max(0.05f, maxTravelDistance);
        maxLifetime = Mathf.Max(0.05f, maxLifetime);
        maxEndScale = Mathf.Max(1f, maxEndScale);
        sortOrderInFrontOfHost = Mathf.Max(1, sortOrderInFrontOfHost);
        deathBallCount = Mathf.Max(1, deathBallCount);
        deathBallSpeed = Mathf.Max(0.1f, deathBallSpeed);
        deathBallFadeDistance = Mathf.Max(0.1f, deathBallFadeDistance);
        deathBallMaxLifetime = Mathf.Max(0.1f, deathBallMaxLifetime);
        deathBallWaitTravelDistance = Mathf.Max(0.1f, deathBallWaitTravelDistance);

        if (string.IsNullOrWhiteSpace(isStunnedBoolParameter))
            isStunnedBoolParameter = "IsStunned";
        if (string.IsNullOrWhiteSpace(stunnedTriggerParameter))
            stunnedTriggerParameter = "stunned";
        if (string.IsNullOrWhiteSpace(deathBoolParameter))
            deathBoolParameter = "IsDying";
        if (string.IsNullOrWhiteSpace(deathTriggerParameter))
            deathTriggerParameter = "death";
        if (string.IsNullOrWhiteSpace(deathAnimationStateName))
            deathAnimationStateName = "Death animation";

        if (!applySuggestedSettingsWhenTypeChanges || effectType == lastValidatedType)
            return;

        ApplySuggestedSettingsForType(effectType);
        lastValidatedType = effectType;
    }

    private void ApplySuggestedSettingsForType(VisualEffectType type)
    {
        switch (type)
        {
            case VisualEffectType.BusterBlast:
                maxTravelDistance = 2f;
                maxLifetime = 0.3f;
                maxEndScale = 1.5f;
                blastColor = new Color(1f, 0.45f, 0.75f, 1f);
                tintBlastSprites = true;
                break;
            case VisualEffectType.DefaultStunned:
                isStunnedBoolParameter = "IsStunned";
                stunnedTriggerParameter = "stunned";
                sortOrderInFrontOfHost = 1;
                break;
            case VisualEffectType.DeathEnergyBall:
                deathBallCount = 8;
                deathBallSpeed = 8f;
                deathBallAngleOffsetDegrees = 22.5f;
                deathBallFadeDistance = 6f;
                deathBallMaxLifetime = 2.5f;
                deathBallWaitTravelDistance = 8f;
                deathBoolParameter = "IsDying";
                deathTriggerParameter = "death";
                deathAnimationStateName = "Death animation";
                sortOrderInFrontOfHost = 1;
                break;
        }
    }
#endif
}

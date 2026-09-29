using UnityEngine;

/// <summary>
/// Tints a copy-bot boss gray (Copy Mega Man style). Auras and charge effects are left unchanged.
/// </summary>
[DisallowMultipleComponent]
public class CopyBossAppearance : MonoBehaviour
{
    [SerializeField] [Range(0.05f, 1f)] private float grayLevel = 0.52f;

    private Boss boss;

    public static CopyBossAppearance Apply(Boss target, float gray = 0.52f)
    {
        if (target == null)
            return null;

        CopyBossAppearance appearance = target.GetComponent<CopyBossAppearance>();
        if (appearance == null)
            appearance = target.gameObject.AddComponent<CopyBossAppearance>();

        appearance.grayLevel = Mathf.Clamp(gray, 0.05f, 1f);
        appearance.ApplyTint();
        return appearance;
    }

    private void Awake()
    {
        boss = GetComponent<Boss>();
        ApplyTint();
    }

    private void ApplyTint()
    {
        if (boss == null)
            boss = GetComponent<Boss>();

        Color gray = new Color(grayLevel, grayLevel, grayLevel, 1f);

        SpriteRenderer body = boss != null ? boss.GetComponent<SpriteRenderer>() : GetComponent<SpriteRenderer>();
        if (body != null)
            body.color = gray;

        if (boss is BossKit kitBoss)
            kitBoss.SetCopyBotAfterimageStyle(grayLevel);
        else if (boss is BossMalice maliceBoss)
            maliceBoss.SetCopyBotAfterimageStyle(grayLevel);
    }
}

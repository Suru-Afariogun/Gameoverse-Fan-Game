using UnityEngine;

/// <summary>
/// Boss-room boost crystals: speed, power, or heal the active copy-bot boss.
/// Place three in the arena (one of each type).
/// </summary>
public class BossRoomBoostCrystal : MonoBehaviour
{
    public enum BoostType
    {
        Speed,
        Power,
        Heal
    }

    [Header("Type")]
    [SerializeField] private BoostType boostType = BoostType.Speed;

    [Header("Radius")]
    [SerializeField] private float boostRadius = 3f;

    [Header("Speed / Power")]
    [SerializeField] private float boostDuration = 3f;
    [SerializeField] private float speedBonus = 2f;
    [SerializeField] private int attackBonus = 2;

    [Header("Heal")]
    [SerializeField] private int healAmount = 2;
    [SerializeField] private float healCooldown = 7f;

    private float nextHealTime;

    private void Update()
    {
        Boss boss = FindBossInRadius();
        if (boss == null || boss.IsDead)
            return;

        switch (boostType)
        {
            case BoostType.Speed:
                boss.BeginCrystalSpeedBoost(speedBonus, boostDuration);
                break;

            case BoostType.Power:
            {
                int bonus = boss.IsCopyBot ? 1 : attackBonus;
                boss.BeginCrystalAttackBoost(bonus, boostDuration);
                break;
            }

            case BoostType.Heal:
                TryHealBoss(boss);
                break;
        }
    }

    private void TryHealBoss(Boss boss)
    {
        if (Time.time < nextHealTime)
            return;

        if (boss.CurrentHealth >= boss.MaxHealth)
            return;

        boss.Heal(Mathf.Max(1, healAmount));
        nextHealTime = Time.time + Mathf.Max(0.05f, healCooldown);
    }

    private Boss FindBossInRadius()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, boostRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;

            Boss boss = hit.GetComponent<Boss>();
            if (boss == null)
                boss = hit.GetComponentInParent<Boss>();

            if (boss != null && boss.gameObject.activeInHierarchy)
                return boss;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, boostRadius);
    }

    private void OnValidate()
    {
        boostRadius = Mathf.Max(0.1f, boostRadius);
        boostDuration = Mathf.Max(0.05f, boostDuration);
        speedBonus = Mathf.Max(0f, speedBonus);
        attackBonus = Mathf.Max(0, attackBonus);
        healAmount = Mathf.Max(1, healAmount);
        healCooldown = Mathf.Max(0.05f, healCooldown);
    }
#endif
}

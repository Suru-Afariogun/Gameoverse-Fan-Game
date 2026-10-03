/// <summary>
/// Common stage enemies (Cranky Clanky, etc.) that take damage from players and projectiles.
/// </summary>
public interface ICommonEnemy : IDamageable
{
    int CurrentHealth { get; }
}

/// <summary>Enemies that can be destroyed instantly, ignoring hit invincibility (Harlie's Heavy Style chain kill).</summary>
public interface IForceKillable
{
    void ForceKill();
}

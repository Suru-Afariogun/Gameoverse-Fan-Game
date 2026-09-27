/// <summary>
/// Common stage enemies (Cranky Clanky, etc.) that take damage from players and projectiles.
/// </summary>
public interface ICommonEnemy : IDamageable
{
    int CurrentHealth { get; }
}

/// <summary>
/// Common enemies that can receive the orange attack boost from an Enemy Crystal.
/// </summary>
public interface IEnemyCrystalBoostable
{
    void SetEnemyCrystalBoost(int attackBonus);
    void ClearEnemyCrystalBoost();
}

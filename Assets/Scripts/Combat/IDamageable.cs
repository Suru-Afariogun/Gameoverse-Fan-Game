using System;
using UnityEngine;

/// <summary>
/// Anything that AttackHitbox (and other combat) can damage.
/// Implemented by PlayerController, Boss, and common enemies such as CrankyClanky.
/// </summary>
public interface IDamageable
{
    void TakeDamage(int amount);
    bool IsDead { get; }
}

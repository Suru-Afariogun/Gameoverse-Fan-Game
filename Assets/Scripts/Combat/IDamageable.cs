using System;
using UnityEngine;

/// <summary>
/// Anything that AttackHitbox (and other combat) can damage.
/// Implemented by PlayerController and Boss.
/// </summary>
public interface IDamageable
{
    void TakeDamage(int amount);
    bool IsDead { get; }
}

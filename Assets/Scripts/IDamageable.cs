/// <summary>
/// Anything the player can shoot. Lets CombatManager apply damage without
/// knowing the concrete type (Enemy, Boss, or anything added later).
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }

    /// <summary>Gold awarded to the player when this target dies.</summary>
    int GoldValue { get; }

    /// <summary>Applies damage. Returns true if this hit killed the target.</summary>
    bool TakeDamage(float damage);
}
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class CombatRelay : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.ProcessCollision(gameObject, other);
        }
    }
}
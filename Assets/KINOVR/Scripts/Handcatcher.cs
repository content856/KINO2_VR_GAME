using UnityEngine;

/// <summary>
/// Put this on each hand's trigger collider (or on a small "palm"
/// collider that's a child of the hand). The Collider on this object
/// must have "Is Trigger" checked. Ball prefabs must be tagged with
/// ballTag and carry a Catchable component.
/// </summary>
public class HandCatcher : MonoBehaviour
{
    [Tooltip("Only objects with this tag are treated as catchable balls.")]
    public string ballTag = "Ball";

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(ballTag)) return;

        Catchable ball = other.GetComponent<Catchable>();
        if (ball != null)
        {
            ball.Catch();
        }
    }
}
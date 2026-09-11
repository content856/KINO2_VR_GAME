using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to the ball prefab. Defines the ball's type/value and what
/// happens the instant it's caught: score is awarded, feedback fires,
/// and the ball is removed. Mystery balls roll their bonus at catch
/// time (not spawn time) so the payoff stays hidden until contact.
/// </summary>
public class Catchable : MonoBehaviour
{
    public enum BallType { Normal, MoreWins, Mystery, SecondChance }

    [Header("Scoring")]
    public BallType ballType = BallType.Normal;
    public int pointValue = 1;

    [Header("Mystery Ball (only used if ballType = Mystery)")]
    public int mysteryMinBonus = 5;
    public int mysteryMaxBonus = 25;

    [Header("Feedback")]
    public GameObject catchVFX;
    public AudioClip catchSFX;

    [Tooltip("Extra hook for anything else that should react to this specific ball being caught.")]
    public UnityEvent<Catchable> onCaught;

    private bool caught = false;

    public void Catch()
    {
        // Guards against both hands, or multiple colliders on one hand,
        // triggering the same ball twice in the same frame.
        if (caught) return;
        caught = true;

        int awarded = pointValue;
        if (ballType == BallType.Mystery)
        {
            awarded = Random.Range(mysteryMinBonus, mysteryMaxBonus + 1);
        }

        ScoreManager.Instance.AddScore(awarded, ballType);

        if (catchVFX != null) Instantiate(catchVFX, transform.position, Quaternion.identity);
        if (catchSFX != null) AudioSource.PlayClipAtPoint(catchSFX, transform.position);

        onCaught?.Invoke(this);

        Destroy(gameObject);
    }
}
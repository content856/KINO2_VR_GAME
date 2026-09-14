using UnityEngine;
using UnityEngine.Events;
using KinoVR;

/// <summary>
/// Attach to the ball prefab. Defines the ball's type/value and what
/// happens the instant it's caught: score is awarded, feedback fires,
/// and the ball is removed. Mystery balls roll their bonus at catch
/// time (not spawn time) so the payoff stays hidden until contact.
/// </summary>
public class Catchable : MonoBehaviour
{
    public enum BallType { Normal, MoreWins, Mystery, SecondChance }

    public int Number { get; private set; } = 1;
    KinoRoundController round;
    bool roundControlled;
    public void Configure(int number, KinoRoundController controller)
    {
        Number = Mathf.Clamp(number, 1, 80);
        round = controller;
        roundControlled = controller != null;
    }

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
        if (caught || !isActiveAndEnabled) return;
        caught = true;

        if (roundControlled && (!round || !round.TryCatch(Number)))
        {
            Destroy(gameObject);
            return;
        }

        int awarded = pointValue;
        if (ballType == BallType.Mystery)
        {
            awarded = Random.Range(mysteryMinBonus, mysteryMaxBonus + 1);
        }

        if (!roundControlled && ScoreManager.Instance) ScoreManager.Instance.AddScore(awarded, ballType);

        if (catchVFX != null)
        {
            var effect = Instantiate(catchVFX, transform.position, Quaternion.identity);
            Destroy(effect, 5);
        }
        if (catchSFX != null) AudioSource.PlayClipAtPoint(catchSFX, transform.position);

        onCaught?.Invoke(this);

        Destroy(gameObject);
    }
}

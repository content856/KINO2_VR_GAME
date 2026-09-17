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
    public enum BallType { Normal, MoreWins, Mystery, SecondChance, KinoBonus }

    public bool IsKinoBonus => ballType == BallType.KinoBonus;
    internal void SetLiveNumber(int number)
    {
        if (!caught) Number = Mathf.Clamp(number, 1, 80);
    }

    public int Number { get; private set; } = 1;
    KinoRoundController round;
    bool roundControlled;
    KinoPooledBall poolBall;
    internal void SetPool(KinoPooledBall ball) => poolBall = ball;
    internal void ResetForPool()
    {
        caught = true;
        Number = 1;
        round = null;
        roundControlled = false;
        ballType = BallType.Normal;
    }
    public void Configure(int number, KinoRoundController controller, bool isKinoBonus = false)
    {
        caught = false;
        Number = Mathf.Clamp(number, 1, 80);
        round = controller;
        roundControlled = controller != null;
        ballType = isKinoBonus ? BallType.KinoBonus : BallType.Normal;
    }

    [Header("Scoring")]
    public BallType ballType = BallType.Normal;
    public int pointValue = 1;

    [Header("Mystery Ball (only used if ballType = Mystery)")]
    public int mysteryMinBonus = 5;
    public int mysteryMaxBonus = 25;

    [Header("Feedback")]
    public GameObject catchVFX;
    public GameObject kinoBonusCatchVFX;
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
        uint generation = poolBall ? poolBall.Generation : 0;

        if (roundControlled && (!round || !round.TryCatch(Number, ballType)))
        {
            Release(generation);
            return;
        }

        int awarded = pointValue * (IsKinoBonus ? KinoRoundState.BonusMultiplier : 1);
        if (ballType == BallType.Mystery)
        {
            awarded = Random.Range(mysteryMinBonus, mysteryMaxBonus + 1);
        }

        if (!roundControlled && ScoreManager.Instance) ScoreManager.Instance.AddScore(awarded, ballType);

        var feedback = IsKinoBonus && kinoBonusCatchVFX ? kinoBonusCatchVFX : catchVFX;
        if (feedback != null)
        {
            var effect = Instantiate(feedback, transform.position, Quaternion.identity);
            Destroy(effect, 5);
        }
        if (catchSFX != null) AudioSource.PlayClipAtPoint(catchSFX, transform.position);

        onCaught?.Invoke(this);

        Release(generation);
    }
    public void Miss()
    {
        if (caught || !isActiveAndEnabled) return;
        caught = true;
        uint generation = poolBall ? poolBall.Generation : 0;
        if (roundControlled && round) round.MissBall(IsKinoBonus);
        Release(generation);
    }
    void Release(uint generation)
    {
        if (poolBall) poolBall.ReturnToPool(generation);
        else Destroy(gameObject);
    }
}

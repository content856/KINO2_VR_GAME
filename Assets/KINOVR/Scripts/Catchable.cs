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
    public enum BallType { Normal, MoreWins, Mystery, SecondChance, KinoBonus, KinoBoost }

    public bool IsKinoBonus => ballType == BallType.KinoBonus;
    public bool IsSecondChance => ballType == BallType.SecondChance;
    public bool IsBoost => ballType == BallType.KinoBoost;
    public bool IsGlow => ballType == BallType.MoreWins;
    public bool IsMystery => ballType == BallType.Mystery;
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
    public void Configure(int number, KinoRoundController controller, bool isKinoBonus = false, bool isSecondChance = false, bool isBoost = false, bool isGlow = false, bool isMystery = false)
    {
        caught = false;
        Number = isMystery ? 0 : Mathf.Clamp(number, 1, 80);
        round = controller;
        roundControlled = controller != null;
        ballType = isKinoBonus ? BallType.KinoBonus : isSecondChance ? BallType.SecondChance : isBoost ? BallType.KinoBoost :
            isMystery ? BallType.Mystery : isGlow ? BallType.MoreWins : BallType.Normal;
    }

    [Header("Scoring")]
    public BallType ballType = BallType.Normal;
    public int pointValue = 1;

    [Header("Feedback")]
    public GameObject catchVFX;
    public GameObject kinoBonusCatchVFX;
    public GameObject secondChanceCatchVFX;
    public AudioClip catchSFX;

    [Tooltip("Extra hook for anything else that should react to this specific ball being caught.")]
    public UnityEvent<Catchable> onCaught;

    private bool caught = false;

    public void Catch() => CatchAt(transform.position);
    public void CatchAt(Vector3 contactPoint)
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

        int awarded = IsMystery ? 0 : IsGlow ? 2 : pointValue * (IsKinoBonus || IsSecondChance || IsBoost ? KinoRoundState.BonusMultiplier : 1);
        if (roundControlled && round && round.catchFeedback)
            round.catchFeedback.Show(contactPoint, ballType, round.State.LastMysteryMultiplier);

        if (!roundControlled && ScoreManager.Instance) ScoreManager.Instance.AddScore(awarded, ballType);

        var feedback = IsKinoBonus && kinoBonusCatchVFX ? kinoBonusCatchVFX :
            IsSecondChance && secondChanceCatchVFX ? secondChanceCatchVFX : catchVFX;
        if (feedback != null)
        {
            var effect = Instantiate(feedback, transform.position, Quaternion.identity);
            Destroy(effect, 5);
        }
        if (roundControlled && round && round.audioController)
            round.audioController.PlayCatch(ballType, transform.position);
        else if (catchSFX != null) AudioSource.PlayClipAtPoint(catchSFX, transform.position);

        onCaught?.Invoke(this);

        Release(generation);
    }
    public void Miss(bool hitFloor = false)
    {
        if (caught || !isActiveAndEnabled) return;
        caught = true;
        uint generation = poolBall ? poolBall.Generation : 0;
        if (roundControlled && round) round.MissBall(IsKinoBonus, IsSecondChance, IsBoost, IsGlow, IsMystery);
        if (hitFloor && roundControlled && round && round.audioController)
            round.audioController.Play(KinoSound.BallMiss, transform.position);
        Release(generation);
    }
    void Release(uint generation)
    {
        if (poolBall) poolBall.ReturnToPool(generation);
        else Destroy(gameObject);
    }
}

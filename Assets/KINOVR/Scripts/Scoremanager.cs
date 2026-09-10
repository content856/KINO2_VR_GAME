using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Single source of truth for the current round's score. Put one of
/// these in the scene (it self-enforces as a singleton). Other systems
/// (UI, celebratory effects, the More Wins / 2nd Chance moments) can
/// subscribe to onScoreChanged / onBallCaught instead of polling.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public int CurrentScore { get; private set; }

    [Tooltip("Fires whenever score changes, with the new total. Drive your score UI off this.")]
    public UnityEvent<int> onScoreChanged;

    [Tooltip("Fires whenever a ball is caught, with points awarded and ball type. Useful for per-type VFX (e.g. a bigger celebration on MoreWins).")]
    public UnityEvent<int, Catchable.BallType> onBallCaught;

    public TextMeshProUGUI scoreText;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddScore(int amount, Catchable.BallType ballType)
    {
        CurrentScore += amount;
        onScoreChanged?.Invoke(CurrentScore);
        onBallCaught?.Invoke(amount, ballType);

        scoreText.text = CurrentScore.ToString();
    }

    public void ResetScore()
    {
        CurrentScore = 0;
        onScoreChanged?.Invoke(CurrentScore);

        scoreText.text = CurrentScore.ToString();
    }
}
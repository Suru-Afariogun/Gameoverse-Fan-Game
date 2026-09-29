using UnityEngine;

/// <summary>
/// Persistent flags for finishing Kaboodle tutorials per character.
/// Once Kit and Malice are both done, forced "speak to Kaboodle" text never returns.
/// </summary>
public static class KaboodleTutorialProgress
{
    const string PrefsKit = "Gameoverse_TutorialDone_Kit";
    const string PrefsMalice = "Gameoverse_TutorialDone_Malice";

    public static bool IsTutorialDone(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return false;

        if (characterId.Equals("Malice", System.StringComparison.OrdinalIgnoreCase))
            return PlayerPrefs.GetInt(PrefsMalice, 0) == 1;

        if (characterId.Equals("Kit", System.StringComparison.OrdinalIgnoreCase))
            return PlayerPrefs.GetInt(PrefsKit, 0) == 1;

        return PlayerPrefs.GetInt("Gameoverse_TutorialDone_" + characterId.Trim(), 0) == 1;
    }

    public static bool BothTutorialsDone()
    {
        return IsTutorialDone("Kit") && IsTutorialDone("Malice");
    }

    public static void MarkTutorialDone(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return;

        if (characterId.Equals("Malice", System.StringComparison.OrdinalIgnoreCase))
            PlayerPrefs.SetInt(PrefsMalice, 1);
        else if (characterId.Equals("Kit", System.StringComparison.OrdinalIgnoreCase))
            PlayerPrefs.SetInt(PrefsKit, 1);
        else
            PlayerPrefs.SetInt("Gameoverse_TutorialDone_" + characterId.Trim(), 1);

        PlayerPrefs.Save();
    }
}

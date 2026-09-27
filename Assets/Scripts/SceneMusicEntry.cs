using System;
using UnityEngine;

/// <summary>
/// One scene → one looping music track. Used by <see cref="SoundManager"/> extra scene list.
/// Scene name is set from the Inspector (drag a .unity Scene asset onto the row).
/// </summary>
[Serializable]
public class SceneMusicEntry
{
    [SerializeField] private string sceneName;
    [SerializeField] private AudioClip music;
    [SerializeField] [Range(0f, 1f)] private float trackVolume = 1f;

    public string SceneName => sceneName ?? string.Empty;
    public AudioClip Music => music;
    public float TrackVolume => trackVolume;

#if UNITY_EDITOR
    public void SetSceneName(string name)
    {
        sceneName = name ?? string.Empty;
    }
#endif
}

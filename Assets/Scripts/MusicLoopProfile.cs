using System;
using UnityEngine;

/// <summary>
/// Optional per-song loop window for <see cref="SoundManager"/>.
/// Leave loop end at 0 to auto-trim the fade-out tail.
/// </summary>
[Serializable]
public class MusicLoopProfile
{
    public AudioClip clip;
    [Tooltip("Where the loop jumps back to. 0 = start of the clip.")]
    [Min(0f)] public float loopStart;
    [Tooltip("Where the loop jumps from. 0 = auto (clip length minus global fade trim).")]
    [Min(0f)] public float loopEnd;
}

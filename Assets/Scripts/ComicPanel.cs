using UnityEngine;
using System;
using TMPro;

namespace Vampire
{
    public enum CharacterSide { Left, Right, None }

    [Serializable]
    public class ComicPanel
    {
        [Header("Background")]
        public Sprite background;

        [Header("Character")]
        public Sprite character;
        public CharacterSide characterSide = CharacterSide.None;

        [Header("Dialogue")]
        [TextArea(2, 5)]
        public string dialogueText = "";

        [Header("Timing")]
        [Tooltip("Seconds before auto-advancing. 0 = wait for player input.")]
        public float autoDuration = 0f;

        [Header("Audio")]
        [Tooltip("One-shot SFX played when this panel appears")]
        public AudioClip sfx;

        [Tooltip("Music to start playing when this panel appears (leave null to keep current track)")]
        public AudioClip music;

        [Range(0f, 1f)]
        public float musicVolume = 0.5f;
    }
}

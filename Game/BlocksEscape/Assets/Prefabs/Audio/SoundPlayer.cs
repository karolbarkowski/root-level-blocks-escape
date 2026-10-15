using UnityEngine;

namespace BlocksEscape
{
    // Plays the game's sounds: the looping background music from the start, and now and then an animal's voice when the
    // player picks up its block (a random clip from that block type's sounds, see GameSounds), and the victory jingle
    // when a level is solved (PlayVictory). Follows the player's
    // music and sound switches in GameSettings.
    [DisallowMultipleComponent]
    public sealed class SoundPlayer : MonoBehaviour
    {
        [SerializeField] GameSounds sounds;
        [SerializeField] Board board;
        [SerializeField] BlockDragger dragger;
        [SerializeField, Range(0f, 1f)] float musicVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] float effectsVolume = 1f;
        [Tooltip("Chance that picking up a block makes its animal call out.")]
        [SerializeField, Range(0f, 1f)] float pickupChance = 0.1f;

        AudioSource _music, _effects;

        void Awake()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.volume = musicVolume;
            _effects = gameObject.AddComponent<AudioSource>();
            _effects.playOnAwake = false;
        }

        void OnEnable()
        {
            if (dragger != null) dragger.Grabbed += OnGrabbed;
            GameSettings.Changed += ApplySettings;
            ApplySettings();
        }

        void OnDisable()
        {
            if (dragger != null) dragger.Grabbed -= OnGrabbed;
            GameSettings.Changed -= ApplySettings;
        }

        void ApplySettings()
        {
            if (_music != null) _music.mute = !GameSettings.MusicOn;
        }

        void Start()
        {
            if (sounds == null || sounds.Music == null) return;
            _music.clip = sounds.Music;
            _music.Play();
        }

        public void PlayVictory()
        {
            if (GameSettings.SoundOn && sounds != null && sounds.Victory != null) _effects.PlayOneShot(sounds.Victory, effectsVolume);
        }

        void OnGrabbed(int block)
        {
            if (!GameSettings.SoundOn || sounds == null || board == null || board.Rules == null || Random.value >= pickupChance) return;
            var types = board.Rules.RemainingTypes(block);
            if (types == null || types.Length == 0) return; // neutral blocks have no voice
            var clips = sounds.ClipsOf(types[0]); // the animal on the outside
            if (clips.Count == 0) return;
            _effects.PlayOneShot(clips[Random.Range(0, clips.Count)], effectsVolume);
        }
    }
}

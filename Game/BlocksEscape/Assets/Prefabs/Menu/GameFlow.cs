using System.Collections;
using UnityEngine;
using BlocksEscape.Levels;

namespace BlocksEscape
{
    // Runs the game in one scene: the main menu, the settings page and the levels share the meadow and its scenery.
    // In the menu the board is cleared and the menu planks lie where it was; starting a level hides them, builds the
    // level from the catalog and shows the level sign and back button. Each page is a GameObject whose children are the
    // planks, top to bottom. A solved level is marked in Progress and, after
    // the last block has left, the next level follows (the menu after the last one).
    //
    // Play starts level 1 until there is a level picker; Continue goes where Progress.ContinueLevel says and is hidden
    // before the first level is played; Quit is hidden where apps don't quit themselves (iOS, web).
    [DisallowMultipleComponent]
    public sealed class GameFlow : MonoBehaviour
    {
        enum Screen { Menu, Settings, Level }

        [SerializeField] LevelCatalog catalog;
        [SerializeField] Board board;
        [SerializeField] BoardCamera boardCamera;
        [SerializeField] BlockDragger dragger;
        [SerializeField] LevelSign levelSign;
        [SerializeField] WorldButton backButton;

        [Header("Main menu")]
        [SerializeField] GameObject mainPage;
        [SerializeField] WorldButton playButton;
        [SerializeField] WorldButton continueButton;
        [SerializeField] WorldButton settingsButton;
        [SerializeField] WorldButton quitButton;

        [Header("Settings")]
        [SerializeField] GameObject settingsPage;
        [SerializeField] WorldButton musicButton;
        [SerializeField] WorldButton soundButton;

        [Header("Layout")]
        [Tooltip("Screen point of the first plank on a page, (0, 0) bottom left, (1, 1) top right.")]
        [SerializeField] Vector2 firstPlank = new Vector2(0.5f, 0.62f);
        [Tooltip("Screen height between planks, 0..1.")]
        [SerializeField, Range(0.02f, 0.3f)] float plankStep = 0.1f;

        [Tooltip("Seconds from solving a level to the next one, so the last block's exit plays out.")]
        [SerializeField, Min(0f)] float nextLevelDelay = 1.5f;

        Screen _screen;
        int _level = -1; // catalog index of the level being played

        static bool CanQuit =>
            Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.WebGLPlayer;

        void OnEnable()
        {
            if (board != null) board.Completed += OnCompleted;
            Listen(backButton, Back);
            Listen(playButton, () => StartLevel(0));
            Listen(continueButton, () => StartLevel(Progress.ContinueLevel(catalog.Count)));
            Listen(settingsButton, ShowSettings);
            Listen(quitButton, Quit);
            Listen(musicButton, () => GameSettings.MusicOn = !GameSettings.MusicOn);
            Listen(soundButton, () => GameSettings.SoundOn = !GameSettings.SoundOn);
            GameSettings.Changed += UpdateSettingsLabels;
        }

        void OnDisable()
        {
            if (board != null) board.Completed -= OnCompleted;
            foreach (var button in new[] { backButton, playButton, continueButton, settingsButton, quitButton, musicButton, soundButton })
                if (button != null) button.Clicked.RemoveAllListeners();
            GameSettings.Changed -= UpdateSettingsLabels;
        }

        static void Listen(WorldButton button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.Clicked.AddListener(action);
        }

        void Start() => ShowMenu();

        public void ShowMenu()
        {
            StopAllCoroutines();
            _screen = Screen.Menu;
            _level = -1;
            board.Clear();
            dragger.enabled = false;
            boardCamera.Frame(); // the cleared board has its own framing; the scenery and planks follow it
            Show(levelSign, false);
            Show(backButton, false);
            mainPage.SetActive(true);
            settingsPage.SetActive(false);
            Show(continueButton, Progress.HasStarted && catalog.Count > 0);
            Show(quitButton, CanQuit);
            LayOut(mainPage);
        }

        public void ShowSettings()
        {
            _screen = Screen.Settings;
            mainPage.SetActive(false);
            settingsPage.SetActive(true);
            Show(backButton, true);
            UpdateSettingsLabels();
            LayOut(settingsPage);
        }

        public void Back()
        {
            if (_screen != Screen.Menu) ShowMenu();
        }

        public void StartLevel(int index)
        {
            if (catalog == null || index < 0 || index >= catalog.Count) return;
            StopAllCoroutines();
            _screen = Screen.Level;
            _level = index;
            Progress.LastPlayed = index;
            mainPage.SetActive(false);
            settingsPage.SetActive(false);
            Show(levelSign, true);
            Show(backButton, true);
            levelSign.Number = index + 1;
            board.Level = catalog[index];
            board.Initialize(LevelData.Parse(catalog[index].text));
            dragger.enabled = true;
        }

        void OnCompleted()
        {
            if (_screen != Screen.Level) return;
            Progress.MarkSolved(_level);
            StartCoroutine(NextLevelAfterDelay());
        }

        IEnumerator NextLevelAfterDelay()
        {
            yield return new WaitForSeconds(nextLevelDelay);
            if (_level + 1 < catalog.Count) StartLevel(_level + 1);
            else ShowMenu();
        }

        void UpdateSettingsLabels()
        {
            SetLabel(musicButton, GameSettings.MusicOn ? "MUSIC ON" : "MUSIC OFF");
            SetLabel(soundButton, GameSettings.SoundOn ? "SOUND ON" : "SOUND OFF");
        }

        static void SetLabel(WorldButton button, string text)
        {
            if (button != null && button.TryGetComponent<WorldLabel>(out var label)) label.Text = text;
        }

        // Stacks the page's visible planks down from First Plank, so a hidden one leaves no gap.
        void LayOut(GameObject page)
        {
            int row = 0;
            foreach (Transform child in page.transform)
                if (child.gameObject.activeSelf && child.TryGetComponent<ScreenAnchor>(out var anchor))
                    anchor.ViewportPoint = firstPlank - new Vector2(0f, plankStep * row++);
        }

        static void Show(Component c, bool visible)
        {
            if (c != null) c.gameObject.SetActive(visible);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BlocksEscape.Levels;

namespace BlocksEscape
{
    // Runs the game in one scene: the main menu, the settings page and the levels share the meadow and its scenery.
    // In the menu the board is cleared, the title sign stands at the top and the menu planks lie where the board was; starting a level hides them, builds the
    // level from the catalog and shows the level sign and back button. Each page is a GameObject whose children are the
    // planks, top to bottom. A solved level is marked in Progress and celebrated (Celebration) as the last block leaves;
    // then the next level follows (the menu after the last one).
    //
    // Every switch is animated: what goes away pops out (PopTransition; the blocks shrink into the board with
    // BlockAppear), then what comes pops in, one after another. Buttons are ignored until a switch has finished.
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
        [Tooltip("The game's title sign at the top of the main menu.")]
        [SerializeField] ScreenAnchor title;
        [SerializeField] GameObject mainPage;
        [SerializeField] WorldButton playButton;
        [SerializeField] WorldButton continueButton;
        [SerializeField] WorldButton settingsButton;
        [SerializeField] WorldButton quitButton;

        [Header("Settings")]
        [SerializeField] GameObject settingsPage;
        [SerializeField] WorldButton musicButton;
        [SerializeField] WorldButton soundButton;

        [Header("Transitions")]
        [SerializeField, Min(0.01f)] float popInTime = 0.3f;
        [SerializeField, Min(0.01f)] float popOutTime = 0.16f;
        [Tooltip("Seconds between one plank or button starting to pop and the next.")]
        [SerializeField, Min(0f)] float popStagger = 0.05f;
        [SerializeField, Min(0.01f)] float blockPopTime = 0.25f;
        [Tooltip("Seconds the blocks' pop-in takes to sweep from the top left corner of the board to the bottom right.")]
        [SerializeField, Min(0f)] float blockSweep = 0.35f;

        [Header("Level solved")]
        [SerializeField] Celebration celebration;
        [Tooltip("Seconds from solving a level to the celebration, so the last block slides out through its gate first.")]
        [SerializeField, Min(0f)] float celebrationDelay = 0.4f;
        [Tooltip("Seconds from solving a level to the next one, so the celebration plays out.")]
        [SerializeField, Min(0f)] float nextLevelDelay = 2.4f;

        Screen _screen;
        int _level = -1; // catalog index of the level being played
        bool _busy;      // a switch is animating
        ScreenAnchor _sign, _back;
        readonly Dictionary<GameObject, List<Vector2>> _slots = new(); // per page: the planks' places as laid out in the scene

        static bool CanQuit =>
            Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.WebGLPlayer;

        void Awake()
        {
            _sign = levelSign.GetComponentInParent<ScreenAnchor>();
            _back = backButton.GetComponentInParent<ScreenAnchor>();
            // The planks' places in the scene, top to bottom, are the slots the shown ones fill (LayOut).
            foreach (var page in new[] { mainPage, settingsPage })
                _slots[page] = Planks(page, false).ConvertAll(plank => plank.ViewportPoint);
            // Everything starts hidden and pops in as the first screen opens.
            mainPage.SetActive(true);
            settingsPage.SetActive(true);
            foreach (var plank in Planks(mainPage, false)) plank.gameObject.SetActive(false);
            foreach (var plank in Planks(settingsPage, false)) plank.gameObject.SetActive(false);
            _sign.gameObject.SetActive(false);
            _back.gameObject.SetActive(false);
            if (title != null) title.gameObject.SetActive(false);
        }

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

        public void ShowMenu() => Switch(ToMenu());
        public void ShowSettings() => Switch(ToSettings());

        public void StartLevel(int index)
        {
            if (catalog != null && index >= 0 && index < catalog.Count) Switch(ToLevel(index));
        }

        public void Back()
        {
            if (_screen != Screen.Menu) ShowMenu();
        }

        // Runs a switch unless one is already running.
        void Switch(IEnumerator routine)
        {
            if (!_busy) StartCoroutine(Busy(routine));
        }

        IEnumerator Busy(IEnumerator routine)
        {
            _busy = true;
            dragger.enabled = false;
            yield return routine;
            dragger.enabled = _screen == Screen.Level;
            _busy = false;
        }

        IEnumerator ToMenu()
        {
            _screen = Screen.Menu;
            _level = -1;
            var hide = new List<ScreenAnchor> { _sign, _back };
            hide.AddRange(Planks(settingsPage, false));
            float blocks = BlockAppear.Sweep(board, false, blockSweep * 0.5f, popOutTime);
            yield return Together(Pop(hide, false), Wait(blocks));
            board.Clear();
            boardCamera.Frame(); // the cleared board has its own framing; the scenery and planks follow it
            yield return Pop(MainMenu(true), true);
        }

        IEnumerator ToSettings()
        {
            _screen = Screen.Settings;
            UpdateSettingsLabels();
            yield return Pop(MainMenu(false), false);
            var show = LayOut(settingsPage);
            show.Add(_back);
            yield return Pop(show, true);
        }

        IEnumerator ToLevel(int index)
        {
            _screen = Screen.Level;
            _level = index;
            Progress.LastPlayed = index;
            // The back button stays when going from level to level; the sign pops out and back in with the new number.
            yield return Pop(new List<ScreenAnchor>(MainMenu(false)) { _sign }, false);
            levelSign.Number = index + 1;
            board.Level = catalog[index];
            board.Initialize(LevelData.Parse(catalog[index].text));
            float blocks = BlockAppear.Sweep(board, true, blockSweep, blockPopTime);
            var show = new List<ScreenAnchor> { _sign };
            if (!_back.gameObject.activeSelf) show.Add(_back);
            yield return Together(Pop(show, true), Wait(blocks));
        }

        void OnCompleted()
        {
            if (_screen != Screen.Level || _busy) return;
            Progress.MarkSolved(_level);
            Switch(NextLevel());
        }

        IEnumerator NextLevel()
        {
            yield return Wait(celebrationDelay);
            if (celebration != null) celebration.Play();
            yield return Wait(nextLevelDelay - celebrationDelay);
            yield return _level + 1 < catalog.Count ? ToLevel(_level + 1) : ToMenu();
        }

        IEnumerator Pop(List<ScreenAnchor> props, bool show) =>
            PopTransition.Run(props, show, show ? popInTime : popOutTime, popStagger);

        static IEnumerator Wait(float seconds)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
        }

        // Runs the routines side by side and waits for all of them.
        IEnumerator Together(params IEnumerator[] routines)
        {
            var running = Array.ConvertAll(routines, StartCoroutine);
            foreach (var r in running) yield return r;
        }

        // The main menu in the order it pops: the title, then the planks. To show it, only the available planks, laid out
        // afresh; to hide it, all of them where they are (hiding skips the ones not shown), so none jumps as it goes.
        List<ScreenAnchor> MainMenu(bool show)
        {
            var menu = new List<ScreenAnchor>();
            if (title != null) menu.Add(title);
            menu.AddRange(show ? LayOut(mainPage) : Planks(mainPage, false));
            return menu;
        }

        // The page's planks, top to bottom; with availableOnly, only those that apply now (no Continue before the first
        // level, no Quit where apps don't quit).
        List<ScreenAnchor> Planks(GameObject page, bool availableOnly)
        {
            var planks = new List<ScreenAnchor>();
            foreach (Transform child in page.transform)
            {
                if (!child.TryGetComponent<ScreenAnchor>(out var anchor)) continue;
                if (availableOnly && child == continueButton.transform && !(Progress.HasStarted && catalog.Count > 0)) continue;
                if (availableOnly && child == quitButton.transform && !CanQuit) continue;
                planks.Add(anchor);
            }
            return planks;
        }

        // Puts the page's available planks in its slots from the top, so a missing one leaves no gap, and returns them.
        // The slots are where the planks stand in the scene (all shown), so the scene shows the layout as played.
        List<ScreenAnchor> LayOut(GameObject page)
        {
            var planks = Planks(page, true);
            var slots = _slots[page];
            for (int row = 0; row < planks.Count && row < slots.Count; row++) planks[row].ViewportPoint = slots[row];
            return planks;
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

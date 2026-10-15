using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
    // Continue goes where Progress.ContinueLevel says; before the first level is ever started it reads START (and
    // starts level 1). Pick Level opens the level picker (LevelPicker): the section planks under a "PICK LEVEL" sign, then
    // the chosen section's pages of level tiles under its name. Back steps out one screen at a time.
    [DisallowMultipleComponent]
    public sealed class GameFlow : MonoBehaviour
    {
        enum Screen { Menu, Settings, Sections, Page, Level }

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
        [SerializeField] WorldButton continueButton;
        [SerializeField, FormerlySerializedAs("playButton")] WorldButton pickLevelButton;
        [SerializeField] WorldButton settingsButton;

        [Header("Level picker")]
        [SerializeField] LevelPicker picker;

        [Header("Settings")]
        [SerializeField] GameObject settingsPage;
        [SerializeField] WorldButton musicButton;
        [SerializeField] WorldButton soundButton;

        [Header("Transitions")]
        [SerializeField, Min(0.01f)] float popInTime = 0.3f;
        [SerializeField, Min(0.01f)] float popOutTime = 0.16f;
        [Tooltip("Seconds between one plank or button starting to pop and the next.")]
        [SerializeField, Min(0f)] float popStagger = 0.05f;
        [Tooltip("Longest the stagger may spread one pop over, in seconds: long lists (the level picker's grid) stagger faster.")]
        [SerializeField, Min(0f)] float maxStaggerSpread = 0.35f;
        [SerializeField, Min(0.01f)] float blockPopTime = 0.25f;
        [Tooltip("Seconds the blocks' pop-in takes to sweep from the top left corner of the board to the bottom right.")]
        [SerializeField, Min(0f)] float blockSweep = 0.35f;
        [Tooltip("Seconds the pen floor takes to grow out of the meadow; the fence starts rising halfway through.")]
        [SerializeField, Min(0.01f)] float floorGrowTime = 0.35f;
        [SerializeField, Min(0.01f)] float fencePopTime = 0.25f;
        [Tooltip("Seconds the fence's pop-in takes to sweep from the top left corner of the board to the bottom right.")]
        [SerializeField, Min(0f)] float fenceSweep = 0.3f;

        [Header("Level solved")]
        [SerializeField] Celebration celebration;
        [Tooltip("Seconds from solving a level to the celebration, so the last block slides out through its gate first.")]
        [SerializeField, Min(0f)] float celebrationDelay = 0.4f;
        [Tooltip("Seconds from solving a level to the next one, so the celebration plays out.")]
        [SerializeField, Min(0f)] float nextLevelDelay = 2.4f;

        Screen _screen;
        int _level = -1; // catalog index of the level being played
        int _section, _page; // the picker page shown or last shown
        bool _busy;      // a switch is animating
        ScreenAnchor _sign, _back;
        readonly Dictionary<GameObject, List<Vector2>> _slots = new(); // per page: the planks' places as laid out in the scene

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
            picker.Setup(catalog != null ? catalog.Count : 0);
        }

        void OnEnable()
        {
            if (board != null) board.Completed += OnCompleted;
            Listen(backButton, Back);
            Listen(continueButton, () => StartLevel(Progress.ContinueLevel(catalog.Count)));
            Listen(pickLevelButton, ShowLevelPicker);
            Listen(settingsButton, ShowSettings);
            picker.SectionPicked += ShowSection;
            picker.LevelPicked += StartLevel;
            picker.PageTurned += TurnPage;
            Listen(musicButton, () => GameSettings.MusicOn = !GameSettings.MusicOn);
            Listen(soundButton, () => GameSettings.SoundOn = !GameSettings.SoundOn);
            GameSettings.Changed += UpdateSettingsLabels;
        }

        void OnDisable()
        {
            if (board != null) board.Completed -= OnCompleted;
            foreach (var button in new[] { backButton, continueButton, pickLevelButton, settingsButton, musicButton, soundButton })
                if (button != null) button.Clicked.RemoveAllListeners();
            GameSettings.Changed -= UpdateSettingsLabels;
            picker.SectionPicked -= ShowSection;
            picker.LevelPicked -= StartLevel;
            picker.PageTurned -= TurnPage;
        }

        static void Listen(WorldButton button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.Clicked.AddListener(action);
        }

        void Start() => ShowMenu();

        public void ShowMenu() => Switch(ToMenu());
        public void ShowSettings() => Switch(ToSettings());

        public void ShowLevelPicker() => Switch(ToSections());

        // Opens a section on the page holding its first unsolved level.
        public void ShowSection(int section) => Switch(ToPage(section, picker.FirstUnsolvedPage(section)));

        void TurnPage(int step)
        {
            int page = _page + step;
            if (_screen == Screen.Page && page >= 0 && page < picker.PageCount(_section)) Switch(TurnPageTo(page));
        }

        public void StartLevel(int index)
        {
            if (catalog != null && index >= 0 && index < catalog.Count) Switch(ToLevel(index));
        }

        public void Back()
        {
            if (_screen == Screen.Page) ShowLevelPicker();
            else if (_screen != Screen.Menu) ShowMenu();
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
            picker.SwipeEnabled = false;
            yield return routine;
            dragger.enabled = _screen == Screen.Level;
            picker.SwipeEnabled = _screen == Screen.Page;
            _busy = false;
        }

        IEnumerator ToMenu()
        {
            _screen = Screen.Menu;
            _level = -1;
            var hide = new List<ScreenAnchor> { _sign, _back };
            hide.AddRange(Planks(settingsPage, false));
            hide.AddRange(picker.AllProps());
            yield return Together(Pop(hide, false), Wait(TakeDownBoard()));
            board.Clear();
            boardCamera.Frame(); // the cleared board has its own framing; the scenery and planks follow it
            SetLabel(continueButton, Progress.HasStarted ? "CONTINUE" : "START");
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

        IEnumerator ToSections()
        {
            _screen = Screen.Sections;
            var hide = MainMenu(false);
            hide.Add(_sign);
            hide.AddRange(picker.PageOnlyProps());
            yield return Pop(hide, false);
            levelSign.Caption = "PICK LEVEL";
            var show = new List<ScreenAnchor> { _sign };
            show.AddRange(picker.SectionProps());
            if (!_back.gameObject.activeSelf) show.Add(_back);
            yield return Pop(show, true);
        }

        IEnumerator ToPage(int section, int page)
        {
            _screen = Screen.Page;
            _section = section;
            _page = page;
            var hide = new List<ScreenAnchor> { _sign };
            hide.AddRange(picker.AllProps());
            yield return Pop(hide, false);
            levelSign.Caption = picker.SectionName(section);
            var show = new List<ScreenAnchor> { _sign };
            show.AddRange(picker.PageProps(section, page));
            yield return Pop(show, true);
        }

        // The sign stays; the tiles pop out and back in with the new page's levels.
        IEnumerator TurnPageTo(int page)
        {
            _page = page;
            yield return Pop(picker.PageOnlyProps(), false);
            yield return Pop(picker.PageProps(_section, page), true);
        }

        IEnumerator ToLevel(int index)
        {
            _screen = Screen.Level;
            _level = index;
            Progress.LastPlayed = index;
            // The back button stays when going from level to level; the sign pops out and back in with the new number.
            // From level to level the old pen comes down first (its blocks are usually gone already).
            var hide = new List<ScreenAnchor>(MainMenu(false)) { _sign };
            hide.AddRange(picker.AllProps());
            yield return Together(Pop(hide, false), Wait(TakeDownBoard()));
            levelSign.Caption = null;
            levelSign.Number = index + 1;
            board.Level = catalog[index];
            board.Initialize(LevelData.Parse(catalog[index].text));
            // The pen goes up first, the animals pop in once the floor is mostly there.
            float pen = BoardAppear.Sweep(board, true, floorGrowTime, fenceSweep, fencePopTime);
            float blocks = BlockAppear.Sweep(board, true, blockSweep, blockPopTime, floorGrowTime * 0.6f);
            var show = new List<ScreenAnchor> { _sign };
            if (!_back.gameObject.activeSelf) show.Add(_back);
            yield return Together(Pop(show, true), Wait(Mathf.Max(pen, blocks)));
        }

        // Shrinks the blocks into the board, then takes the fence and the floor down. Returns the seconds it takes.
        float TakeDownBoard()
        {
            float blocks = BlockAppear.Sweep(board, false, blockSweep * 0.5f, popOutTime);
            float pen = BoardAppear.Sweep(board, false, floorGrowTime * 0.6f, fenceSweep * 0.5f, popOutTime, blocks * 0.5f);
            return Mathf.Max(blocks, pen);
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

        IEnumerator Pop(List<ScreenAnchor> props, bool show)
        {
            int moving = 0; // hiding skips the props not shown
            foreach (var p in props) if (p != null && (show || p.gameObject.activeSelf)) moving++;
            float stagger = moving > 1 ? Mathf.Min(popStagger, maxStaggerSpread / (moving - 1)) : popStagger;
            return PopTransition.Run(props, show, show ? popInTime : popOutTime, stagger);
        }

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

        // The page's planks, top to bottom; with availableOnly, only those that apply now (no Continue without levels).
        List<ScreenAnchor> Planks(GameObject page, bool availableOnly)
        {
            var planks = new List<ScreenAnchor>();
            foreach (Transform child in page.transform)
            {
                if (!child.TryGetComponent<ScreenAnchor>(out var anchor)) continue;
                if (availableOnly && child == continueButton.transform && catalog.Count == 0) continue;
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
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlocksEscape
{
    // The level picker's props; GameFlow decides when they show and animates them. The catalog is split into sections of
    // Levels Per Section, one plank each on the section page (its name and how many of its levels are solved). A section
    // opens as pages of a Columns x Rows grid of level tiles, with arrow buttons and the page's level range below; a
    // horizontal swipe also turns the page.
    //
    // The scene holds one tile, the top left one, laid out as wanted; the others are copies of it placed Tile Spacing
    // apart on screen (viewport units, read inside the safe area like the tile's own anchor).
    [DisallowMultipleComponent]
    public sealed class LevelPicker : MonoBehaviour
    {
        [Header("Sections")]
        [Tooltip("One plank per section, top to bottom.")]
        [SerializeField] GameObject sectionPage;
        [SerializeField, Min(1)] int levelsPerSection = 200;
        [SerializeField] string[] sectionNames = { "MEADOW", "ORCHARD", "POND", "BARNYARD", "HAYLOFT", "HILLTOP" };

        [Header("Level pages")]
        [Tooltip("The top left tile; the rest of the grid is copied from it.")]
        [SerializeField] LevelTile tileTemplate;
        [SerializeField, Min(1)] int columns = 5;
        [SerializeField, Min(1)] int rows = 5;
        [Tooltip("Screen distance between neighbouring tiles' centres, in viewport units.")]
        [SerializeField] Vector2 tileSpacing = new Vector2(0.17f, 0.08f);
        [SerializeField] WorldButton previousButton;
        [SerializeField] WorldButton nextButton;
        [Tooltip("Shows the page's level range, e.g. 26-50.")]
        [SerializeField] WorldLabel rangeLabel;
        [Tooltip("Horizontal drag that turns the page, as a share of the screen width.")]
        [SerializeField, Range(0.02f, 0.5f)] float swipeDistance = 0.12f;

        public event Action<int> SectionPicked; // section index
        public event Action<int> LevelPicked;   // catalog index
        public event Action<int> PageTurned;    // +1 next, -1 previous

        // Turns pages on horizontal swipes while true.
        public bool SwipeEnabled { get; set; }

        public int PageSize => columns * rows;

        readonly List<LevelTile> _tiles = new();
        readonly List<ScreenAnchor> _planks = new();
        int _levelCount;
        Vector2? _swipeFrom;

        // Builds the grid from the template and hides every prop; GameFlow calls it from Awake.
        public void Setup(int levelCount)
        {
            _levelCount = levelCount;
            if (_tiles.Count == 0)
            {
                var first = tileTemplate.GetComponent<ScreenAnchor>();
                var origin = first.ViewportPoint;
                for (int i = 0; i < PageSize; i++)
                {
                    var tile = i == 0 ? tileTemplate : Instantiate(tileTemplate, tileTemplate.transform.parent);
                    tile.name = $"Level Tile {i + 1}";
                    tile.GetComponent<ScreenAnchor>().ViewportPoint =
                        origin + new Vector2(i % columns * tileSpacing.x, -(i / columns) * tileSpacing.y);
                    tile.GetComponent<WorldButton>().Clicked.AddListener(() => { if (tile.Level >= 0) LevelPicked?.Invoke(tile.Level); });
                    _tiles.Add(tile);
                }
                previousButton.Clicked.AddListener(() => PageTurned?.Invoke(-1));
                nextButton.Clicked.AddListener(() => PageTurned?.Invoke(1));
                int index = 0;
                foreach (Transform plank in sectionPage.transform)
                {
                    if (!plank.TryGetComponent<ScreenAnchor>(out var anchor)) continue;
                    int section = index++;
                    plank.GetComponent<WorldButton>().Clicked.AddListener(() => SectionPicked?.Invoke(section));
                    _planks.Add(anchor);
                }
            }
            sectionPage.SetActive(true);
            foreach (var prop in AllProps()) prop.gameObject.SetActive(false);
        }

        public int SectionCount => Mathf.Min(_planks.Count, (_levelCount + levelsPerSection - 1) / levelsPerSection);

        public string SectionName(int section) =>
            section >= 0 && section < sectionNames.Length ? sectionNames[section] : $"SECTION {section + 1}";

        // First catalog index and level count of a section.
        (int first, int count) Section(int section)
        {
            int first = section * levelsPerSection;
            return (first, Mathf.Clamp(_levelCount - first, 0, levelsPerSection));
        }

        public int PageCount(int section) => (Section(section).count + PageSize - 1) / PageSize;

        // The section of a catalog index.
        public int SectionOf(int level) => Mathf.Clamp(level / levelsPerSection, 0, Mathf.Max(0, SectionCount - 1));

        // The page holding the section's first unsolved level (the first page when all are solved).
        public int FirstUnsolvedPage(int section)
        {
            var (first, count) = Section(section);
            for (int i = 0; i < count; i++)
                if (!Progress.IsSolved(first + i)) return i / PageSize;
            return 0;
        }

        // The section planks with their progress, in the order they pop.
        public List<ScreenAnchor> SectionProps()
        {
            var shown = new List<ScreenAnchor>();
            for (int s = 0; s < SectionCount; s++)
            {
                var (first, count) = Section(s);
                int solved = 0;
                for (int i = 0; i < count; i++) if (Progress.IsSolved(first + i)) solved++;
                // Name over progress, small enough that no name needs shrinking, so every plank's lettering matches.
                _planks[s].GetComponent<WorldLabel>().Text = $"<line-height=72%>{SectionName(s)}\n<size=62%>{solved}/{count}</size>";
                shown.Add(_planks[s]);
            }
            return shown;
        }

        // Fills the grid with the page's levels and returns what shows: the used tiles, then the range and the arrows that
        // apply.
        public List<ScreenAnchor> PageProps(int section, int page)
        {
            var (first, count) = Section(section);
            int start = page * PageSize;
            var shown = new List<ScreenAnchor>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (start + i >= count) continue;
                int level = first + start + i;
                _tiles[i].Show(level, Progress.IsSolved(level));
                shown.Add(_tiles[i].GetComponent<ScreenAnchor>());
            }
            rangeLabel.Text = $"{first + start + 1}-{first + Mathf.Min(start + PageSize, count)}";
            shown.Add(rangeLabel.GetComponent<ScreenAnchor>());
            if (page > 0) shown.Add(previousButton.GetComponent<ScreenAnchor>());
            if (page < PageCount(section) - 1) shown.Add(nextButton.GetComponent<ScreenAnchor>());
            return shown;
        }

        // Every prop of the picker, for hiding (hiding skips the ones not shown).
        public List<ScreenAnchor> AllProps()
        {
            var all = new List<ScreenAnchor>(_planks);
            foreach (var tile in _tiles) all.Add(tile.GetComponent<ScreenAnchor>());
            all.Add(rangeLabel.GetComponent<ScreenAnchor>());
            all.Add(previousButton.GetComponent<ScreenAnchor>());
            all.Add(nextButton.GetComponent<ScreenAnchor>());
            return all;
        }

        // The grid's props only, for turning a page.
        public List<ScreenAnchor> PageOnlyProps()
        {
            var props = AllProps();
            props.RemoveRange(0, _planks.Count);
            return props;
        }

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || !SwipeEnabled)
            {
                _swipeFrom = null;
                return;
            }
            var screen = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame) _swipeFrom = screen;
            else if (_swipeFrom.HasValue && pointer.press.wasReleasedThisFrame)
            {
                var delta = screen - _swipeFrom.Value;
                _swipeFrom = null;
                if (Mathf.Abs(delta.x) > swipeDistance * Screen.width && Mathf.Abs(delta.x) > 1.5f * Mathf.Abs(delta.y))
                    PageTurned?.Invoke(delta.x < 0f ? 1 : -1); // dragging left brings the next page
            }
        }
    }
}

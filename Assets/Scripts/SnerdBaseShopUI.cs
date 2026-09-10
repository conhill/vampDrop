using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using Vampire.DropPuzzle;
using Vampire.Player;

namespace Vampire
{
    [RequireComponent(typeof(UIDocument))]
    public class SnerdBaseShopUI : MonoBehaviour
    {
        public static SnerdBaseShopUI Instance { get; private set; }

        [Header("Visuals — drag assets in Inspector")]
        [Tooltip("Background image for the shop screen")]
        public Sprite backgroundSprite;
        [Tooltip("Snerd character sprite shown on the left side")]
        public Sprite snerdSprite;

        [Header("Audio")]
        [Tooltip("Random clip played when the shop opens (shop_twitch_enter_*)")]
        [SerializeField] private AudioClip[] shopEnterClips;
        [Tooltip("Random clip played when the shop closes (shop_twitch_exit_*)")]
        [SerializeField] private AudioClip[] shopExitClips;
        private AudioSource _audio;

        private UIDocument      _doc;
        private VisualElement   _root;
        private Label           _currencyLabel, _riceLabel, _riceballLabel, _craftableLabel;
        private Button          _craftButton;
        private VisualElement   _upgradesContainer;
        private FPSController   _fps;
        private bool            _isOpen;
        public bool             IsOpen => _isOpen;

        // ── Upgrade data ───────────────────────────────────────────────────────
        private struct UpgradeDef
        {
            public string name;
            public string description;
            public string category;
            public Func<string>  getStatus;
            public Func<int>     getCost;
            public Func<bool>    isAvailable;
            public Func<bool>    isMaxed;
            public Action        purchase;
        }

        private List<UpgradeDef> _defs;

        // ── Pages ──────────────────────────────────────────────────────────────
        // The three fixed metal boxes baked into the background are addressed as
        // slots 0-2. Upgrades are shown SLOTS-per-page; Prev/Next just swap which
        // upgrades fill the (never-moving) boxes.
        private const int SLOTS = 3;
        private int _page;
        private readonly Label[]  _slotTitle = new Label[SLOTS];
        private readonly Label[]  _slotDesc  = new Label[SLOTS];
        private readonly Label[]  _slotCost  = new Label[SLOTS];
        private readonly Button[] _slotBuy   = new Button[SLOTS];
        private Button _prevBtn, _nextBtn;
        private Label  _pageLabel;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake  = false;
            _audio.spatialBlend = 0f; // 2D — this is a full-screen UI shop
        }

        private void Start()
        {
            _fps  = FindObjectOfType<FPSController>();

            // UIDocument must stay enabled so the visual tree is never rebuilt.
            // We show/hide by toggling display on the root element instead.
            _root = _doc.rootVisualElement.Q("snerd-shop-root");
            if (_root == null)
            {
                // Fallback: root element IS the visualTreeAsset root
                _root = _doc.rootVisualElement;
                Debug.LogWarning("[SnerdBaseShopUI] snerd-shop-root not found, using doc root");
            }

            ApplySprite("shop-background", backgroundSprite);
            ApplySprite("snerd-image",     snerdSprite);

            _currencyLabel  = _root.Q<Label>("currency-label");
            _riceLabel      = _root.Q<Label>("rice-label");
            _riceballLabel  = _root.Q<Label>("riceball-label");
            _craftableLabel = _root.Q<Label>("craftable-label");
            _craftButton    = _root.Q<Button>("craft-button");
            _upgradesContainer = _root.Q("upgrades-container");

            _craftButton?.RegisterCallback<ClickEvent>(_ => OnCraftClicked());
            _root.Q<Button>("exit-button")?.RegisterCallback<ClickEvent>(_ => Close());

            // Page slots (the three fixed background boxes) + Prev/Next nav.
            for (int i = 0; i < SLOTS; i++)
            {
                _slotTitle[i] = _root.Q<Label>($"slot{i}-title");
                _slotDesc[i]  = _root.Q<Label>($"slot{i}-desc");
                _slotCost[i]  = _root.Q<Label>($"slot{i}-cost");
                _slotBuy[i]   = _root.Q<Button>($"slot{i}-buy");
                int slot = i; // capture for the closure
                _slotBuy[i]?.RegisterCallback<ClickEvent>(_ => OnSlotBuy(slot));
            }
            _prevBtn   = _root.Q<Button>("prev-page");
            _nextBtn   = _root.Q<Button>("next-page");
            _pageLabel = _root.Q<Label>("page-label");
            _prevBtn?.RegisterCallback<ClickEvent>(_ => ChangePage(-1));
            _nextBtn?.RegisterCallback<ClickEvent>(_ => ChangePage(1));

            // Hide via display — keeps the visual tree and all callbacks intact
            _root.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            RefreshStats();
        }

        // ── Public API ─────────────────────────────────────────────────────────
        public void Open()
        {
            // Late-find in case FPSController wasn't ready at Start()
            if (_fps == null) _fps = FindObjectOfType<FPSController>();

            PlayShopSound(shopEnterClips);
            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            EscapeMenuManager.PushEscBlock();
            if (_fps != null) _fps.enabled = false;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible   = true;

            // Build defs once; always reopen on page 0 so the first boxes are populated.
            if (_defs == null) BuildUpgradeDefs();
            _page = 0;
            RefreshPage();
            RefreshStats();
        }

        public void Close()
        {
            PlayShopSound(shopExitClips);
            _isOpen = false;
            _root.style.display = DisplayStyle.None;
            EscapeMenuManager.PopEscBlock();
            if (_fps != null) _fps.enabled = true;
            // Always restore cursor — even if FPS controller was null
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible   = false;
        }

        // ── Crafting ───────────────────────────────────────────────────────────
        private void OnCraftClicked()
        {
            RiceCraftingSystem.Instance?.CraftRiceBalls();
        }

        // ── Stats ──────────────────────────────────────────────────────────────
        private void RefreshStats()
        {
            var pd = PlayerDataManager.Instance;
            if (pd == null) return;

            if (_currencyLabel != null)
                _currencyLabel.text = $"${pd.TotalCurrency * 0.01f:F2}";
            if (_riceLabel != null)
                _riceLabel.text = $"Rice: {pd.RiceGrains}";
            if (_riceballLabel != null)
                _riceballLabel.text = $"Riceballs: {pd.Inventory.GetTotalBalls()}";

            int  craftable = RiceCraftingSystem.Instance?.GetCraftableCount() ?? 0;
            bool crafting  = RiceCraftingSystem.Instance?.IsCrafting()        ?? false;

            if (_craftableLabel != null)
                _craftableLabel.text = crafting ? "Crafting..." : craftable > 0 ? $"Can craft: {craftable}" : "Need 5 rice to craft";
            _craftButton?.SetEnabled(craftable > 0 && !crafting);

            RefreshPage();
        }

        // ── Upgrades ───────────────────────────────────────────────────────────
        private void BuildUpgradeDefs()
        {
            var shop = UpgradeShop.Instance;
            var pd   = PlayerDataManager.Instance;
            _defs = new List<UpgradeDef>();

            if (shop == null || pd == null) return;

            // Snerd sells the FPS-scene rice-COLLECTION upgrades. One entry per upgrade
            // "line": Multi-Pickup handles its own unlock→+1 progression internally, so
            // each maps to exactly one box. Drop-gate upgrades belong to Vorkin.
            Def("Pick Up Radius", "Bigger rice pickup reach", "COLLECTION",
                () => $"{pd.FPSCollector.pickupRadius:F2} / 5.0",
                () => 100 + (int)((pd.FPSCollector.pickupRadius - 1.5f) / 0.25f) * 50,
                () => pd.FPSCollector.pickupRadius < 5.0f,
                () => pd.FPSCollector.pickupRadius >= 5.0f,
                () => shop.BuyPickupRadiusUpgrade());

            Def("Multi-Pickup", "Grab more rice each pickup", "COLLECTION",
                () => pd.FPSCollector.maxSimultaneousPickups <= 1 ? "Locked" : $"{pd.FPSCollector.maxSimultaneousPickups} / 5",
                () => pd.FPSCollector.maxSimultaneousPickups <= 1 ? 400 : 400 + (pd.FPSCollector.maxSimultaneousPickups - 1) * 300,
                () => pd.FPSCollector.maxSimultaneousPickups < 5,
                () => pd.FPSCollector.maxSimultaneousPickups >= 5,
                () => { if (pd.FPSCollector.maxSimultaneousPickups <= 1) shop.UnlockMultiPickup(); else shop.BuyMultiPickupUpgrade(); });

            Def("Magnetic Pull", "Rice flies toward you", "COLLECTION",
                () => pd.FPSCollector.magneticPullEnabled ? "ON" : "Locked",
                () => 800,
                () => !pd.FPSCollector.magneticPullEnabled,
                () => pd.FPSCollector.magneticPullEnabled,
                () => shop.UnlockMagneticPull());

            Def("Move Speed", "Move faster while collecting", "COLLECTION",
                () => $"{pd.FPSCollector.moveSpeedMultiplier:F1}x / 2.0x",
                () => 150 + (int)((pd.FPSCollector.moveSpeedMultiplier - 1.0f) * 10) * 75,
                () => pd.FPSCollector.moveSpeedMultiplier < 2.0f,
                () => pd.FPSCollector.moveSpeedMultiplier >= 2.0f,
                () => shop.BuyMoveSpeedUpgrade());
        }

        private void Def(string name, string description, string category, Func<string> getStatus, Func<int> getCost,
            Func<bool> isAvailable, Func<bool> isMaxed, Action purchase)
        {
            _defs.Add(new UpgradeDef
            {
                name = name, description = description, category = category,
                getStatus = getStatus, getCost = getCost,
                isAvailable = isAvailable, isMaxed = isMaxed,
                purchase = purchase
            });
        }

        // ── Paging ─────────────────────────────────────────────────────────────
        private int PageCount() =>
            (_defs == null || _defs.Count == 0) ? 1 : (_defs.Count + SLOTS - 1) / SLOTS;

        private void ChangePage(int dir)
        {
            _page = Mathf.Clamp(_page + dir, 0, PageCount() - 1);
            RefreshPage();
        }

        private void OnSlotBuy(int slot)
        {
            if (_defs == null) return;
            int idx = _page * SLOTS + slot;
            if (idx < 0 || idx >= _defs.Count) return;

            var def = _defs[idx];
            if (def.isMaxed() || !def.isAvailable()) return;
            def.purchase();
            RefreshStats();
        }

        // Fills the three fixed background boxes with the current page's upgrades and
        // updates the Prev/Next nav. Boxes never move — only their contents change.
        private void RefreshPage()
        {
            if (_defs == null) return;
            var pd = PlayerDataManager.Instance;
            int currency = pd?.TotalCurrency ?? 0;

            int pageCount = PageCount();
            _page = Mathf.Clamp(_page, 0, pageCount - 1);

            for (int i = 0; i < SLOTS; i++)
            {
                int idx = _page * SLOTS + i;

                if (idx >= _defs.Count)
                {
                    // No upgrade for this box on the last page — blank it, hide its button.
                    if (_slotTitle[i] != null) _slotTitle[i].text = "";
                    if (_slotDesc[i]  != null) _slotDesc[i].text  = "";
                    if (_slotCost[i]  != null) _slotCost[i].text  = "";
                    if (_slotBuy[i]   != null) _slotBuy[i].style.display = DisplayStyle.None;
                    continue;
                }

                var def   = _defs[idx];
                bool maxed = def.isMaxed();
                bool avail = def.isAvailable();
                int  cost  = def.getCost();
                bool canAfford = currency >= cost;

                if (_slotTitle[i] != null) _slotTitle[i].text = def.name;
                if (_slotDesc[i]  != null) _slotDesc[i].text  = def.description;
                if (_slotCost[i]  != null)
                    _slotCost[i].text = maxed ? "MAXED" : !avail ? "Locked" : $"${cost} Skrilla";

                if (_slotBuy[i] != null)
                {
                    _slotBuy[i].style.display = DisplayStyle.Flex;
                    if (maxed)           { _slotBuy[i].text = "✓";      _slotBuy[i].SetEnabled(false); }
                    else if (!avail)     { _slotBuy[i].text = "Locked"; _slotBuy[i].SetEnabled(false); }
                    else if (!canAfford) { _slotBuy[i].text = "Need $"; _slotBuy[i].SetEnabled(false); }
                    else                 { _slotBuy[i].text = "Buy";    _slotBuy[i].SetEnabled(true);  }
                }
            }

            if (_pageLabel != null) _pageLabel.text = $"Page {_page + 1} / {pageCount}";
            _prevBtn?.SetEnabled(_page > 0);
            _nextBtn?.SetEnabled(_page < pageCount - 1);
        }

        // ── Audio ──────────────────────────────────────────────────────────────
        private void PlayShopSound(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0 || _audio == null) return;
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip != null) _audio.PlayOneShot(clip);
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private void ApplySprite(string elementName, Sprite sprite)
        {
            if (sprite == null) return;
            var el = _root?.Q(elementName);
            if (el != null) el.style.backgroundImage = new StyleBackground(sprite);
        }
    }
}

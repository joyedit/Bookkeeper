using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Bookkeeper
{
    // --- DATA DEFINITIONS ---
    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketBookkeeperRequest { }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketBookkeeperResponse
    {
        public List<BookkeeperItemDTO> Items = new List<BookkeeperItemDTO>();
    }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class BookkeeperItemDTO
    {
        public string Code;
        public int Count;
        public string Type;
        public List<SimplePos> Locations = new List<SimplePos>();
    }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class SimplePos { public int X, Y, Z; }

    public class BookkeeperEntry
    {
        public ItemStack Stack;
        public List<BlockPos> Locations;
    }

    public class GuiDialogBookkeeper : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        private BookkeeperModSystem modSystem;
        private List<BookkeeperEntry> allEntries = new List<BookkeeperEntry>();
        private List<BookkeeperEntry> filteredEntries = new List<BookkeeperEntry>();
        private List<BookkeeperEntry> currentVisibleEntries = new List<BookkeeperEntry>();
        private InventoryGeneric virtualInventory;

        private string currentSearchText = "";
        private int currentPage = 0;

        // LAYOUT: 10 Columns, 9 Rows (90 Items)
        private const int COLS = 10;
        private int itemsPerPage = 90;

        private bool isWaitingForServer = false;
        private long openTime = 0;

        public GuiDialogBookkeeper(ICoreClientAPI capi, BookkeeperModSystem system) : base(capi)
        {
            this.modSystem = system;
        }

        public void UpdateDataFromServer(List<BookkeeperItemDTO> data)
        {
            allEntries.Clear();
            foreach (var dto in data)
            {
                try
                {
                    AssetLocation code = new AssetLocation(dto.Code);
                    CollectibleObject collectible = (dto.Type == "Block") ? (CollectibleObject)capi.World.GetBlock(code) : (CollectibleObject)capi.World.GetItem(code);

                    if (collectible != null)
                    {
                        ItemStack stack = new ItemStack(collectible, dto.Count);
                        List<BlockPos> locs = dto.Locations.Select(p => new BlockPos(p.X, p.Y, p.Z)).ToList();
                        allEntries.Add(new BookkeeperEntry() { Stack = stack, Locations = locs });
                    }
                }
                catch { }
            }

            allEntries.Sort((a, b) => string.Compare(a.Stack.GetName(), b.Stack.GetName(), StringComparison.OrdinalIgnoreCase));
            isWaitingForServer = false;
            FilterItems(currentSearchText);

            if (IsOpened()) ComposeDialog();
        }

        private void FilterItems(string searchText)
        {
            currentSearchText = searchText.ToLower();
            currentPage = 0;
            filteredEntries = string.IsNullOrEmpty(currentSearchText)
                ? new List<BookkeeperEntry>(allEntries)
                : allEntries.Where(e => e.Stack.GetName()?.ToLower().Contains(currentSearchText) == true).ToList();
            UpdateView();
        }

        private void UpdateView()
        {
            int skip = currentPage * itemsPerPage;
            currentVisibleEntries = filteredEntries.Skip(skip).Take(itemsPerPage).ToList();
            virtualInventory = new InventoryGeneric(Math.Max(1, currentVisibleEntries.Count), "bookkeeper-grid", capi, null);

            for (int i = 0; i < currentVisibleEntries.Count; i++)
            {
                virtualInventory[i].Itemstack = currentVisibleEntries[i].Stack;
            }
        }

        public void ComposeDialog()
        {
            if (virtualInventory == null) virtualInventory = new InventoryGeneric(1, "bookkeeper-init", capi, null);

            double windowWidth = 820;
            double gridWidth = 480;
            double leftMargin = 170;
            double windowHeight = 620;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, windowWidth, windowHeight);

            ElementBounds searchBounds = ElementBounds.Fixed(leftMargin, 45, gridWidth, 30);

            // HEIGHT: 435px (fits 9 rows) to clear buttons
            ElementBounds gridBounds = ElementBounds.Fixed(leftMargin, 85, gridWidth + 5, 435);

            ElementBounds prevButtonBounds = ElementBounds.Fixed(leftMargin, 575, 80, 30);
            ElementBounds nextButtonBounds = ElementBounds.Fixed(leftMargin + gridWidth - 80, 575, 80, 30);
            ElementBounds statusLabelBounds = ElementBounds.Fixed(windowWidth / 2 - 150, 580, 300, 30);

            int totalPages = (int)Math.Ceiling((double)filteredEntries.Count / itemsPerPage);
            if (totalPages < 1) totalPages = 1;

            string statusText = isWaitingForServer ? "Scanning Storage..." : $"Page {currentPage + 1} / {totalPages} ({filteredEntries.Count} Items)";

            SingleComposer = capi.Gui.CreateCompo("bookkeeperdialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Bookkeeper's Ledger", OnTitleBarClose)
                .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "searchBar")
                .AddItemSlotGrid(virtualInventory, OnSlotClick, COLS, gridBounds, "itemgrid")
                .AddSmallButton("Prev", OnPrevPage, prevButtonBounds)
                .AddSmallButton("Next", OnNextPage, nextButtonBounds)
                .AddDynamicText(statusText, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), statusLabelBounds, "statusLabel")
                .Compose();

            // Disable the grid's built-in click handling so a held cursor item can never be
            // moved or destroyed; all clicks are handled in OnMouseDown (locate only).
            var slotGrid = SingleComposer.GetSlotGrid("itemgrid");
            if (slotGrid != null) slotGrid.CanClickSlot = (slotId) => false;

            if (!string.IsNullOrEmpty(currentSearchText))
                SingleComposer.GetTextInput("searchBar").SetValue(currentSearchText);
        }

        private void OnSearchChanged(string text)
        {
            if (capi.World.ElapsedMilliseconds - openTime < 500 && !string.IsNullOrEmpty(text)) return;
            if (text == currentSearchText) return;
            FilterItems(text);
            ComposeDialog();
            SingleComposer.FocusElement(SingleComposer.GetTextInput("searchBar").TabIndex);
        }

        private bool OnPrevPage()
        {
            if (currentPage > 0) { currentPage--; UpdateView(); ComposeDialog(); }
            return true;
        }

        private bool OnNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)filteredEntries.Count / itemsPerPage);
            if (currentPage < totalPages - 1) { currentPage++; UpdateView(); ComposeDialog(); }
            return true;
        }

        // Grid click handling is disabled (CanClickSlot=false); this no-op satisfies
        // AddItemSlotGrid's signature. All clicks are routed through OnMouseDown.
        private void OnSlotClick(object packet) { }

        public override void OnMouseDown(MouseEvent args)
        {
            var grid = SingleComposer?.GetSlotGrid("itemgrid");
            if (!args.Handled && grid != null && grid.Bounds.PointInside(args.X, args.Y))
            {
                // Read-only ledger: never move items. If the player is holding something on
                // the cursor, do nothing — and crucially never clear the cursor. (The old
                // OnSlotClick wiped whatever was on the cursor, which could destroy a held item.)
                var mouse = capi.World.Player.InventoryManager.MouseItemSlot;
                if (mouse?.Itemstack != null) { args.Handled = true; return; }

                // hoverSlotId only refreshes on mouse-move and resets to -1 on recompose, so
                // recompute it from the actual click position before reading it.
                grid.OnMouseMove(capi, args);
                int slotId = grid.hoverSlotId;
                if (slotId >= 0 && slotId < currentVisibleEntries.Count)
                {
                    var entry = currentVisibleEntries[slotId];
                    if (entry.Locations.Count > 0)
                    {
                        modSystem.SetHighlights(entry.Locations, entry.Stack.GetName());
                        TryClose();
                    }
                    else
                    {
                        capi.ShowChatMessage("Bookkeeper: no known location for that item.");
                    }
                }

                args.Handled = true;
                return;
            }

            base.OnMouseDown(args);
        }

        private void OnTitleBarClose() => TryClose();

        public override void OnGuiOpened()
        {
            openTime = capi.World.ElapsedMilliseconds;
            isWaitingForServer = true;
            if (virtualInventory == null) virtualInventory = new InventoryGeneric(1, "bookkeeper-init", capi, null);
            ComposeDialog();
            BookkeeperModSystem.clientChannel?.SendPacket(new PacketBookkeeperRequest());
            SingleComposer.FocusElement(-1);
        }

        public bool IsLookingAtStation()
        {
            var blockSel = capi.World.Player.CurrentBlockSelection;
            if (blockSel?.Block == null) return false;
            string path = blockSel.Block.Code.Path;
            // No foundation requirement — the lectern works anywhere.
            return path.Contains("bookkeeperlectern") || path.Contains("manifestboard");
        }
    }
}

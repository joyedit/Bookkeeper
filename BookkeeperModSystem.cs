using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Bookkeeper
{
    public class BookkeeperModSystem : ModSystem
    {
        public static IClientNetworkChannel clientChannel;
        public static IServerNetworkChannel serverChannel;
        public static GuiDialogBookkeeper dialog;

        private List<BlockPos> activeHighlights = new List<BlockPos>();
        private WaypointMapLayer wpLayer;
        private ContainerLabelRenderer labelRenderer;
        private ICoreClientAPI capi;
        private static BookkeeperConfig config;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("BlockBookkeeperLectern", typeof(BlockBookkeeperLectern));

            api.Network.RegisterChannel("bookkeeper")
               .RegisterMessageType(typeof(PacketBookkeeperRequest))
               .RegisterMessageType(typeof(PacketBookkeeperResponse))
               .RegisterMessageType(typeof(SimplePos));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            clientChannel = capi.Network.GetChannel("bookkeeper")
                .SetMessageHandler<PacketBookkeeperResponse>(OnBookkeeperDataReceived);

            dialog = new GuiDialogBookkeeper(capi, this);

            capi.Input.RegisterHotKey("bookkeeper", "Open Storage Bookkeeper", GlKeys.K, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("bookkeeper", OnHotKey);

            // Uses GameTickListener for instant click detection/removal
            capi.Event.RegisterGameTickListener(OnClientTick, 50);

            // Register the floating label renderer for through-wall container labels
            labelRenderer = new ContainerLabelRenderer(capi);
            capi.Event.RegisterRenderer(labelRenderer, EnumRenderStage.Ortho);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            try
            {
                config = sapi.LoadModConfig<BookkeeperConfig>("BookkeeperConfig.json");
            }
            catch { }

            if (config == null)
            {
                config = new BookkeeperConfig();
            }

            sapi.StoreModConfig(config, "BookkeeperConfig.json");

            serverChannel = sapi.Network.GetChannel("bookkeeper")
                .SetMessageHandler<PacketBookkeeperRequest>(OnClientRequest);
        }

        private bool OnHotKey(KeyCombination comb)
        {
            if (dialog == null) return true;
            if (dialog.IsOpened()) dialog.TryClose();
            else if (dialog.IsLookingAtStation()) dialog.TryOpen();
            // IsLookingAtStation will show appropriate error message if needed
            return true;
        }

        // --- INSTANT REMOVAL LOGIC ---
        private void OnClientTick(float dt)
        {
            if (!capi.Input.MouseButton.Right) return;

            var blockSel = capi.World.Player.CurrentBlockSelection;
            if (blockSel?.Position == null) return;

            int removed = activeHighlights.RemoveAll(p =>
                p.X == blockSel.Position.X &&
                p.Y == blockSel.Position.Y &&
                p.Z == blockSel.Position.Z);

            if (removed > 0)
            {
                RefreshHighlights();
            }
        }

        public void SetHighlights(List<BlockPos> positions, string itemName = null, Dictionary<string, int> perLocationCounts = null)
        {
            // Clear previous highlights
            activeHighlights.Clear();
            activeHighlights.AddRange(positions);
            RefreshHighlights();

            // Build and display floating labels
            if (labelRenderer != null && itemName != null)
            {
                var labelList = new List<ContainerLabel>();
                foreach (var pos in positions)
                {
                    string key = $"{pos.X},{pos.Y},{pos.Z}";
                    int count = 0;
                    perLocationCounts?.TryGetValue(key, out count);

                    string labelText = count > 0 ? $"{itemName} x{count}" : itemName;

                    labelList.Add(new ContainerLabel
                    {
                        X = pos.X,
                        Y = pos.Y,
                        Z = pos.Z,
                        Text = labelText
                    });
                }
                labelRenderer.SetLabels(labelList);
            }
            else if (labelRenderer != null)
            {
                labelRenderer.ClearLabels();
            }

            // Lazy-load the waypoint layer
            if (wpLayer == null)
            {
                try
                {
                    var mapManager = capi.ModLoader.GetModSystem<WorldMapManager>();
                    if (mapManager?.MapLayers != null)
                    {
                        wpLayer = mapManager.MapLayers.OfType<WaypointMapLayer>().FirstOrDefault();
                    }
                }
                catch (Exception ex)
                {
                    capi.ShowChatMessage($"Bookkeeper: Could not access waypoint system: {ex.Message}");
                }
            }

            // Create temporary waypoints visible through walls
            if (wpLayer != null)
            {
                foreach (var pos in positions)
                {
                    var wp = new Waypoint
                    {
                        Position = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5),
                        Title = itemName ?? "Bookkeeper",
                        Icon = "circle",
                        Color = ColorUtil.ToRgba(255, 51, 153, 255),
                        ShowInWorld = true,
                        Pinned = false,
                        Temporary = true,
                        OwningPlayerUid = capi.World.Player.PlayerUID
                    };
                    wpLayer.AddTemporaryWaypoint(wp);
                }
                capi.ShowChatMessage($"Locating {positions.Count} containers. Look for floating labels through walls.");
            }
            else
            {
                capi.ShowChatMessage($"Locating {positions.Count} containers. Look for floating labels.");
            }
        }

        private void ClearBookkeeperWaypoints()
        {
        }

        private void RefreshHighlights()
        {
            // BLUE: (Alpha=150, Blue=255, Green=0, Red=0)
            int blue = ColorUtil.ToRgba(150, 255, 0, 0);

            if (activeHighlights.Count > 0) {
                List<int> colors = activeHighlights.Select(_ => blue).ToList();
                capi.World.HighlightBlocks(capi.World.Player, 56, activeHighlights, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);
            } else {
                capi.World.HighlightBlocks(capi.World.Player, 56, new List<BlockPos>(), new List<int>());
                // Also clear floating labels when all highlights are dismissed
                labelRenderer?.ClearLabels();
            }
        }

        private void OnClientRequest(IServerPlayer player, PacketBookkeeperRequest packet)
        {
            Dictionary<string, BookkeeperItemDTO> consolidated = new Dictionary<string, BookkeeperItemDTO>();
            BlockPos pPos = player.Entity.Pos.AsBlockPos;
            int radius = config.ChunkRadius;
            int vertRange = config.VerticalRange;

            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++) {
                    int chunkX = pPos.X / 32 + x;
                    int chunkZ = pPos.Z / 32 + z;
                    int minChunkY = Math.Max(0, pPos.Y - vertRange) / 32;
                    int maxChunkY = Math.Min(player.Entity.World.BlockAccessor.MapSizeY, pPos.Y + vertRange) / 32;

                    for (int y = minChunkY; y <= maxChunkY; y++) {
                        IWorldChunk chunk = player.Entity.World.BlockAccessor.GetChunk(chunkX, y, chunkZ);
                        if (chunk == null) continue;
                        foreach (var entry in chunk.BlockEntities) {
                            // Standard containers (chests, vessels, crates, barrels, etc.)
                            if (entry.Value is IBlockEntityContainer container && container.Inventory != null)
                            {
                                ScanInventory(container.Inventory, consolidated, entry.Key);
                            }
                            // Display entities (tool racks, display cases, shelves, etc.)
                            // These don't implement IBlockEntityContainer but have an Inventory property.
                            else
                            {
                                // Try public "Inventory" property first
                                var invProp = entry.Value.GetType().GetProperty("Inventory");
                                if (invProp != null)
                                {
                                    var inv = invProp.GetValue(entry.Value) as IInventory;
                                    if (inv != null)
                                    {
                                        ScanInventory(inv, consolidated, entry.Key);
                                        continue;
                                    }
                                }

                                // Fallback: try "inventory" field (some VS classes use lowercase)
                                var invField = entry.Value.GetType().GetField("inventory",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (invField != null)
                                {
                                    var inv = invField.GetValue(entry.Value) as IInventory;
                                    if (inv != null)
                                    {
                                        ScanInventory(inv, consolidated, entry.Key);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            serverChannel.SendPacket(new PacketBookkeeperResponse { Items = consolidated.Values.ToList() }, player);
        }

        private void ScanInventory(IInventory inventory, Dictionary<string, BookkeeperItemDTO> list, BlockPos pos)
        {
            foreach (var slot in inventory) {
                if (slot?.Itemstack == null) continue;
                string code = slot.Itemstack.Collectible.Code.ToString();
                if (!list.ContainsKey(code))
                    list[code] = new BookkeeperItemDTO { Code = code, Count = 0, Type = slot.Itemstack.Class.ToString() };

                list[code].Count += slot.Itemstack.StackSize;
                if (list[code].Locations.Count < 20 && !list[code].Locations.Any(l => l.X == pos.X && l.Y == pos.Y && l.Z == pos.Z))
                    list[code].Locations.Add(new SimplePos { X = pos.X, Y = pos.Y, Z = pos.Z });
            }
        }

        private void OnBookkeeperDataReceived(PacketBookkeeperResponse packet) => dialog?.UpdateDataFromServer(packet.Items);
    }
}

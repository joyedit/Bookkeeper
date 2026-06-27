using Vintagestory.API.Common;

namespace Bookkeeper
{
    public class BlockBookkeeperLectern : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side != EnumAppSide.Client) return true;

            // No foundation requirement — the lectern works anywhere. Just toggle the ledger.
            var dialog = BookkeeperModSystem.dialog;
            if (dialog != null)
            {
                if (dialog.IsOpened())
                    dialog.TryClose();
                else
                    dialog.TryOpen();
            }

            return true;
        }
    }
}

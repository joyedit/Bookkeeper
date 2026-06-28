namespace Bookkeeper
{
    public class BookkeeperConfig
    {
        // Vertical range in blocks above and below the player position
        public int VerticalRange { get; set; } = 5;

        // Horizontal scan radius in chunks (1 chunk = 32 blocks)
        public int ChunkRadius { get; set; } = 2;

        // When true, the ledger honors land claims: containers the player isn't allowed to use
        // (e.g. inside someone else's claim) are hidden. Owners/granted players and unclaimed
        // land are unaffected. Defers to the game's own claim permissions.
        public bool HonorClaims { get; set; } = true;
    }
}

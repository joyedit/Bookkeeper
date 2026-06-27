namespace Bookkeeper
{
    public class BookkeeperConfig
    {
        // Vertical range in blocks above and below the player position
        public int VerticalRange { get; set; } = 5;

        // Horizontal scan radius in chunks (1 chunk = 32 blocks)
        public int ChunkRadius { get; set; } = 2;
    }
}

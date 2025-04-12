namespace ModernScreenshotTool.Models
{
    public class Resolution
    {
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
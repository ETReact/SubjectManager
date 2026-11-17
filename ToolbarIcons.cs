using System.Drawing;
using System.Drawing.Text;

namespace SubjectManager
{
    /// <summary>
    /// Icone della toolbar disegnate a runtime con il font di icone di Windows
    /// (Segoe Fluent Icons su Windows 11, Segoe MDL2 Assets su Windows 10).
    /// </summary>
    internal static class ToolbarIcons
    {
        public const char Close = '';
        public const char Refresh = '';
        public const char Save = '';
        public const char Undo = '';
        public const char MoveToFolder = '';
        public const char View = '';
        public const char Rename = '';
        public const char Filter = '';
        public const char Flag = '';
        public const char Tree = '';
        public const char Add = '';
        public const char Delete = '';
        public const char Export = '';

        private static readonly string FontName = ResolveFontName();

        private static string ResolveFontName()
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                using (var f = new Font(name, 10f))
                {
                    if (f.Name == name) return name;
                }
            }
            return null;
        }

        /// <summary>Restituisce un'icona 16x16 o null se il font non è disponibile.</summary>
        public static Image Create(char glyph, Color color)
        {
            if (FontName == null) return null;

            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontName, 11f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, 16, 16), format);
            }
            return bmp;
        }
    }
}

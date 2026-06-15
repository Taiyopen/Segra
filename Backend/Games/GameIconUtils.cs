using System.Drawing;
using System.Drawing.Imaging;

namespace Segra.Backend.Games
{
    internal static class GameIconUtils
    {
        /// <summary>
        /// Returns a PNG data URL for the executable's associated icon, or null if unavailable.
        /// </summary>
        public static string? ExtractIconAsBase64(string? exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return null;

            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon == null)
                    return null;

                using var bitmap = icon.ToBitmap();
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            }
            catch
            {
                return null;
            }
        }
    }
}

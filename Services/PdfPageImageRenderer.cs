using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Docnet.Core;
using Docnet.Core.Models;

namespace InvoiceManager.Services;

public sealed class PdfPageImageRenderer
{
    public const int DefaultDpi = 144;
    public static IReadOnlyList<int> SupportedDpiValues { get; } = new[] { 72, 96, 120, 144, 192, 240, 300 };

    public List<string> RenderAsBase64Images(string pdfPath, int imageDpi, CancellationToken cancellationToken)
    {
        if (!SupportedDpiValues.Contains(imageDpi))
        {
            throw new ArgumentOutOfRangeException(nameof(imageDpi), "A megadott DPI-érték nem támogatott.");
        }

        var images = new List<string>();
        using var document = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(imageDpi / 72.0));
        var pageCount = document.GetPageCount();
        for (var index = 0; index < pageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = document.GetPageReader(index);
            var width = page.GetPageWidth();
            var height = page.GetPageHeight();
            var pixels = page.GetImage();

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bounds = new Rectangle(0, 0, width, height);
            var bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Jpeg);
            images.Add(System.Convert.ToBase64String(output.ToArray()));
        }

        if (images.Count == 0)
        {
            throw new InvalidDataException("A PDF nem tartalmaz feldolgozható oldalt.");
        }

        return images;
    }
}

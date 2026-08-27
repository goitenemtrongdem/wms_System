using System.Drawing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace AsrsWarehouse.Services;

public class QrCodeReaderService : IQrCodeReaderService
{
    public string? Decode(string imagePath)
    {
        using var bitmap = new Bitmap(imagePath);
        var reader = new ZXing.Windows.Compatibility.BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
                TryInverted = true
            }
        };

        return reader.Decode(bitmap)?.Text?.Trim();
    }
}

namespace AsrsWarehouse.Services;

public interface IQrCodeReaderService
{
    string? Decode(string imagePath);
}

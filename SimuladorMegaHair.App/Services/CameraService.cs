using System.Runtime.InteropServices;

namespace SimuladorMegaHair.App.Services;

public class CameraService
{
    /// <summary>
    /// Abre a câmera e retorna o caminho local da foto tirada.
    /// </summary>
    public async Task<string?> TirarFotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            await Shell.Current.DisplayAlert(
                "Câmera",
                "Este dispositivo não suporta captura de fotos.",
                "OK");
            return null;
        }

        var photo = await MediaPicker.Default.CapturePhotoAsync();

        if (photo is null)
            return null;

        var localFilePath = Path.Combine(FileSystem.CacheDirectory, photo.FileName);

        await using var sourceStream = await photo.OpenReadAsync();
        await using var localFileStream = File.OpenWrite(localFilePath);

        await sourceStream.CopyToAsync(localFileStream);

        return localFilePath;
    }

    /// <summary>
    /// Alternativa: selecionar foto da galeria.
    /// </summary>
    public async Task<string?> SelecionarDaGaleriaAsync()
{
        try
        {
            // Força execução na UI thread
            var photo = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await MediaPicker.Default.PickPhotoAsync();
            });

            if (photo is null)
                return null;

            var extension = Path.GetExtension(photo.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var localFilePath = Path.Combine(FileSystem.CacheDirectory, uniqueFileName);

            await using var sourceStream = await photo.OpenReadAsync();
            await using var localFileStream = File.Create(localFilePath);

            await sourceStream.CopyToAsync(localFileStream);

            return localFilePath;
        }
        catch (COMException comEx)
        {
            System.Diagnostics.Debug.WriteLine($"[COMException] HResult=0x{comEx.HResult:X8} Msg={comEx.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack: {comEx.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"Platform: {DeviceInfo.Platform}, Version: {DeviceInfo.VersionString}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Erro]: {ex.Message}");
            return null;
        }
    }
}
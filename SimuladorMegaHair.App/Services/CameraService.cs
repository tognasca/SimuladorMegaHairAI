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
        var photo = await MediaPicker.Default.PickPhotoAsync();

        if (photo is null)
            return null;

        var localFilePath = Path.Combine(FileSystem.CacheDirectory, photo.FileName);

        await using var sourceStream = await photo.OpenReadAsync();
        await using var localFileStream = File.OpenWrite(localFilePath);

        await sourceStream.CopyToAsync(localFileStream);

        return localFilePath;
    }
}
using System.Runtime.InteropServices;

namespace SimuladorMegaHair.App.WinUI;

/// <summary>
/// Gerencia a inicialização segura do Windows App SDK com tratamento robusto de erros.
/// Evita falhas de COM durante a inicialização automática do Module Initializer.
/// </summary>
internal static class WindowsAppSDKInitializer
{
    private static bool _initialized = false;
    private static Exception? _initializationError = null;

    /// <summary>
    /// Inicializa o Windows App SDK de forma segura.
    /// Deve ser chamado no construtor da App ou no OnStart.
    /// </summary>
    public static bool TryInitialize(out Exception? error)
    {
        error = null;

        if (_initialized)
            return _initializationError == null;

        try
        {
            // Tenta inicializar o Windows App SDK
            var options = new global::Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentInitializeOptions();
            var deploymentResult = global::Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManager.Initialize(options);

            if (deploymentResult.Status != global::Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentStatus.Ok)
            {
                int hr = deploymentResult.ExtendedError.HResult;
                _initializationError = new InvalidOperationException(
                    $"WindowsAppRuntime.DeploymentManager.Initialize retornou status de erro: 0x{hr:X}",
                    deploymentResult.ExtendedError);
                error = _initializationError;
                return false;
            }

            _initialized = true;
            return true;
        }
        catch (COMException comEx) when (comEx.HResult == unchecked((int)0x80040154)) // REGDB_E_CLASSNOTREG
        {
            _initializationError = new InvalidOperationException(
                "Windows App SDK não está registrado corretamente no sistema. " +
                "Por favor, instale o Windows App SDK Runtime.",
                comEx);
            error = _initializationError;
            _initialized = true;
            return false;
        }
        catch (TypeInitializationException tiEx)
        {
            _initializationError = new InvalidOperationException(
                "Falha ao inicializar tipos WinRT. " +
                "Isso geralmente indica que o Windows App SDK Runtime não está instalado ou está corrompido.",
                tiEx);
            error = _initializationError;
            _initialized = true;
            return false;
        }
        catch (Exception ex)
        {
            _initializationError = ex;
            error = ex;
            _initialized = true;
            return false;
        }
    }

    public static Exception? GetInitializationError() => _initializationError;
    public static bool IsInitialized => _initialized;
}

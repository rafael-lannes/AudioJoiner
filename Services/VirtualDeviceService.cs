using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using AudioJoiner.Models;
using NAudio.CoreAudioApi;

namespace AudioJoiner.Services;

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, out IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bDefault, out IntPtr ppFormat);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bDefault, out long pmftDefaultPeriod, out long pmftMinimumPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ref long pmftPeriod);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, out IntPtr pMode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ref PropertyKey key, out IntPtr pv);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ref PropertyKey key, ref IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bVisible);
}

[ComImport]
[Guid("294fcb00-1f11-4bb7-b45e-10c15a3d05e5")]
internal class PolicyConfigClient
{
}

public class VirtualDeviceService
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    private static readonly string[] DownloadUrls = new[]
    {
        "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip",
        "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip"
    };

    public bool IsVirtualDriverInstalled()
    {
        return GetAvailableVirtualDevices().Count > 0;
    }

    public List<AudioDeviceInfo> GetAvailableVirtualDevices()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            var endpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                string name = endpoint.FriendlyName.ToLowerInvariant();
                if (name.Contains("cable") || name.Contains("virtual") || name.Contains("vb-audio") ||
                    name.Contains("vac") || name.Contains("audiojoiner") || name.Contains("hi-fi"))
                {
                    list.Add(new AudioDeviceInfo
                    {
                        Id = endpoint.ID,
                        Name = endpoint.FriendlyName,
                        IconType = DeviceIconType.General,
                        IsAvailable = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating virtual devices: {ex.Message}");
        }

        return list;
    }

    public bool SetAsDefaultWindowsPlaybackDevice(string deviceId)
    {
        try
        {
            var policyConfig = (IPolicyConfigVista)new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set default device via IPolicyConfig: {ex.Message}");
            try
            {
                Process.Start(new ProcessStartInfo("control", "mmsys.cpl sounds") { UseShellExecute = true });
            }
            catch { }
            return false;
        }
    }

    public async Task<bool> DownloadAndInstallVirtualDriverAsync(Action<string> statusCallback)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "AudioJoiner_VBCable");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "VBCABLE_Driver_Pack.zip");
            byte[]? zipBytes = null;

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                foreach (var url in DownloadUrls)
                {
                    try
                    {
                        statusCallback("Baixando driver oficial de áudio virtual (VB-CABLE)...");
                        var response = await client.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            zipBytes = await response.Content.ReadAsByteArrayAsync();
                            break;
                        }
                    }
                    catch
                    {
                        // Tentar próxima URL
                    }
                }
            }

            if (zipBytes == null || zipBytes.Length == 0)
            {
                statusCallback("Falha no download automático. Abrindo página oficial de download...");
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/index.htm") { UseShellExecute = true });
                return false;
            }

            await File.WriteAllBytesAsync(zipPath, zipBytes);

            statusCallback("Extraindo instalador do driver...");
            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            string installerPath = Environment.Is64BitOperatingSystem
                ? Path.Combine(tempDir, "VBCABLE_Setup_x64.exe")
                : Path.Combine(tempDir, "VBCABLE_Setup.exe");

            if (!File.Exists(installerPath))
            {
                installerPath = Path.Combine(tempDir, "VBCABLE_Setup.exe");
            }

            if (File.Exists(installerPath))
            {
                statusCallback("Executando instalador como Administrador...");
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                statusCallback("Instalador iniciado! Clique em 'Install Driver' e reinicie o áudio após concluir.");
                return true;
            }

            statusCallback("Instalador não encontrado no pacote extraído.");
            return false;
        }
        catch (Exception ex)
        {
            statusCallback($"Erro: {ex.Message}. Abrindo página oficial...");
            try
            {
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/index.htm") { UseShellExecute = true });
            }
            catch { }
            return false;
        }
    }

    public void OpenWindowsSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("control", "mmsys.cpl sounds") { UseShellExecute = true });
            }
            catch { }
        }
    }
}

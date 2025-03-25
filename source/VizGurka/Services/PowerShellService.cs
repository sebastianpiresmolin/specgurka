using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VizGurka.Services
{
    public class PowerShellService
    {
        private readonly ILogger<PowerShellService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _scriptPath;
        private readonly string _configPath;

        public PowerShellService(ILogger<PowerShellService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Set paths when service is created
            _scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "fetch_github_artifacts.ps1");
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        }

        public async Task<(bool Success, string Output, string Error)> RunScriptAsync()
        {
            try
            {
                if (!File.Exists(_scriptPath))
                {
                    _logger.LogError("PowerShell script not found at {ScriptPath}", _scriptPath);
                    return (false, string.Empty, $"Script not found at {_scriptPath}");
                }

                if (!File.Exists(_configPath))
                {
                    _logger.LogError("Configuration file not found at {ConfigPath}", _configPath);
                    return (false, string.Empty, $"Config not found at {_configPath}");
                }

                _logger.LogInformation("Running script: {ScriptPath}", _scriptPath);
                _logger.LogInformation("With config: {ConfigPath}", _configPath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -ConfigPath \"{_configPath}\" -Debug",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        _logger.LogError("Failed to start PowerShell process");
                        return (false, string.Empty, "Failed to start PowerShell process");
                    }

                    // Read output asynchronously
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    // Wait for the process to exit with a timeout
                    bool exited = await Task.Run(() => process.WaitForExit(30000)); // 30 second timeout

                    if (!exited)
                    {
                        _logger.LogWarning("PowerShell script execution timed out");
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error killing timed out process");
                        }
                        return (false, output, "Script execution timed out");
                    }

                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogWarning("PowerShell script reported errors: {Error}", error);
                    }

                    bool success = process.ExitCode == 0;
                    _logger.LogInformation("PowerShell script execution {Result} with exit code {ExitCode}", 
                        success ? "succeeded" : "failed", process.ExitCode);

                    return (success, output, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception running PowerShell script");
                return (false, string.Empty, ex.ToString());
            }
        }
    }
}
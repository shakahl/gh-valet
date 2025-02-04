﻿using System.Diagnostics;
using Valet.Interfaces;

namespace Valet.Services;

public class ProcessService : IProcessService
{
    public async Task RunAsync(
        string filename,
        string arguments,
        string? cwd = null,
        IEnumerable<(string, string)>? environmentVariables = null,
        bool output = true,
        string? inputForStdIn = null)
    {
        using var cts = new CancellationTokenSource();
        using var process = GetProcess(filename, arguments, cwd, environmentVariables);
        process.Start();

        if (!string.IsNullOrWhiteSpace(inputForStdIn))
        {
            var writer = process.StandardInput;
            writer.AutoFlush = true;
            await writer.WriteAsync(inputForStdIn);
            writer.Close();
        }

        ReadStream(process.StandardOutput, output, cts.Token);
        ReadStream(process.StandardError, output, cts.Token);

        await process.WaitForExitAsync(cts.Token);

        cts.Cancel();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception(error);
        }
    }

    public async Task<string> RunAndCaptureAsync(string filename, string arguments, string? cwd = null, IEnumerable<(string, string)>? environmentVariables = null, bool throwOnError = true)
    {
        using var process = GetProcess(filename, arguments, cwd, environmentVariables);
        process.Start();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && throwOnError)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception(error);
        }

        return await process.StandardOutput.ReadToEndAsync();
    }

    private static Process GetProcess(string filename, string arguments, string? cwd = null, IEnumerable<(string, string)>? environmentVariables = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = filename,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = cwd,
            CreateNoWindow = true
        };

        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                startInfo.EnvironmentVariables.Add(key, value);
            }
        }

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    private static void ReadStream(StreamReader reader, bool output, CancellationToken ctx)
    {
        Task.Run(() =>
        {
            while (!ctx.IsCancellationRequested)
            {
                int current;
                while ((current = reader.Read()) >= 0)
                {
                    if (output)
                        Console.Write((char)current);
                }
            }
        }, ctx);
    }
}

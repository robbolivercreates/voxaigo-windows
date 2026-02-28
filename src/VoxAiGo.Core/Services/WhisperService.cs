using System.IO;
using NAudio.Wave;
using NAudio.MediaFoundation;
using Whisper.net;
using Whisper.net.Ggml;
using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

public class WhisperService : ITranscriptionService, IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _currentModelPath;
    private const string ModelDirectory = "Models";

    public bool IsModelLoaded => _processor != null;

    public async Task LoadModelAsync(string modelName = "ggml-base.bin")
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModelDirectory, modelName);

        // Model not downloaded yet — fail gracefully instead of crashing.
        // The UI handles this case by checking IsModelLoaded before calling TranscribeAsync.
        if (!File.Exists(path))
            return;

        if (_currentModelPath == path && IsModelLoaded) return; // Already loaded

        _currentModelPath = path;
        
        // Dispose old instance if exists
        _processor?.Dispose();
        _factory?.Dispose();

        _factory = WhisperFactory.FromPath(path);
        
        // Create processor with default settings
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto") // Auto-detect language
            .Build();
    }

    public async Task<string> TranscribeAsync(byte[] audioData, TranscriptionMode mode, SpeechLanguage outputLanguage)
    {
        if (_processor == null)
            throw new InvalidOperationException("Whisper model not loaded.");

        // Resample in-memory using NAudio
        var samples = await Task.Run(() => ResampleTo16kHz(audioData));
        
        var text = "";
        await foreach (var segment in _processor.ProcessAsync(samples))
        {
            text += segment.Text + " ";
        }

        return text.Trim();
    }

    private float[] ResampleTo16kHz(byte[] audioData)
    {
        using var ms = new MemoryStream(audioData);
        using var reader = new WaveFileReader(ms); // Parses WAV header
        
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
        using var resampler = new MediaFoundationResampler(reader, targetFormat);
        resampler.ResamplerQuality = 60;

        var sampleProvider = resampler.ToSampleProvider();
        var buffer = new float[reader.Length]; // Rough estimate
        var samples = new List<float>();
        
        int read;
        var tempBuffer = new float[4096];
        while ((read = sampleProvider.Read(tempBuffer, 0, tempBuffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++) samples.Add(tempBuffer[i]);
        }
        
        return samples.ToArray();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}

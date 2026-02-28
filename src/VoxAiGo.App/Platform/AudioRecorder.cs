using NAudio.CoreAudioApi;
using NAudio.Wave;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace VoxAiGo.App.Platform;

public partial class AudioRecorder : ObservableObject, IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _tempFilePath;
    private float _smoothedLevel;

    [ObservableProperty]
    private float _audioLevel;

    [ObservableProperty]
    private bool _speechDetected;

    public string? LastRecordingPath => _tempFilePath;
    public WaveFormat? SourceFormat => _capture?.WaveFormat;

    public void Start(string? deviceId = null)
    {
        Stop(); // Ensure clean state

        var device = deviceId != null 
            ? new MMDeviceEnumerator().GetDevice(deviceId) 
            : new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

        _capture = new WasapiCapture(device);
        // Note: WASAPI captures at system format (often 48kHz float)
        
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"voxaigo_{DateTime.Now.Ticks}.wav");
        _writer = new WaveFileWriter(_tempFilePath, _capture.WaveFormat);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        try 
        {
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio Start Error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _capture?.StopRecording();

        // Flush and close writer synchronously so the WAV file is complete
        // before GetRecordedBytes() reads it
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch { }
        _writer = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_writer == null) return;

        try { _writer.Write(e.Buffer, 0, e.BytesRecorded); }
        catch { return; } // Writer may be closed during stop

        // Calculate Level (RMS) for UI
        CalculateLevel(e.Buffer, e.BytesRecorded);
    }
    
    private void CalculateLevel(byte[] buffer, int bytesRecorded)
    {
        float sum = 0;
        int samples = 0;

        if (_capture?.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            // 32-bit float
            for (int i = 0; i < bytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, i);
                sum += sample * sample;
                samples++;
            }
        }
        else
        {
            // 16-bit PCM
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float normalized = sample / 32768f;
                sum += normalized * normalized;
                samples++;
            }
        }

        if (samples > 0)
        {
            float rms = MathF.Sqrt(sum / samples);
            _smoothedLevel = _smoothedLevel * 0.6f + rms * 0.4f;
            AudioLevel = _smoothedLevel; // UI update
            
            if (rms > 0.02f) SpeechDetected = true;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Writer is already disposed in Stop() — just clean up capture
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _capture?.Dispose();
        _capture = null;
    }

    public byte[]? GetRecordedBytes()
    {
        if (_tempFilePath == null || !File.Exists(_tempFilePath)) return null;
        
        for (int i = 0; i < 5; i++)
        {
            try 
            {
                return File.ReadAllBytes(_tempFilePath);
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        return null;
    }

    /// <summary>
    /// Resamples the raw audio to 16kHz Mono Float (Required for Whisper).
    /// </summary>
    public float[] GetSamplesForWhisper()
    {
        if (_tempFilePath == null || !File.Exists(_tempFilePath)) return Array.Empty<float>();

        // Open original WAV
        using var reader = new WaveFileReader(_tempFilePath);
        
        // Target format: 16kHz, Mono, IEEE Float
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
        
        // Use MediaFoundationResampler (built into Windows, high quality)
        using var resampler = new MediaFoundationResampler(reader, targetFormat);
        resampler.ResamplerQuality = 60; // Better quality

        // Read all samples
        var floatList = new List<float>();
        var buffer = new byte[reader.Length]; // rough size
        int read;
        
        // We need to read as float samples
        var provider = resampler.ToSampleProvider();
        var sampleBuffer = new float[1024];
        
        while ((read = provider.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                floatList.Add(sampleBuffer[i]);
            }
        }

        return floatList.ToArray();
    }

    public void Dispose()
    {
        Stop();
    }
}

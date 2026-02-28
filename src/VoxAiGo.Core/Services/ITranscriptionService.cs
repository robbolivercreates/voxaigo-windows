using VoxAiGo.Core.Models;

namespace VoxAiGo.Core.Services;

public interface ITranscriptionService
{
    Task<string> TranscribeAsync(byte[] audioData, TranscriptionMode mode, SpeechLanguage outputLanguage);
}

namespace PonyuDev.SherpaOnnx.Vad.Engine
{
    /// <summary>
    /// A detected speech segment with start sample index and audio data.
    /// Copies all data from the native <c>SpeechSegment</c>
    /// so the native handle can be released immediately.
    /// </summary>
    public sealed class VadSegment
    {
        /// <summary>
        /// Start sample index in the original waveform. 64-bit: the index
        /// keeps growing for the lifetime of a detector, and a 32-bit value
        /// overflows after about 37 hours at 16 kHz.
        /// </summary>
        public long StartSample { get; }

        /// <summary>PCM audio samples of the speech segment.</summary>
        public float[] Samples { get; }

        /// <summary>Duration in seconds based on sample rate.</summary>
        public float Duration { get; }

        /// <summary>Start time in seconds based on sample rate.</summary>
        public float StartTime { get; }

        internal VadSegment(
            long startSample,
            float[] samples,
            int sampleRate)
        {
            StartSample = startSample;
            Samples = samples;
            StartTime = (float)(startSample / (double)sampleRate);
            Duration = samples.Length / (float)sampleRate;
        }
    }
}

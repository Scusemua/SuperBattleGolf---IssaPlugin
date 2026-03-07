using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static readonly ItemType BatItemType = (ItemType)100;
        public static AudioClip HomerunClip { get; private set; }

        public static void LoadHomerunSound()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string wavPath = Path.Combine(pluginDir, "homerun.wav");

            if (!File.Exists(wavPath))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[Bat] homerun.wav not found in: {pluginDir}  "
                        + "(convert your audio to WAV PCM 16-bit format)"
                );
                return;
            }

            try
            {
                HomerunClip = LoadWav(wavPath);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Bat] Loaded homerun.wav "
                        + $"({HomerunClip.length:F1}s, {HomerunClip.channels}ch, "
                        + $"{HomerunClip.frequency}Hz)."
                );
            }
            catch (Exception e)
            {
                IssaPluginPlugin.Log.LogError($"[Bat] Failed to load homerun.wav: {e.Message}");
            }
        }

        private static AudioClip LoadWav(string path)
        {
            byte[] wav = File.ReadAllBytes(path);

            int channels = BitConverter.ToInt16(wav, 22);
            int sampleRate = BitConverter.ToInt32(wav, 24);
            int bitsPerSample = BitConverter.ToInt16(wav, 34);

            int dataOffset = 12;
            int dataSize = 0;
            while (dataOffset < wav.Length - 8)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(wav, dataOffset, 4);
                int chunkSize = BitConverter.ToInt32(wav, dataOffset + 4);
                if (chunkId == "data")
                {
                    dataOffset += 8;
                    dataSize = chunkSize;
                    break;
                }
                dataOffset += 8 + chunkSize;
            }

            if (dataSize == 0)
                throw new InvalidOperationException("No 'data' chunk found in WAV file.");

            float[] samples;
            if (bitsPerSample == 16)
            {
                int count = dataSize / 2;
                samples = new float[count];
                for (int i = 0; i < count; i++)
                    samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;
            }
            else if (bitsPerSample == 24)
            {
                int count = dataSize / 3;
                samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int s =
                        wav[dataOffset + i * 3]
                        | (wav[dataOffset + i * 3 + 1] << 8)
                        | (wav[dataOffset + i * 3 + 2] << 16);
                    if (s >= 0x800000)
                        s -= 0x1000000;
                    samples[i] = s / 8388608f;
                }
            }
            else if (bitsPerSample == 32)
            {
                int count = dataSize / 4;
                samples = new float[count];
                for (int i = 0; i < count; i++)
                    samples[i] = BitConverter.ToSingle(wav, dataOffset + i * 4);
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported bit depth: {bitsPerSample}. Use 16, 24, or 32-bit WAV."
                );
            }

            var clip = AudioClip.Create(
                "homerun",
                samples.Length / channels,
                channels,
                sampleRate,
                false
            );
            clip.SetData(samples, 0);
            return clip;
        }

        public static void PlayHomerunSound(Vector3 position)
        {
            if (HomerunClip != null)
                AudioSource.PlayClipAtPoint(HomerunClip, position);
        }

        public static void GiveBatToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                BatItemType,
                Configuration.BaseballBatUses.Value,
                "Bat"
            );
        }
    }
}

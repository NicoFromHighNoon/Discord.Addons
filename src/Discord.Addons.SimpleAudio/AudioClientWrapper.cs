﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord.Audio;

namespace Discord.Addons.SimpleAudio
{
    internal sealed class AudioClientWrapper
    {
        public IAudioClient Client { get; }
        public ConcurrentQueue<string> Playlist { get; } = new ConcurrentQueue<string>();

        public AudioClientWrapper(IAudioClient client)
        {
            Client = client;
        }

        private Process _ffmpeg;

        public async Task SendAudioAsync(string ffmpeg)
        {
            while (!Playlist.IsEmpty)
            {
                if (Playlist.TryDequeue(out var path))
                {
                    using (_ffmpeg = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = $"-hide_banner -loglevel panic -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = false
                    }))
                    using (var output = _ffmpeg.StandardOutput.BaseStream)
                    using (var stream = Client.CreatePCMStream(AudioApplication.Music))
                    {
                        try
                        {
                            await PausableCopyToAsync(output, stream, 81920);
                        }
                        catch (OperationCanceledException)
                        {
                            if (!_next)
                            {
                                //TODO: Replace with .Clear() when NET Core 2.0 is a thing
                                while (Playlist.TryDequeue(out var _)) { }
                                break;
                            }
                            else
                            {
                                _next = false;
                            }
                        }
                        finally
                        {
                            await stream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        private readonly object _cancelLock = new object();
        private bool _next = false;
        private CancellationTokenSource _cancel = new CancellationTokenSource();

        public void Cancel()
        {
            lock (_cancelLock)
            {
                if (IsPlaying())
                {
                    using (_cancel)
                    {
                        _cancel.Cancel();
                    }
                    _cancel = new CancellationTokenSource();
                }
            }
        }

        public void SkipToNext()
        {
            _next = true;
            Cancel();
        }

        public bool IsPlaying()
        {
            if (_ffmpeg == null)
                return false;

            // Don't try this at home - I'm a professional
            var disposedField = typeof(Process)
                .GetTypeInfo()
                .GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
            var isDisposed = (bool)disposedField.GetValue(_ffmpeg);

            return !(isDisposed || _ffmpeg.HasExited);
        }

        public void SetVolume(float newVolume)
        {
            _playingVolume = newVolume;
        }

        private readonly object _pauseLock = new object();
        private bool _pause = false;
        private CancellationTokenSource _pauseToken = new CancellationTokenSource();

        public void Pause() => _pause = true;
        public void Resume()
        {
            lock (_pauseLock)
            {
                if (_pause)
                {
                    _pause = false;
                    using (_pauseToken)
                    {
                        _pauseToken.Cancel();
                    }
                    _pauseToken = new CancellationTokenSource();
                }
            }
        }

        private async Task PausableCopyToAsync(Stream source, Stream destination, int buffersize)
        {
            Contract.Requires(destination != null);
            Contract.Requires(buffersize > 0);
            Contract.Requires(source.CanRead);
            Contract.Requires(destination.CanWrite);

            byte[] buffer = new byte[buffersize];
            int count;

            while ((count = await source.ReadAsync(buffer, 0, buffersize, _cancel.Token)) > 0)
            {
                if (_pause)
                {
                    try
                    {
                        await Task.Delay(-1, _pauseToken.Token);
                    }
                    catch (OperationCanceledException) { }
                }

                var volAdjusted = ScaleVolumeUnsafeNoAlloc(buffer, _playingVolume);
                await destination.WriteAsync(volAdjusted, 0, count, _cancel.Token);
            }
        }

        private float _playingVolume = 0.5f;

        //https://hastebin.com/umapabejis.cs
        private unsafe static byte[] ScaleVolumeUnsafeNoAlloc(byte[] audioSamples, float volume)
        {
            Contract.Requires(audioSamples != null);
            Contract.Requires(audioSamples.Length % 2 == 0);
            Contract.Requires(volume >= 0f && volume <= 1f);
            Contract.Assert(BitConverter.IsLittleEndian);

            if (Math.Abs(volume - 1f) < 0.0001f) return audioSamples;

            // 16-bit precision for the multiplication
            int volumeFixed = (int)Math.Round(volume * 65536d);

            int count = audioSamples.Length / 2;

            fixed (byte* srcBytes = audioSamples)
            {
                short* src = (short*)srcBytes;

                for (int i = count; i != 0; i--, src++)
                    *src = (short)(((*src) * volumeFixed) >> 16);
            }

            return audioSamples;
        }
    }
}

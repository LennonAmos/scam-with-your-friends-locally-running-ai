using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PocketTTS;

namespace ScamOllama
{
    // Optional companion behavior, activated only by the Famous Callers Rick voice.
    // Native pause/transcript tags reserve time without speaking a stage direction.
    public static class RickBurps
    {
        public const string Cue = "<transcript text=\"\">[641ms]</transcript>";
        private static readonly Dictionary<uint,int> Counts = new Dictionary<uint,int>();
        private static readonly ConditionalWeakTable<object, Playback> Requests = new ConditionalWeakTable<object, Playback>();
        private static readonly object Gate = new object();

        public static bool IsRick(string path)
        {
            return path != null && path.Replace('\\','/').EndsWith("/FamousCallers/Voices/rick_sanchez.wav", StringComparison.OrdinalIgnoreCase);
        }

        public static void Prepare(ref string text, string voice, uint callId, bool filler)
        {
            if (callId == 0 || filler || !IsRick(voice) || string.IsNullOrWhiteSpace(text) || text.Contains(Cue)) return;
            lock (Gate)
            {
                int count;
                Counts.TryGetValue(callId, out count);
                if (Counts.Count > 128) Counts.Clear();
                Counts[callId] = ++count;
                if (count % 3 == 0) text = Cue + " " + text;
            }
        }

        public static void Process(PocketTtsFrame frame, string voice, object request)
        {
            if (request == null || !IsRick(voice) || frame.data == null || frame.sampleRateHz < 8000 || frame.sampleRateHz > 192000 || frame.channels < 1 || frame.channels > 8 || frame.dataLength < 0 || frame.dataLength > frame.data.Length) return;
            lock (Gate)
            {
                Playback state;
                if (!Requests.TryGetValue(request, out state))
                {
                    var field = request.GetType().GetField("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    string text = field == null ? null : field.GetValue(request) as string;
                    state = new Playback(text != null && text.Contains(Cue), frame.sampleRateHz);
                    Requests.Add(request, state);
                }
                state.Apply(frame.data, frame.dataLength, frame.channels);
            }
        }

        private sealed class Playback
        {
            private readonly float[] sound;
            private readonly int wait;
            private int quiet, position;
            private bool finished;
            public Playback(bool enabled, int rate)
            {
                finished = !enabled;
                wait = (int)(rate * 0.30);
                sound = enabled ? Synthesize(rate) : null;
            }
            public void Apply(float[] data, int length, int channels)
            {
                if (finished) return;
                for (int i=0; i+channels<=length; i+=channels)
                {
                    bool silent = true;
                    for (int channel=0; channel<channels; channel++)
                        if (Math.Abs(data[i+channel]) > 0.0000001f) { silent=false; break; }
                    if (!silent)
                    {
                        // Never overwrite spoken audio, even if a provider cuts a pause short.
                        if (position > 0) { finished=true; return; }
                        quiet=0;
                        continue;
                    }
                    if (++quiet <= wait) continue;
                    if (position >= sound.Length) { finished=true; return; }
                    float value = sound[position++];
                    for (int channel=0; channel<channels; channel++) data[i+channel]=value;
                }
            }
        }

        // Original procedural sound: no television recording or external sample.
        public static float[] Synthesize(int rate)
        {
            var data = new float[(int)(rate * 0.26)];
            double phase=0;
            uint noise=1357911;
            for (int i=0; i<data.Length; i++)
            {
                double t=(double)i/rate, progress=(double)i/data.Length;
                noise=unchecked(noise*1664525u+1013904223u);
                double breath=((noise>>8)/(double)0xFFFFFF)*2-1;
                double frequency=108-55*progress+5*Math.Sin(t*83);
                phase += frequency*Math.PI*2/rate;
                double pulse=Math.Sin(phase)+0.52*Math.Sin(phase*2)+0.29*Math.Sin(phase*3)+0.17*Math.Sin(phase*5);
                double formant=0.24*Math.Sin(phase*4.7)+0.13*Math.Sin(phase*9.2);
                double envelope=Math.Min(1,t/0.018)*Math.Min(1,(0.26-t)/0.065)*(0.8+0.2*Math.Sin(t*35));
                double sample=(pulse*0.34+formant+breath*0.11)*envelope;
                data[i]=(float)(0.62*sample/(1+Math.Abs(sample)));
            }
            return data;
        }
    }
}

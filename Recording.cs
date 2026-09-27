using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AutoInput
{
    public class KeyFrame
    {
        public int Key { get; set; }
        public bool Up { get; set; }
        public bool Extended { get; set; }
        public int AtMs { get; set; }
    }
    public class Clip
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int DurationMs { get; set; }
        public List<KeyFrame> Frames { get; set; }
        public Clip() { Id = Guid.NewGuid().ToString(); Frames = new List<KeyFrame>(); }
        public override string ToString() { return Name + " · " + DurationMs + "ms · " + Frames.Count + "개 이벤트"; }
    }
    public class ClipStep
    {
        public string ClipId { get; set; }
        public int Repeat { get; set; }
        public int GapMs { get; set; }
    }
    public class RecordingLibrary
    {
        public List<Clip> Clips { get; set; }
        public List<ClipStep> Steps { get; set; }
        public RecordingLibrary() { Clips = new List<Clip>(); Steps = new List<ClipStep>(); }
    }

    // 실제 사용자 입력만 기록합니다. hook 콜백에서는 파일 저장이나 UI 갱신을 하지 않습니다.
    internal sealed class KeyboardRecorder : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        private readonly HookProc callback;
        private IntPtr hook;
        private readonly Stopwatch watch = new Stopwatch();
        private readonly Dictionary<int, KeyFrame> held = new Dictionary<int, KeyFrame>();
        private Clip clip;
        private Func<bool> active;
        private HashSet<int> reservedKeys = new HashSet<int>();
        internal bool FocusLost { get; private set; }
        internal bool LimitReached { get; private set; }
        internal int Elapsed { get { return (int)watch.ElapsedMilliseconds; } }
        internal KeyboardRecorder() { callback = OnKey; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HookData { public uint key, scan, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        internal void Start(string name, Func<bool> targetActive, IEnumerable<int> controlKeys)
        {
            reservedKeys = new HashSet<int>(controlKeys);
            clip = new Clip { Name = name }; active = targetActive;
            FocusLost = LimitReached = false; held.Clear(); watch.Restart();
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && clip != null && !FocusLost && !LimitReached)
            {
                if (!active()) FocusLost = true;
                else
                {
                    var input = (HookData)Marshal.PtrToStructure(data, typeof(HookData));
                    int key = (int)input.key;
                    int msg = message.ToInt32();
                    bool up = msg == 0x101 || msg == 0x105;
                    bool down = msg == 0x100 || msg == 0x104;
                    // 현재 설정된 제어키는 제외합니다. 자동 반복 down은 유지 시간으로 표현합니다.
                    if ((input.flags & 0x10) == 0 && !reservedKeys.Contains(key) && (up || down))
                    {
                        var frame = new KeyFrame { Key = key, Up = up, Extended = (input.flags & 1) != 0, AtMs = Elapsed };
                        if (up && held.Remove(key)) clip.Frames.Add(frame);
                        else if (down && !held.ContainsKey(key)) { held.Add(key, frame); clip.Frames.Add(frame); }
                    }
                    LimitReached = Elapsed >= 600000 || clip.Frames.Count >= 20000;
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        internal Clip Stop()
        {
            Dispose(); watch.Stop();
            clip.DurationMs = Elapsed;
            foreach (var item in held.Values)
                clip.Frames.Add(new KeyFrame { Key = item.Key, Extended = item.Extended, Up = true, AtMs = Elapsed });
            held.Clear();
            var result = clip; clip = null; return result;
        }
        public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    }

    public static class SequencePlayer
    {
        public static async Task PlayAsync(RecordingLibrary library, int cycles, Action<KeyFrame> send,
            Func<bool> active, CancellationToken token, bool infinite = false)
        {
            if (library == null || library.Steps.Count == 0) throw new InvalidOperationException("재생할 조합이 없습니다.");
            if (!infinite && cycles < 1) throw new ArgumentOutOfRangeException("cycles");
            var held = new Dictionary<int, KeyFrame>();
            try
            {
                int remaining = cycles;
                while (infinite || remaining-- > 0)
                {
                token.ThrowIfCancellationRequested();
                var cycleWatch = Stopwatch.StartNew();
                foreach (var step in library.Steps)
                {
                    var clip = library.Clips.Find(c => c.Id == step.ClipId);
                    if (clip == null) throw new InvalidOperationException("조합에 없는 클립이 참조되었습니다.");
                    for (int repeat = 0; repeat < step.Repeat; repeat++)
                    {
                        var watch = Stopwatch.StartNew();
                        foreach (var frame in clip.Frames)
                        {
                            await WaitUntil(watch, frame.AtMs, active, token);
                            if (!frame.Up) held[frame.Key] = frame;
                            send(frame);
                            if (frame.Up) held.Remove(frame.Key);
                        }
                        await WaitUntil(watch, clip.DurationMs + step.GapMs, active, token);
                    }
                }
                // 0ms 클립 조합도 UI 메시지(중지 키)를 처리할 시간을 보장합니다.
                if (cycleWatch.ElapsedMilliseconds < 1) await Task.Delay(1, token);
                }
            }
            finally
            {
                Exception failure = null;
                foreach (var frame in held.Values)
                {
                    try { send(new KeyFrame { Key = frame.Key, Extended = frame.Extended, Up = true }); }
                    catch (Exception ex) { failure = ex; }
                }
                if (failure != null) throw new InvalidOperationException("일부 키 해제에 실패했습니다.", failure);
            }
        }
        private static async Task WaitUntil(Stopwatch watch, int at, Func<bool> active, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!active()) throw new InvalidOperationException("대상 창이 바뀌어 중지했습니다.");
                long left = at - watch.ElapsedMilliseconds;
                if (left <= 0) return;
                await Task.Delay((int)Math.Min(10, left), token);
            }
        }
    }
}

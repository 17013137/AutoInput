using System;
using System.Threading;
using System.Threading.Tasks;
using AutoInput;

internal static class RunnerTests
{
    private static int Main()
    {
        try { HotkeyTests(); SequenceTests().GetAwaiter().GetResult(); InfiniteTests().GetAwaiter().GetResult(); Console.WriteLine("PASS: infinite repeat/cancellation/focus/zero-duration, hotkeys, finite sequence, XML"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task InfiniteTests()
    {
        var clip = new Clip { DurationMs = 5 };
        clip.Frames.Add(new KeyFrame { Key = 65 });
        clip.Frames.Add(new KeyFrame { Key = 65, Up = true, AtMs = 3 });
        var library = new RecordingLibrary(); library.Clips.Add(clip);
        library.Steps.Add(new ClipStep { ClipId = clip.Id, Repeat = 1 });
        int downs = 0, ups = 0;
        using (var cancel = new CancellationTokenSource())
        {
            try
            {
                await SequencePlayer.PlayAsync(library, 1, f => {
                    if (f.Up) ups++; else if (++downs == 3) cancel.Cancel();
                }, () => true, cancel.Token, true);
                throw new Exception("Infinite run completed without cancellation");
            }
            catch (OperationCanceledException) { }
        }
        Assert(downs == 3 && ups == 3, "Infinite must exceed finite count and release held key on cancel");
        downs = ups = 0; bool active = true;
        try
        {
            await SequencePlayer.PlayAsync(library, 1, f => {
                if (f.Up) ups++; else if (++downs == 2) active = false;
            }, () => active, CancellationToken.None, true);
            throw new Exception("Infinite ignored focus loss");
        }
        catch (InvalidOperationException) { }
        Assert(downs == 2 && ups == 2, "Infinite focus loss must release held key");
        clip.DurationMs = 0; clip.Frames.Clear();
        using (var cancel = new CancellationTokenSource())
        {
            var task = SequencePlayer.PlayAsync(library, 1, f => { }, () => true, cancel.Token, true);
            Assert(!task.IsCompleted, "Zero duration loop must yield control");
            cancel.Cancel();
            try { await task; throw new Exception("Zero duration loop ignored cancellation"); }
            catch (OperationCanceledException) { }
        }
    }
    private static void HotkeyTests()
    {
        var settings = new HotkeySettings(); settings.Validate();
        var copy = settings.Copy(); copy.Keys[0] = 65; copy.Validate();
        Assert(settings.Keys[0] == 118, "Editing must not change active bindings");
        copy.Keys[1] = 65;
        try { copy.Validate(); throw new Exception("Duplicate hotkeys accepted"); }
        catch (System.IO.InvalidDataException) { }
        copy = settings.Copy(); copy.Keys[0] = 123;
        try { copy.Validate(); throw new Exception("Reserved F12 accepted"); }
        catch (System.IO.InvalidDataException) { }
        copy.Keys = new int[0];
        try { copy.Validate(); throw new Exception("Incomplete bindings accepted"); }
        catch (System.IO.InvalidDataException) { }
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(HotkeySettings));
        using (var stream = new System.IO.MemoryStream())
        {
            serializer.Serialize(stream, settings); stream.Position = 0;
            var loaded = (HotkeySettings)serializer.Deserialize(stream); loaded.Validate();
            Assert(loaded.Keys[0] == 118 && loaded.Keys[5] == 122, "Hotkeys must survive persistence");
        }
    }
    private static async Task SequenceTests()
    {
        var clip = new Clip { Name = "동시 입력", DurationMs = 30 };
        clip.Frames.Add(new KeyFrame { Key = 65, AtMs = 0 });
        clip.Frames.Add(new KeyFrame { Key = 66, AtMs = 5 });
        clip.Frames.Add(new KeyFrame { Key = 65, Up = true, AtMs = 15 });
        clip.Frames.Add(new KeyFrame { Key = 66, Up = true, AtMs = 20 });
        var library = new RecordingLibrary(); library.Clips.Add(clip);
        library.Steps.Add(new ClipStep { ClipId = clip.Id, Repeat = 2, GapMs = 10 });
        var sent = new System.Collections.Generic.List<string>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SequencePlayer.PlayAsync(library, 2, f => sent.Add(f.Key + ":" + f.Up), () => true, CancellationToken.None);
        Assert(sent.Count == 16, "Step and whole-sequence repeats must compose");
        Assert(watch.ElapsedMilliseconds >= 160, "Clip duration and gaps must be retained");
        for (int i = 0; i < sent.Count; i += 4)
            Assert(sent[i] == "65:False" && sent[i+1] == "66:False" && sent[i+2] == "65:True" && sent[i+3] == "66:True", "Overlapping key order");
        using (var cancel = new CancellationTokenSource())
        {
            sent.Clear();
            try
            {
                await SequencePlayer.PlayAsync(library, 1, f => { sent.Add(f.Key + ":" + f.Up); if (f.Key == 66 && !f.Up) cancel.Cancel(); }, () => true, cancel.Token);
                throw new Exception("Sequence cancellation missing");
            }
            catch (OperationCanceledException) { }
            Assert(sent.Count == 4 && sent.Contains("65:True") && sent.Contains("66:True"), "Cancel must release every held key");
        }
        bool active = true; sent.Clear();
        try
        {
            await SequencePlayer.PlayAsync(library, 1, f => { sent.Add(f.Key + ":" + f.Up); active = false; }, () => active, CancellationToken.None);
            throw new Exception("Sequence focus guard missing");
        }
        catch (InvalidOperationException) { }
        Assert(sent.Count == 2 && sent[1] == "65:True", "Focus loss must release held key");
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecordingLibrary));
        using (var stream = new System.IO.MemoryStream())
        {
            serializer.Serialize(stream, library); stream.Position = 0;
            var loaded = (RecordingLibrary)serializer.Deserialize(stream);
            Assert(loaded.Clips[0].Name == clip.Name && loaded.Clips[0].Frames.Count == 4 && loaded.Steps[0].ClipId == clip.Id, "Saved clips and sequence must roundtrip");
        }
    }
}

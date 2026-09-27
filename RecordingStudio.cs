using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace AutoInput
{
    internal sealed class RecordingStudio : UserControl
    {
        internal readonly NumericUpDown Cycles = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 70 };
        internal readonly CheckBox InfiniteRepeat = new CheckBox { Text = "무한 반복", AutoSize = true };
        private readonly TextBox name = new TextBox { Text = "클립 1", Width = 180 };
        private readonly ListBox clips = new ListBox { Dock = DockStyle.Fill };
        private readonly ClipTimeline steps = new ClipTimeline();
        private Point clipMouseDown;
        private Clip draggedClip;
        private readonly Label duration = new Label { AutoSize = true };
        private readonly TextBox timeline = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly NumericUpDown repeats = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 70 };
        private readonly NumericUpDown gap = new NumericUpDown { Minimum = 0, Maximum = 60000, Increment = 100, Width = 90 };
        private RecordingLibrary library = new RecordingLibrary();
        private readonly Action<string> log;
        private readonly string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordings.xml");
        internal string ClipName { get { return name.Text.Trim().Length == 0 ? "새 클립" : name.Text.Trim(); } }
        internal RecordingStudio(Action<string> logger)
        {
            log = logger; Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "녹화 클립 · 시작/중지 단축키는 왼쪽 안내 참조\n대상 창에서만 녹화됩니다. 먼저 대상 창을 지정하세요.", AutoSize = true }, 0, 0);
            layout.Controls.Add(Row(new Label { Text = "다음 녹화 이름", AutoSize = true }, name,
                Button("이름 변경", () => { var c = clips.SelectedItem as Clip; if (c != null) { c.Name = ClipName; RefreshLists(); Save(); } })), 0, 1);
            layout.Controls.Add(clips, 0, 2);
            layout.Controls.Add(Row(Button("조합에 추가 ↓", AddStep), Button("클립 삭제", DeleteClip)), 0, 3);
            layout.Controls.Add(timeline, 0, 4);
            var zoom = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 1, Width = 55 };
            zoom.ValueChanged += (s, e) => steps.Zoom = (int)zoom.Value;
            layout.Controls.Add(Row(new Label { Text = "타임라인 · 클립을 드래그해서 추가 / 순서 이동", AutoSize = true },
                new Label { Text = "확대", AutoSize = true }, zoom, duration), 0, 5);
            layout.Controls.Add(steps, 0, 6);
            layout.Controls.Add(Row(Button("←", () => MoveStep(-1)), Button("→", () => MoveStep(1)), Button("제거", RemoveStep),
                new Label { Text = "반복", AutoSize = true }, repeats, new Label { Text = "매회 뒤 대기 ms", AutoSize = true }, gap,
                Button("적용", ApplyStep)), 0, 7);
            InfiniteRepeat.CheckedChanged += (s, e) => Cycles.Enabled = !InfiniteRepeat.Checked;
            layout.Controls.Add(Row(new Label { Text = "전체 반복", AutoSize = true }, Cycles, InfiniteRepeat,
                Button("파일 저장", Save)), 0, 8);
            clips.SelectedIndexChanged += (s, e) => ShowTimeline();
            clips.MouseDown += (s, e) => {
                clipMouseDown = e.Location; draggedClip = null;
                int index = clips.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && index >= 0) draggedClip = clips.Items[index] as Clip;
            };
            clips.MouseUp += (s, e) => draggedClip = null;
            clips.MouseMove += (s, e) => {
                if (e.Button != MouseButtons.Left || draggedClip == null) return;
                var area = new Rectangle(clipMouseDown.X - SystemInformation.DragSize.Width / 2,
                    clipMouseDown.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
                if (area.Contains(e.Location)) return;
                var clip = draggedClip; draggedClip = null;
                clips.DoDragDrop(new TimelineDrag { Clip = clip }, DragDropEffects.Copy);
            };
            steps.SequenceChanged += (s, e) => { UpdateDuration(); Save(); };
            steps.SelectedIndexChanged += (s, e) => {
                int i = steps.SelectedIndex;
                if (i >= 0) { repeats.Value = library.Steps[i].Repeat; gap.Value = library.Steps[i].GapMs; }
            };
            LoadLibrary(); RefreshLists();
        }
        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            row.Controls.AddRange(controls); return row;
        }
        private static Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s, e) => action(); return button;
        }
        internal void AddClip(Clip clip)
        {
            if (clip.Frames.Count == 0) { log("입력이 없어 녹화 클립을 저장하지 않았습니다."); return; }
            library.Clips.Add(clip); RefreshLists(); clips.SelectedItem = clip;
            name.Text = "클립 " + (library.Clips.Count + 1); Save();
            log("녹화 완료: " + clip);
        }
        private void AddStep()
        {
            var clip = clips.SelectedItem as Clip;
            if (clip == null) return;
            library.Steps.Add(new ClipStep { ClipId = clip.Id, Repeat = (int)repeats.Value, GapMs = (int)gap.Value });
            RefreshLists(); steps.SelectedIndex = library.Steps.Count - 1; Save();
        }
        private void DeleteClip()
        {
            var clip = clips.SelectedItem as Clip;
            if (clip == null) return;
            if (library.Steps.Exists(s => s.ClipId == clip.Id)) { log("조합에서 먼저 제거한 뒤 클립을 삭제하세요."); return; }
            library.Clips.Remove(clip); RefreshLists(); Save();
        }
        private void RemoveStep()
        {
            steps.DeleteSelected();
        }
        internal bool DeleteFocusedStep()
        {
            if (!Enabled || !steps.Focused) return false;
            steps.DeleteSelected(); return true;
        }
        private void MoveStep(int delta)
        {
            int i = steps.SelectedIndex, next = i + delta;
            if (i < 0 || next < 0 || next >= library.Steps.Count) return;
            var step = library.Steps[i]; library.Steps.RemoveAt(i); library.Steps.Insert(next, step);
            RefreshLists(); steps.SelectedIndex = next; Save();
        }
        private void ApplyStep()
        {
            int i = steps.SelectedIndex; if (i < 0) return;
            library.Steps[i].Repeat = (int)repeats.Value; library.Steps[i].GapMs = (int)gap.Value;
            RefreshLists(); steps.SelectedIndex = i; Save();
        }
        private void RefreshLists()
        {
            var selected = clips.SelectedItem;
            clips.Items.Clear(); foreach (var clip in library.Clips) clips.Items.Add(clip);
            if (selected != null && clips.Items.Contains(selected)) clips.SelectedItem = selected;
            else if (clips.Items.Count > 0) clips.SelectedIndex = 0;
            steps.Bind(library); UpdateDuration();
        }
        private void UpdateDuration()
        {
            double milliseconds = 0;
            foreach (var step in library.Steps)
            {
                var clip = library.Clips.Find(c => c.Id == step.ClipId);
                milliseconds += ((double)clip.DurationMs + step.GapMs) * step.Repeat;
            }
            duration.Text = "1회 길이 " + (milliseconds / 1000).ToString("0.##") + "초";
        }
        private void ShowTimeline()
        {
            var clip = clips.SelectedItem as Clip;
            if (clip == null) { timeline.Clear(); return; }
            var text = new StringBuilder("시점(ms)  간격(ms)  키 / 동작\r\n"); int previous = 0;
            foreach (var f in clip.Frames)
            {
                text.AppendLine(f.AtMs + "\t+" + (f.AtMs - previous) + "\t" + (Keys)f.Key + (f.Up ? " 떼기" : " 누르기"));
                previous = f.AtMs;
            }
            text.AppendLine("종료 " + clip.DurationMs + "ms"); timeline.Text = text.ToString();
        }
        internal RecordingLibrary Snapshot()
        {
            if (library.Steps.Count == 0) throw new InvalidOperationException("녹화 클립을 선택하고 '조합에 추가'를 누른 뒤 재생하세요.");
            return library; // 재생 중 편집 UI가 비활성화되어 변경되지 않습니다.
        }
        private void Save()
        {
            try
            {
                string temp = file + ".tmp";
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(RecordingLibrary)).Serialize(stream, library);
                if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
                else File.Move(temp, file);
            }
            catch (Exception ex) { log("파일 저장 실패 (메모리에는 유지됨): " + ex.Message); }
        }
        private void LoadLibrary()
        {
            if (!File.Exists(file)) return;
            try
            {
                RecordingLibrary loaded;
                using (var stream = File.OpenRead(file)) loaded = (RecordingLibrary)new XmlSerializer(typeof(RecordingLibrary)).Deserialize(stream);
                var ids = new System.Collections.Generic.HashSet<string>();
                foreach (var clip in loaded.Clips)
                {
                    if (string.IsNullOrEmpty(clip.Id) || !ids.Add(clip.Id) || clip.DurationMs < 0 || clip.DurationMs > 610000 || clip.Frames.Count > 21000) throw new InvalidDataException("잘못된 클립");
                    int last = 0;
                    var held = new System.Collections.Generic.HashSet<int>();
                    foreach (var f in clip.Frames)
                    {
                        if (f.AtMs < last || f.AtMs > clip.DurationMs || f.Key < 1 || f.Key > 254) throw new InvalidDataException("잘못된 키 이벤트");
                        if (f.Up ? !held.Remove(f.Key) : !held.Add(f.Key)) throw new InvalidDataException("키 누름/해제 순서 오류");
                        last = f.AtMs;
                    }
                    if (held.Count != 0) throw new InvalidDataException("키 해제가 없는 클립");
                }
                foreach (var step in loaded.Steps)
                    if (!ids.Contains(step.ClipId) || step.Repeat < 1 || step.Repeat > 1000 || step.GapMs < 0 || step.GapMs > 60000) throw new InvalidDataException("잘못된 조합");
                library = loaded;
            }
            catch (Exception ex) { log("녹화 파일을 읽지 못했습니다: " + ex.Message); }
        }
    }
}

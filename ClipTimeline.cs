using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoInput
{
    internal sealed class TimelineDrag
    {
        internal Clip Clip;
        internal ClipStep Step;
    }

    // 한 트랙에 연속 배치하는 타임라인입니다. 모델 순서가 곧 재생 순서입니다.
    internal sealed class ClipTimeline : ScrollableControl
    {
        private RecordingLibrary library;
        private readonly List<RectangleF> blocks = new List<RectangleF>();
        private readonly ToolTip tooltip = new ToolTip();
        private int selected = -1, insertion = -1, dragIndex = -1, hover = -1;
        private Point mouseDown;
        private double totalMs, pixelsPerMs;
        private int zoom = 1;
        internal event EventHandler SelectedIndexChanged;
        internal event EventHandler SequenceChanged;
        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                selected = library != null && value >= 0 && value < library.Steps.Count ? value : -1;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        internal int Zoom
        {
            get { return zoom; }
            set { zoom = Math.Max(1, Math.Min(16, value)); Rebuild(); }
        }
        internal ClipTimeline()
        {
            Dock = DockStyle.Fill; AutoScroll = true; AllowDrop = true; TabStop = true;
            BackColor = Color.FromArgb(25, 30, 40);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        internal void Bind(RecordingLibrary data)
        {
            library = data; Rebuild();
            SelectedIndex = Math.Min(selected, data.Steps.Count - 1);
        }
        private double Duration(ClipStep step)
        {
            var clip = library.Clips.Find(c => c.Id == step.ClipId);
            return Math.Max(1, ((double)clip.DurationMs + step.GapMs) * step.Repeat);
        }
        private void Rebuild()
        {
            blocks.Clear(); totalMs = 0;
            if (library == null) return;
            foreach (var step in library.Steps) totalMs += Duration(step);
            pixelsPerMs = Math.Max(1, ClientSize.Width - 40) * (double)zoom / Math.Max(1, totalMs);
            double x = 20;
            foreach (var step in library.Steps)
            {
                float width = (float)(Duration(step) * pixelsPerMs);
                blocks.Add(new RectangleF((float)x, 44, width, 74)); x += width;
            }
            AutoScrollMinSize = new Size((int)Math.Ceiling(x + 20), 150);
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Rebuild(); }
        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            if (blocks.Count == 0)
            {
                TextRenderer.DrawText(g, "녹화 클립을 여기로 드래그하세요", Font, new Point(24, 64), Color.LightGray);
                return;
            }
            double tickMs = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, 100 / pixelsPerMs))));
            while (tickMs * pixelsPerMs < 80) tickMs *= 2;
            double first = Math.Max(0, Math.Floor((-AutoScrollPosition.X - 20) / pixelsPerMs / tickMs) * tickMs);
            using (var grid = new Pen(Color.FromArgb(65, 73, 88)))
            {
                for (double time = first; time <= totalMs; time += tickMs)
                {
                    float x = (float)(20 + time * pixelsPerMs);
                    if (x + AutoScrollPosition.X > ClientSize.Width) break;
                    g.DrawLine(grid, x, 28, x, 128);
                    DrawText(g, (time / 1000).ToString("0.##") + "s", new RectangleF(x + 3, 5, 95, 22), Color.Silver);
                }
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                var r = blocks[i];
                if (r.Right + AutoScrollPosition.X < 0 || r.Left + AutoScrollPosition.X > ClientSize.Width) continue;
                var step = library.Steps[i]; var clip = library.Clips.Find(c => c.Id == step.ClipId);
                using (var fill = new SolidBrush(i == selected ? Color.FromArgb(44, 116, 174) : Color.FromArgb(47, 76, 111))) g.FillRectangle(fill, r);
                using (var edge = new Pen(i == selected ? Color.FromArgb(110, 220, 255) : BackColor, i == selected ? 2 : 1)) g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
                if (r.Width > 35)
                {
                    DrawText(g, clip.Name, new RectangleF(r.X + 8, r.Y + 8, r.Width - 16, 22), Color.White);
                    DrawText(g, "×" + step.Repeat + " · " + (Duration(step) / 1000).ToString("0.##") + "s", new RectangleF(r.X + 8, r.Y + 38, r.Width - 16, 22), Color.LightCyan);
                }
            }
            if (insertion >= 0)
            {
                float x = insertion < blocks.Count ? blocks[insertion].Left : blocks[blocks.Count - 1].Right;
                using (var pen = new Pen(Color.FromArgb(255, 192, 90), 3)) g.DrawLine(pen, x, 34, x, 124);
            }
        }
        private void DrawText(Graphics g, string text, RectangleF bounds, Color color)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Font, brush, bounds, format);
        }
        private Point ContentPoint(Point point) { return new Point(point.X - AutoScrollPosition.X, point.Y - AutoScrollPosition.Y); }
        private int Hit(Point point)
        {
            point = ContentPoint(point);
            for (int i = 0; i < blocks.Count; i++) if (blocks[i].Contains(point)) return i;
            return -1;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus(); SelectedIndex = Hit(e.Location); dragIndex = selected; mouseDown = e.Location;
        }
        internal void DeleteSelected()
        {
            if (!Enabled || library == null || selected < 0 || selected >= library.Steps.Count) return;
            int index = selected;
            library.Steps.RemoveAt(index);
            dragIndex = insertion = hover = -1;
            tooltip.SetToolTip(this, "");
            Rebuild(); SelectedIndex = Math.Min(index, library.Steps.Count - 1);
            if (SequenceChanged != null) SequenceChanged(this, EventArgs.Empty);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Delete && Focused && Enabled)
            {
                DeleteSelected(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragIndex = -1; base.OnMouseUp(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left && dragIndex >= 0 &&
                !new Rectangle(mouseDown.X - SystemInformation.DragSize.Width / 2, mouseDown.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height).Contains(e.Location))
            {
                var step = library.Steps[dragIndex]; dragIndex = -1;
                DoDragDrop(new TimelineDrag { Step = step }, DragDropEffects.Move); return;
            }
            int index = Hit(e.Location);
            if (hover != index)
            {
                hover = index;
                tooltip.SetToolTip(this, index < 0 ? "" : library.Clips.Find(c => c.Id == library.Steps[index].ClipId).Name + " · " + (Duration(library.Steps[index]) / 1000).ToString("0.###") + "초");
            }
        }
        private TimelineDrag Payload(DragEventArgs e)
        {
            return e.Data.GetDataPresent(typeof(TimelineDrag)) ? e.Data.GetData(typeof(TimelineDrag)) as TimelineDrag : null;
        }
        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnDragOver(e); }
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e); var data = Payload(e);
            if (!Enabled || library == null || data == null ||
                (data.Step != null ? !library.Steps.Contains(data.Step) : data.Clip == null || !library.Clips.Contains(data.Clip)))
            { e.Effect = DragDropEffects.None; return; }
            e.Effect = data.Step == null ? DragDropEffects.Copy : DragDropEffects.Move;
            var point = PointToClient(new Point(e.X, e.Y));
            if (point.X < 28) AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X - 24), 0);
            else if (point.X > ClientSize.Width - 28) AutoScrollPosition = new Point(-AutoScrollPosition.X + 24, 0);
            point = ContentPoint(point); insertion = blocks.Count;
            for (int i = 0; i < blocks.Count; i++) if (point.X < blocks[i].Left + blocks[i].Width / 2) { insertion = i; break; }
            Invalidate();
        }
        protected override void OnDragLeave(EventArgs e) { insertion = -1; Invalidate(); base.OnDragLeave(e); }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e); var data = Payload(e);
            if (!Enabled || data == null || insertion < 0) return;
            int destination = Math.Min(insertion, library.Steps.Count);
            ClipStep step;
            if (data.Step != null)
            {
                int source = library.Steps.IndexOf(data.Step); if (source < 0) return;
                step = data.Step; library.Steps.RemoveAt(source); if (source < destination) destination--;
            }
            else
            {
                if (data.Clip == null || !library.Clips.Contains(data.Clip)) return;
                step = new ClipStep { ClipId = data.Clip.Id, Repeat = 1, GapMs = 0 };
            }
            library.Steps.Insert(destination, step); insertion = -1; Rebuild(); SelectedIndex = destination;
            if (SequenceChanged != null) SequenceChanged(this, EventArgs.Empty);
        }
        protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
    }
}

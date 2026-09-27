using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoInput
{
    public sealed class MainForm : Form
    {
        private readonly CheckBox dry = new CheckBox { Text = "로그만 실행 (실제 입력 없음)", Checked = true, AutoSize = true };
        private readonly Button start = new Button { Text = "3초 후 조합 재생", AutoSize = true };
        private readonly Button stop = new Button { Text = "모두 중지", AutoSize = true, Enabled = false };
        private readonly Button hotkeyButton = new Button { Text = "키 설정", AutoSize = true };
        private readonly Label hint = new Label { AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        private HotkeySettings hotkeys = new HotkeySettings();
        private bool settingsOpen;
        private readonly Button chooseTarget = new Button { Text = "3초 후 대상 창 지정", AutoSize = true };
        private readonly Label targetLabel = new Label { Text = "대상: 지정되지 않음", AutoSize = true };
        private IntPtr savedTarget;
        private uint savedProcess;
        private readonly Label status = new Label { Text = "중지됨", AutoSize = true };
        private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private CancellationTokenSource cancellation;
        private bool hotkeyReady, closeRequested;
        private RecordingStudio studio;
        private KeyboardRecorder recorder;
        private readonly System.Windows.Forms.Timer recordTimer = new System.Windows.Forms.Timer { Interval = 40 };

        public MainForm()
        {
            Text = "AutoInput · 키 녹화 및 조합";
            Font = new Font("맑은 고딕", 10);
            ClientSize = new Size(1200, 720);
            MinimumSize = new Size(1120, 650);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(8, 8, 12, 0), Margin = new Padding(0)
            };
            toolbar.Controls.Add(hotkeyButton);
            Controls.Add(root); root.Controls.Add(toolbar, 0, 0); root.Controls.Add(split, 0, 1);
            split.Controls.Add(layout, 0, 0);
            studio = new RecordingStudio(AppendLog); split.Controls.Add(studio, 1, 0);
            layout.Controls.Add(dry, 0, 0); layout.SetColumnSpan(dry, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(chooseTarget);
            layout.Controls.Add(buttons, 0, 1); layout.SetColumnSpan(buttons, 2);
            layout.Controls.Add(status, 0, 2); layout.SetColumnSpan(status, 2);
            layout.Controls.Add(hint, 0, 3); layout.SetColumnSpan(hint, 2);
            var test = new TextBox { Dock = DockStyle.Fill, AccessibleName = "키 입력 테스트" };
            AddRow(layout, 4, "키 입력 테스트", test);
            layout.Controls.Add(targetLabel, 0, 5); layout.SetColumnSpan(targetLabel, 2);
            layout.Controls.Add(log, 0, 6); layout.SetColumnSpan(log, 2);
            start.Click += async (s, e) => await StartAsync();
            chooseTarget.Click += async (s, e) => await ChooseTargetAsync();
            stop.Click += (s, e) => Cancel();
            hotkeyButton.Click += (s, e) => EditHotkeys();
            try { hotkeys = HotkeySettings.Load(); }
            catch (Exception ex) { AppendLog("단축키 파일 오류, 기본값을 사용합니다: " + ex.Message); }
            UpdateHotkeyLabels();
            recordTimer.Tick += (s, e) => {
                if (recorder == null) return;
                if (recorder.FocusLost || recorder.LimitReached || !TargetActive() || recorder.Elapsed >= 600000) StopRecording();
                else status.Text = "● 녹화 중 " + (recorder.Elapsed / 1000.0).ToString("F1") + "초 · " + hotkeys.Display(1) + " 종료";
            };
            FormClosing += (s, e) => StopRecording();
            FormClosed += (s, e) => recordTimer.Dispose();
            FormClosing += (s, e) => { if (cancellation != null) { e.Cancel = true; closeRequested = true; Cancel(); } };
        }

        private static void AddRow(TableLayoutPanel layout, int row, string title, Control control)
        {
            layout.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 8, 0, 8) }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void CaptureTarget()
        {
            IntPtr target = KeyboardSender.GetForegroundWindow();
            if (target == IntPtr.Zero) { AppendLog("대상 창을 찾을 수 없습니다."); return; }
            savedTarget = target; savedProcess = KeyboardSender.ProcessOf(target);
            targetLabel.Text = "대상: " + KeyboardSender.TitleOf(target);
            AppendLog("대상 지정 완료 · " + hotkeys.Display(0) + " 녹화 / " + hotkeys.Display(2) + " 조합 재생");
        }
        private void UpdateHotkeyLabels()
        {
            hint.Text = "녹화: " + hotkeys.Display(0) + " 시작 / " + hotkeys.Display(1) + " 중지\n"
                + "조합: " + hotkeys.Display(2) + " 시작 / " + hotkeys.Display(3) + " 중지\n"
                + hotkeys.Display(4) + " 모두 중지 · " + hotkeys.Display(5) + " 현재 창 지정\n"
                + "재생 버튼은 3초 후 실행합니다. 창이 바뀌면 중지됩니다.";
            stop.Text = "모두 중지 (" + hotkeys.Display(4) + ")";
        }
        private void UnregisterKeys()
        {
            for (int i = 0; i < HotkeySettings.Labels.Length; i++) KeyboardSender.UnregisterHotKey(Handle, 101 + i);
        }
        private bool RegisterKeys(HotkeySettings settings, out string error)
        {
            for (int i = 0; i < settings.Keys.Length; i++)
            {
                if (!KeyboardSender.RegisterHotKey(Handle, 101 + i, 0x4000, (uint)settings.Keys[i]))
                {
                    error = HotkeySettings.Labels[i] + " (" + settings.Display(i) + ") 등록 실패. 다른 프로그램의 사용 여부를 확인하거나 키를 바꾸세요.";
                    UnregisterKeys(); return false;
                }
            }
            error = null; return true;
        }
        private void EditHotkeys()
        {
            if (cancellation != null || recorder != null) return;
            settingsOpen = true;
            UnregisterKeys(); hotkeyReady = false;
            try
            {
                using (var dialog = new HotkeyDialog(hotkeys))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var previous = hotkeys;
                    UnregisterKeys(); hotkeyReady = false;
                    string error;
                    if (RegisterKeys(dialog.Result, out error))
                    {
                        try
                        {
                            dialog.Result.Save(); hotkeys = dialog.Result; hotkeyReady = true;
                            UpdateHotkeyLabels(); status.Text = "단축키 적용 완료";
                            AppendLog("단축키를 적용하고 저장했습니다."); return;
                        }
                        catch (Exception ex) { error = "단축키 저장 실패: " + ex.Message; UnregisterKeys(); }
                    }
                    string restoreError;
                    hotkeyReady = RegisterKeys(previous, out restoreError);
                    if (!hotkeyReady) error += "\n기존 단축키 복원도 실패했습니다: " + restoreError;
                    status.Text = hotkeyReady ? "기존 단축키 유지" : "단축키 등록 실패 · 설정에서 변경하세요";
                    AppendLog(error);
                    MessageBox.Show(this, error, "단축키 적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                if (!hotkeyReady)
                {
                    string error;
                    hotkeyReady = RegisterKeys(hotkeys, out error);
                    if (!hotkeyReady) { status.Text = "단축키 복원 실패 · 키 설정을 확인하세요"; AppendLog(error); }
                }
                settingsOpen = false; start.Enabled = hotkeyReady;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            hotkeyReady = RegisterKeys(hotkeys, out error);
            start.Enabled = hotkeyReady;
            if (!hotkeyReady) { status.Text = "단축키 등록 실패 · 설정에서 변경하세요"; AppendLog(error); }
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterKeys();
            base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && hotkeyReady && !settingsOpen)
            {
                int action = message.WParam.ToInt32() - 101;
                // Delete가 전역 단축키여도 타임라인 편집에 포커스가 있으면 삭제를 우선합니다.
                if (action >= 0 && action < hotkeys.Keys.Length && hotkeys.Keys[action] == (int)Keys.Delete
                    && studio.DeleteFocusedStep()) return;
                switch (message.WParam.ToInt32() - 101)
                {
                    case 0: StartRecording(); break;
                    case 1: StopRecording(); break;
                    case 2: StartFromHotkey(); break;
                    case 3: if (cancellation != null) cancellation.Cancel(); break;
                    case 4: Cancel(); break;
                    case 5: if (cancellation == null && recorder == null) CaptureTarget(); break;
                }
            }
            base.WndProc(ref message);
        }
        private async void StartFromHotkey()
        {
            if (!hotkeyReady || cancellation != null || recorder != null) return;
            if (savedTarget == IntPtr.Zero || !KeyboardSender.IsWindow(savedTarget)
                || KeyboardSender.ProcessOf(savedTarget) != savedProcess)
            {
                AppendLog("대상 창을 먼저 지정하세요. 창을 다시 열었다면 재지정하세요.");
                return;
            }
            if (KeyboardSender.GetForegroundWindow() != savedTarget) return;
            await StartAsync(savedTarget);
        }

        private async Task ChooseTargetAsync()
        {
            if (cancellation != null || recorder != null) return;
            cancellation = new CancellationTokenSource();
            start.Enabled = chooseTarget.Enabled = hotkeyButton.Enabled = false;
            stop.Enabled = true;
            try
            {
                for (int i = 3; i > 0; i--)
                {
                    status.Text = i + "초 후 대상 지정 · 원하는 창을 클릭하세요";
                    await Task.Delay(1000, cancellation.Token);
                }
                CaptureTarget();
            }
            catch (OperationCanceledException) { AppendLog("대상 지정을 취소했습니다."); }
            catch (Exception ex) { AppendLog(ex.Message); }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                start.Enabled = hotkeyReady; chooseTarget.Enabled = hotkeyButton.Enabled = true; stop.Enabled = false;
                status.Text = "중지됨";
                if (closeRequested) Close();
            }
        }
        private bool TargetActive()
        {
            return savedTarget != IntPtr.Zero && KeyboardSender.IsWindow(savedTarget)
                && KeyboardSender.ProcessOf(savedTarget) == savedProcess
                && KeyboardSender.GetForegroundWindow() == savedTarget;
        }
        private void StartRecording()
        {
            if (recorder != null) return;
            if (cancellation != null || !hotkeyReady) return;
            if (!TargetActive()) { AppendLog("먼저 대상 창을 지정하고 해당 창에서 " + hotkeys.Display(0) + "을 누르세요."); return; }
            var next = new KeyboardRecorder();
            try
            {
                next.Start(studio.ClipName, TargetActive, hotkeys.Keys); recorder = next;
                studio.Enabled = start.Enabled = chooseTarget.Enabled = hotkeyButton.Enabled = false;
                stop.Enabled = true; recordTimer.Start();
                status.Text = "● 녹화 중 · " + hotkeys.Display(1) + " 종료";
            }
            catch (Exception ex) { next.Dispose(); AppendLog("녹화 시작 실패: " + ex.Message); }
        }
        private void StopRecording()
        {
            if (recorder == null) return;
            recordTimer.Stop();
            var clip = recorder.Stop(); recorder = null;
            studio.Enabled = chooseTarget.Enabled = hotkeyButton.Enabled = true; start.Enabled = hotkeyReady;
            stop.Enabled = false; status.Text = "녹화 종료";
            studio.AddClip(clip);
        }
        private void Cancel() { StopRecording(); if (cancellation != null) cancellation.Cancel(); }
        private void AppendLog(string message)
        {
            if (log.TextLength > 16000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(295, 272);
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }

        private async Task StartAsync(IntPtr immediateTarget = default(IntPtr))
        {
            if (cancellation != null || recorder != null || !hotkeyReady) return;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            bool preview = dry.Checked;
            bool infinite = studio.InfiniteRepeat.Checked;
            start.Enabled = chooseTarget.Enabled = studio.Enabled = dry.Enabled = hotkeyButton.Enabled = false;
            stop.Enabled = true;
            try
            {
                RecordingLibrary sequence = studio.Snapshot();
                foreach (var clip in sequence.Clips)
                    if (sequence.Steps.Exists(step => step.ClipId == clip.Id))
                        foreach (var frame in clip.Frames)
                            if (Array.IndexOf(hotkeys.Keys, frame.Key) >= 0)
                                throw new InvalidOperationException("클립 '" + clip.Name + "'의 " + (Keys)frame.Key + " 키가 현재 단축키와 겹칩니다. 단축키를 변경하세요.");
                for (int i = immediateTarget == IntPtr.Zero ? 3 : 0; i > 0; i--)
                {
                    status.Text = i + "초 후 시작 · 대상 창을 선택하세요";
                    await Task.Delay(1000, token);
                }
                IntPtr target = immediateTarget == IntPtr.Zero ? KeyboardSender.GetForegroundWindow() : immediateTarget;
                if (target == IntPtr.Zero) throw new InvalidOperationException("대상 창을 찾을 수 없습니다.");
                status.Text = (preview ? "로그 실행 중 · " : "키 입력 중 · ") + hotkeys.Display(3) + " 중지";
                if (infinite) status.Text = "∞ 무한 반복 · " + status.Text;
                await SequencePlayer.PlayAsync(sequence, (int)studio.Cycles.Value,
                    frame => {
                        if (!preview) KeyboardSender.Send((Keys)frame.Key, frame.Up, frame.Extended);
                        AppendLog((preview ? "[미리보기] " : "") + (Keys)frame.Key + (frame.Up ? " · 키 뗌" : " · 키 누름"));
                    }, () => KeyboardSender.GetForegroundWindow() == target, token, infinite);
                AppendLog("조합 재생 완료");
            }
            catch (OperationCanceledException) { AppendLog("중지했습니다."); }
            catch (Exception ex) { AppendLog(ex.Message); }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                start.Enabled = hotkeyReady;
                chooseTarget.Enabled = studio.Enabled = dry.Enabled = hotkeyButton.Enabled = true;
                stop.Enabled = false; status.Text = "중지됨";
                if (closeRequested) Close();
            }
        }
    }
}

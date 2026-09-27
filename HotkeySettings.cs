using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace AutoInput
{
    public class HotkeySettings
    {
        public int[] Keys { get; set; }
        public static readonly string[] Labels = { "녹화 시작", "녹화 중지", "조합 시작", "조합 중지", "모두 중지 / 대기 취소", "현재 창을 대상 지정" };
        public HotkeySettings() { Keys = new[] { 118, 120, 117, 119, 121, 122 }; }
        public HotkeySettings Copy() { return new HotkeySettings { Keys = (int[])Keys.Clone() }; }
        public static List<int> AllowedKeys()
        {
            var keys = new List<int>();
            for (int k = 112; k <= 122; k++) keys.Add(k); // F12는 Windows 디버거 예약 키
            for (int k = 65; k <= 90; k++) keys.Add(k);
            for (int k = 48; k <= 57; k++) keys.Add(k);
            keys.AddRange(new[] { 32, 27, 9, 13, 45, 46, 36, 35, 33, 34, 37, 38, 39, 40 });
            return keys;
        }
        public void Validate()
        {
            if (Keys == null || Keys.Length != Labels.Length) throw new InvalidDataException("단축키 설정 개수가 올바르지 않습니다.");
            var seen = new HashSet<int>(); var allowed = AllowedKeys();
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!allowed.Contains(Keys[i])) throw new InvalidDataException(Labels[i] + ": 지원하지 않는 키입니다.");
                if (!seen.Add(Keys[i])) throw new InvalidDataException("단축키가 중복됩니다: " + (System.Windows.Forms.Keys)Keys[i]);
            }
        }
        public string Display(int index) { return ((System.Windows.Forms.Keys)Keys[index]).ToString(); }
        internal static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkeys.xml"); } }
        internal static HotkeySettings Load()
        {
            if (!File.Exists(FilePath)) return new HotkeySettings();
            using (var stream = File.OpenRead(FilePath))
            {
                var settings = (HotkeySettings)new XmlSerializer(typeof(HotkeySettings)).Deserialize(stream);
                settings.Validate(); return settings;
            }
        }
        internal void Save()
        {
            Validate(); string temp = FilePath + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(HotkeySettings)).Serialize(stream, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
    }

    internal sealed class HotkeyInput : TextBox
    {
        internal int SelectedKey { get; private set; }
        internal event Action<string> InputMessage;
        internal HotkeyInput(int key)
        {
            ReadOnly = true; ShortcutsEnabled = false; Dock = DockStyle.Fill;
            Cursor = Cursors.Hand; TextAlign = HorizontalAlignment.Center;
            SetKey(key);
            Enter += (s, e) => { BackColor = Color.LightCyan; SelectAll(); };
            Leave += (s, e) => BackColor = SystemColors.Window;
            BackColor = SystemColors.Window;
        }
        internal void SetKey(int key) { SelectedKey = key; Text = ((Keys)key).ToString(); }
        private void CaptureKey(Keys keyData)
        {
            int key = (int)(keyData & Keys.KeyCode);
            if ((keyData & Keys.Modifiers) != Keys.None || !HotkeySettings.AllowedKeys().Contains(key))
            {
                if (InputMessage != null) InputMessage("지원하는 단일 키를 눌러주세요. Ctrl/Alt/Shift 조합과 F12는 지원하지 않습니다.");
                return;
            }
            SetKey(key); SelectAll();
            if (InputMessage != null) InputMessage(Text + " 지정됨 · 적용을 누르면 저장됩니다.");
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            CaptureKey(keyData); return true; // Tab/Enter/Escape도 이 칸에서는 설정할 키로 처리합니다.
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            CaptureKey(e.KeyData); e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    internal sealed class HotkeyDialog : Form
    {
        internal HotkeySettings Result;
        internal HotkeyDialog(HotkeySettings current)
        {
            Text = "키 설정"; Font = new Font("맑은 고딕", 10);
            ClientSize = new Size(520, 400); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            Controls.Add(layout); var inputs = new HotkeyInput[HotkeySettings.Labels.Length];
            var hint = new Label { Text = "변경할 키 칸을 클릭하고 원하는 키를 누르세요.\n입력 후 적용을 누르면 저장됩니다. 기능키(F1~F11)를 권장합니다.", AutoSize = true, MaximumSize = new Size(480, 0) };
            for (int i = 0; i < inputs.Length; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                inputs[i] = new HotkeyInput(current.Keys[i]);
                inputs[i].AccessibleName = HotkeySettings.Labels[i] + " 키 입력";
                inputs[i].InputMessage += message => hint.Text = message;
                layout.Controls.Add(new Label { Text = HotkeySettings.Labels[i], AutoSize = true }, 0, i);
                layout.Controls.Add(inputs[i], 1, i);
            }
            layout.Controls.Add(hint, 0, 6); layout.SetColumnSpan(hint, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var apply = new Button { Text = "적용", AutoSize = true };
            var defaults = new Button { Text = "기본값", AutoSize = true };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { apply, defaults, cancel });
            layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2); CancelButton = cancel;
            defaults.Click += (s, e) => { var preset = new HotkeySettings(); for (int i = 0; i < inputs.Length; i++) inputs[i].SetKey(preset.Keys[i]); };
            apply.Click += (s, e) => {
                var next = current.Copy(); for (int i = 0; i < inputs.Length; i++) next.Keys[i] = inputs[i].SelectedKey;
                try { next.Validate(); Result = next; DialogResult = DialogResult.OK; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "단축키 확인", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
        }
    }
}

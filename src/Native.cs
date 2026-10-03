using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ReplyOrbs {
    public static class Native {
        static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;
        public static readonly IntPtr PasteInputMarker = new IntPtr(BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) | 1L);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rectangle { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct Mouse { public Point Pt; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] public struct Keyboard { public uint Key, Scan, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KeyInput Key; [FieldOffset(0)] public MouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        public delegate void EventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        public static IntPtr Root(IntPtr hwnd) { return GetAncestor(hwnd, 2); }
        public static bool Own(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid == CurrentProcessId; }
        public static string ProcessName(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); try { using (var process = Process.GetProcessById((int)pid)) return process.ProcessName.ToLowerInvariant(); } catch { return ""; } }
        public static bool Paste() {
            if (GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) return false;
            var inputs = new[] { Key(0x11, 0), Key(0x56, 0), Key(0x56, 2), Key(0x11, 2) };
            return SendInput(4, inputs, Marshal.SizeOf(typeof(Input))) == 4;
        }
        static Input Key(ushort code, uint flags) { return new Input { Type = 1, Data = new InputUnion { Key = new KeyInput { Key = code, Flags = flags, Extra = PasteInputMarker } } }; }
        public static bool IsOwnPasteKey(Keyboard key) { return (key.Flags & 0x10) != 0 && key.Extra == PasteInputMarker && (key.Key == 0x11 || key.Key == 0x56); }
        public static DataObject Data(Reply reply, Store store, bool files) {
            var d = new DataObject();
            if (!string.IsNullOrEmpty(reply.Text)) d.SetData(DataFormats.UnicodeText, reply.Text);
            if (files && reply.Files.Count > 0) {
                var list = new StringCollection();
                foreach (var f in reply.Files) { var path = store.Resolve(f); if (!File.Exists(path)) throw new IOException("Вложение отсутствует: " + f.Name); list.Add(path); }
                d.SetFileDropList(list);
                d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
            }
            return d;
        }
        public static async Task<uint> Copy(DataObject data, CancellationToken ct = default(CancellationToken)) {
            for (int attempt = 0; attempt < 5; attempt++) {
                ct.ThrowIfCancellationRequested();
                try { Clipboard.SetDataObject(data, true); return GetClipboardSequenceNumber(); }
                catch (ExternalException) { if (attempt == 4) throw new IOException("Буфер обмена занят. Нажмите ещё раз."); }
                await Task.Delay(75, ct);
            }
            throw new IOException("Буфер обмена недоступен.");
        }
    }
    public sealed class Probe {
        public IntPtr Window;
        public string Selection;
        public Rect Bounds;
        public string Identity;
        public bool Eligible;
        public bool ClickInside;
    }
    public sealed class SelectionProbe {
        int busy;
        public bool FixtureAllowed;
        // One in-flight provider call. A broken provider cannot create an unbounded thread queue.
        public async Task<Probe> Read(Native.Point point, IntPtr hwnd, bool selection, bool verifyClick = false) {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return null;
            var tcs = new TaskCompletionSource<Probe>();
            var worker = new Thread(delegate() {
                try { tcs.SetResult(Query(point, hwnd, selection, verifyClick)); } catch { tcs.SetResult(null); } finally { Interlocked.Exchange(ref busy, 0); }
            }) { IsBackground = true, Name = "ReplyOrbs UIA probe" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(700));
            return completed == tcs.Task ? await tcs.Task : null;
        }
        Probe Query(Native.Point point, IntPtr hwnd, bool selection, bool verifyClick) {
            if (Native.GetForegroundWindow() != hwnd || Native.Own(hwnd) && !FixtureAllowed) return null;
            var result = new Probe { Window = hwnd };
            if (selection) {
                var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
                if (TrySelection(element, result)) return result;
                if (TrySelection(AutomationElement.FocusedElement, result)) return result;
            }
            var focused = AutomationElement.FocusedElement;
            if (focused == null || !focused.Current.HasKeyboardFocus || focused.Current.IsPassword) return result;
            result.Identity = string.Join(".", focused.GetRuntimeId().Select(i => i.ToString()).ToArray());
            if (verifyClick) result.ClickInside = HitComposer(point, focused);
            var process = Native.ProcessName(hwnd);
            // Only known messenger compose labels, never arbitrary/search fields.
            bool allowedProcess = process == "telegram" || process == "max" || process == "chrome" || process == "msedge" || FixtureAllowed && Native.Own(hwnd);
            var name = focused.Current.Name.Trim().ToLowerInvariant();
            var names = new[] { "сообщение", "написать сообщение", "введите сообщение", "type a message", "message", "message input", "текст сообщения", "написать сообщение..." };
            if (!allowedProcess || !names.Contains(name) || focused.Current.ControlType != ControlType.Edit || !focused.Current.IsEnabled) return result;
            // Browser source must actually be a WhatsApp/MAX document, not another tab.
            if (process == "chrome" || process == "msedge") {
                bool chatDocument = false; var parent = focused;
                for (int i = 0; i < 12 && parent != null; i++, parent = TreeWalker.ControlViewWalker.GetParent(parent)) {
                    if (parent.Current.ControlType != ControlType.Document) continue;
                    object value; string url = "";
                    if (parent.TryGetCurrentPattern(ValuePattern.Pattern, out value)) url = ((ValuePattern)value).Current.Value;
                    Uri uri;
                    if (Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Host == "web.whatsapp.com" || uri.Host == "web.max.ru")) chatDocument = true;
                }
                if (!chatDocument) return result;
            }
            object pattern;
            // Without an observable empty value, auto paste stays disabled.
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) {
                var value = (ValuePattern)pattern;
                result.Eligible = !value.Current.IsReadOnly && value.Current.Value.Length == 0;
            }
            if (result.Eligible && focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
                var selections = ((TextPattern)pattern).GetSelection();
                result.Eligible = selections.Length == 1 && selections[0].GetText(1).Length == 0;
            }
            if (Native.GetForegroundWindow() != hwnd || !Automation.Compare(focused, AutomationElement.FocusedElement)) return null;
            return result;
        }
        static bool HitComposer(Native.Point point, AutomationElement focused) {
            var position = new System.Windows.Point(point.X, point.Y);
            if (!focused.Current.BoundingRectangle.Contains(position)) return false;
            var hit = AutomationElement.FromPoint(position);
            for (int i = 0; i < 8 && hit != null; i++, hit = TreeWalker.RawViewWalker.GetParent(hit)) {
                if (hit.Current.IsPassword || hit.Current.ProcessId != focused.Current.ProcessId) return false;
                if (Automation.Compare(hit, focused)) return true;
                var type = hit.Current.ControlType;
                if (type == ControlType.Button || type == ControlType.ScrollBar || type == ControlType.MenuItem) return false;
            }
            return false;
        }
        static bool TrySelection(AutomationElement element, Probe result) {
            uint pid; Native.GetWindowThreadProcessId(result.Window, out pid);
            for (int i = 0; i < 7 && element != null; i++, element = TreeWalker.RawViewWalker.GetParent(element)) {
                if (element.Current.IsPassword || element.Current.ProcessId != pid) return false;
                object pattern;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) continue;
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges.Length != 1) continue;
                var text = ranges[0].GetText(12001).Trim();
                if (text.Length < 3 || text.Length > 12000) continue;
                var rect = ranges[0].GetBoundingRectangles();
                result.Selection = text;
                if (rect.Length > 0) result.Bounds = rect[rect.Length - 1];
                return Native.GetForegroundWindow() == result.Window;
            }
            return false;
        }
    }
    public sealed class DesktopEvents : IDisposable {
        readonly Dispatcher dispatcher;
        readonly Native.HookProc mouseProc, keyProc;
        readonly Native.EventProc eventProc;
        IntPtr mouse, keyboard, focus, foreground;
        long revision;
        public long Revision { get { return Interlocked.Read(ref revision); } }
        public event Action<Native.Point, IntPtr, long> Click;
        public event Action PointerDown;
        public event Action<uint> Key;
        public event Action<IntPtr> Focus;
        public event Action SelectionKey;
        public DesktopEvents(Dispatcher dispatcher) {
            this.dispatcher = dispatcher;
            mouseProc = Mouse; keyProc = Keyboard;
            eventProc = delegate(IntPtr h, uint e, IntPtr w, int o, int c, uint t, uint time) { Interlocked.Increment(ref revision); var window = Native.GetForegroundWindow(); Post(delegate { if (Focus != null) Focus(window); }); };
            var module = Native.GetModuleHandle(null);
            mouse = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            keyboard = Native.SetWindowsHookEx(13, keyProc, module, 0);
            focus = Native.SetWinEventHook(0x8005, 0x8005, IntPtr.Zero, eventProc, 0, 0, 0);
            foreground = Native.SetWinEventHook(3, 3, IntPtr.Zero, eventProc, 0, 0, 0);
        }
        void Post(Action action) { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(action, DispatcherPriority.Input); }
        IntPtr Mouse(int code, IntPtr w, IntPtr l) {
            if (code >= 0 && (w.ToInt32() == 0x201 || w.ToInt32() == 0x204 || w.ToInt32() == 0x207)) {
                Interlocked.Increment(ref revision); Post(delegate { if (PointerDown != null) PointerDown(); });
            }
            if (code >= 0 && w.ToInt32() == 0x202) {
                var data = (Native.Mouse)Marshal.PtrToStructure(l, typeof(Native.Mouse));
                var point = data.Pt; var hwnd = Native.Root(Native.WindowFromPoint(point));
                var version = Interlocked.Increment(ref revision);
                Post(delegate { if (Click != null) Click(point, hwnd, version); });
            }
            return Native.CallNextHookEx(mouse, code, w, l);
        }
        IntPtr Keyboard(int code, IntPtr w, IntPtr l) {
            if (code >= 0 && (w.ToInt32() == 0x100 || w.ToInt32() == 0x104)) {
                var data = (Native.Keyboard)Marshal.PtrToStructure(l, typeof(Native.Keyboard));
                if (!Native.IsOwnPasteKey(data)) { Interlocked.Increment(ref revision); var key = data.Key; Post(delegate { if (Key != null) Key(key); }); }
            }
            if (code >= 0 && w.ToInt32() == 0x101 && Native.GetAsyncKeyState(0x10) < 0) Post(delegate { if (SelectionKey != null) SelectionKey(); });
            return Native.CallNextHookEx(keyboard, code, w, l);
        }
        public bool Available { get { return mouse != IntPtr.Zero && keyboard != IntPtr.Zero && focus != IntPtr.Zero && foreground != IntPtr.Zero; } }
        public void Dispose() { if (mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(mouse); if (keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboard); if (focus != IntPtr.Zero) Native.UnhookWinEvent(focus); if (foreground != IntPtr.Zero) Native.UnhookWinEvent(foreground); }
    }
}

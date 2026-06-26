using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace NanoAgent.VS.ToolWindows
{
    /// <summary>
    /// Makes a TextBlock's text selectable + copyable (Ctrl+C / right-click).
    /// WPF has no selectable TextBlock, so reflect into the internal TextEditor that
    /// RichTextBox/TextBox already use and attach it read-only. No rendering rewrite, formatting
    /// and hyperlinks survive. Ceiling: selection is per-TextBlock, not contiguous across blocks —
    /// switch the whole conversation to a read-only RichTextBox/FlowDocument if that's needed.
    /// Usage: local:SelectableTextBlock.IsEnabled="True" in XAML, or SetIsEnabled(tb, true) in code.
    /// </summary>
    public static class SelectableTextBlock
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SelectableTextBlock), new PropertyMetadata(false, OnChanged));

        public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject o, bool v) => o.SetValue(IsEnabledProperty, v);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb && e.NewValue is true) CreateFor(tb);
        }

        // ── reflection into System.Windows.Documents.TextEditor (the engine behind TextBox/RichTextBox) ──
        private static readonly Type EditorType = Type.GetType(
            "System.Windows.Documents.TextEditor, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")!;
        private static readonly PropertyInfo IsReadOnly = EditorType.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly PropertyInfo TextView = EditorType.GetProperty("TextView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Registers the class-level mouse/keyboard command handlers (drag-select, Ctrl+C). Without this the editor is inert.
        private static readonly MethodInfo Register = EditorType.GetMethod(
            "RegisterCommandHandlers", BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(Type), typeof(bool), typeof(bool), typeof(bool) }, null)!;
        private static readonly PropertyInfo TextContainer = typeof(TextBlock).GetProperty("TextContainer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly PropertyInfo ContainerTextView =
            Type.GetType("System.Windows.Documents.ITextContainer, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")!
                .GetProperty("TextView")!;

        private static bool _registered;

        private static void CreateFor(TextBlock tb)
        {
            try
            {
                if (!_registered)
                {
                    // acceptsRichContent: true, readOnly: true, registerEventListeners: true
                    Register.Invoke(null, new object[] { typeof(TextBlock), true, true, true });
                    _registered = true;
                }

                tb.Focusable = true; // editor needs the TextBlock focusable to own a selection

                object container = TextContainer.GetValue(tb);
                object editor = Activator.CreateInstance(
                    EditorType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
                    null, new[] { container, tb, false }, null)!;
                IsReadOnly.SetValue(editor, true);
                TextView.SetValue(editor, ContainerTextView.GetValue(container));
            }
            catch { /* internal API moved; degrade to non-selectable */ }
        }
    }
}

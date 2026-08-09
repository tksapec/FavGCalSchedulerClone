using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class TextEditingBehavior
{
    public static void Attach(TextBoxBase textBox)
    {
        textBox.ContextMenu = CreateContextMenu(textBox);
    }

    public static void Attach(ComboBox comboBox)
    {
        comboBox.Loaded += (_, _) => AttachEditableTextBox(comboBox);
    }

    public static void Attach(DatePicker datePicker)
    {
        datePicker.Loaded += (_, _) =>
        {
            datePicker.ApplyTemplate();
            if (datePicker.Template.FindName("PART_TextBox", datePicker) is DatePickerTextBox editor)
            {
                Attach(editor);
            }
        };
    }

    private static void AttachEditableTextBox(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox editor)
        {
            Attach(editor);
        }
    }

    private static ContextMenu CreateContextMenu(TextBoxBase editor)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("切り取り", ApplicationCommands.Cut, editor));
        menu.Items.Add(CreateMenuItem("コピー", ApplicationCommands.Copy, editor));
        menu.Items.Add(CreateMenuItem("貼り付け", ApplicationCommands.Paste, editor));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("すべて選択", ApplicationCommands.SelectAll, editor));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedUICommand command, TextBoxBase editor) =>
        new() { Header = header, Command = command, CommandTarget = editor };
}

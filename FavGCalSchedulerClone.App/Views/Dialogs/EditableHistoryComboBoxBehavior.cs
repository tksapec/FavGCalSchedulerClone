using System.Windows.Controls;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class EditableHistoryComboBoxBehavior
{
    public static void Attach(ComboBox comboBox)
    {
        comboBox.IsEditable = true;
        comboBox.IsTextSearchEnabled = true;
        TextEditingBehavior.Attach(comboBox);
        comboBox.Loaded += (_, _) => ConfigureEditor(comboBox);
    }

    private static void ConfigureEditor(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is not TextBox editor)
        {
            return;
        }

        editor.IsReadOnly = false;
        editor.AcceptsReturn = false;
    }
}
